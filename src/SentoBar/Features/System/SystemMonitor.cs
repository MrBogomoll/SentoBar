using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace SentoBar;

internal sealed record MonitorNetwork(string Id,string Name,long? Received,long? Sent);
internal sealed record MonitorDrive(string Name,long Free,long Total,string? VolumeId=null,(ulong Idle,ulong Time)? Counters=null)
{
    internal string DeviceId=>VolumeId??Name;
}
internal sealed record MonitorProcess(string Name,int Id,long WorkingSet);
internal sealed record MonitorSnapshot(long Timestamp,long? Idle,long? CpuTotal,ulong? Available,ulong? Total,
    TimeSpan Uptime,int? ProcessCount,int? ThreadCount,bool PartialThreads,
    IReadOnlyList<MonitorNetwork> Networks,IReadOnlyList<MonitorDrive> Drives,IReadOnlyList<MonitorProcess> Processes,
    string[] Errors,MonitorHardware? Hardware=null);

internal static class SystemMonitor
{
    [StructLayout(LayoutKind.Sequential)] private struct Memory
    {public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;}
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref Memory memory);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint,StringBuilder volumeName,int size);

    // Called only by the overlay's single background worker; all native handles are scoped here.
    internal static MonitorSnapshot Capture(bool network,bool storage,bool processes,bool processCounts=true,bool hardware=true,bool gpuOnly=false,
        bool includeOffline=false,bool sampleCpu=true,bool sampleMemory=true,bool sampleDiskActivity=false)
    {
        var errors=new List<string>();long? idle=null,totalCpu=null;
        if(sampleCpu)
        {
            if(GetSystemTimes(out long i,out long k,out long u)){idle=i;totalCpu=k+u;}
            else errors.Add(Localization.Text("CPU unavailable"));
        }
        var memory=new Memory{Length=(uint)Marshal.SizeOf<Memory>()};
        bool hasMemory=sampleMemory && GlobalMemoryStatusEx(ref memory);
        if(sampleMemory && !hasMemory)errors.Add(Localization.Text("Memory unavailable"));
        var (count,threads,partial,top)=processCounts || processes?CaptureProcesses(processes,Process.GetProcesses):(null,null,false,Array.Empty<MonitorProcess>());
        if(processCounts && count==null)errors.Add(Localization.Text("Process enumeration unavailable"));
        var networks=new List<MonitorNetwork>();
        if(network)try
        {
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())try
            {
                if(adapter.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;
                if(adapter.OperationalStatus==OperationalStatus.Up)
                {
                    var stats=adapter.GetIPStatistics();networks.Add(new(adapter.Id,adapter.Name,stats.BytesReceived,stats.BytesSent));
                }
                else if(includeOffline)networks.Add(new(adapter.Id,adapter.Name,null,null));
            }catch{errors.Add(Localization.Text("An interface is unavailable"));}
        }catch{errors.Add(Localization.Text("Network unavailable"));}
        var drives=new List<MonitorDrive>();
        if(storage)try
        {
            var volumeId=new StringBuilder(64);
            var activity=sampleDiskActivity?ReadDiskActivity():null;
            foreach(var drive in DriveInfo.GetDrives())try
            {
                if(drive.DriveType!=DriveType.Fixed || !drive.IsReady)continue;
                string? id=GetVolumeNameForVolumeMountPointW(drive.Name,volumeId,volumeId.Capacity)?volumeId.ToString():null;
                var counters=activity!=null && activity.TryGetValue(drive.Name.TrimEnd('\\'),out var reading)
                    ?reading:((ulong Idle,ulong Time)?)null;
                drives.Add(new(drive.Name,drive.TotalFreeSpace,drive.TotalSize,id,counters));
            }catch{errors.Add(Localization.Text("A volume is unavailable"));}
        }catch{errors.Add(Localization.Text("Storage unavailable"));}
        return new(Stopwatch.GetTimestamp(),idle,totalCpu,hasMemory?memory.AvailablePhysical:null,hasMemory?memory.TotalPhysical:null,
            TimeSpan.FromMilliseconds(Environment.TickCount64),count,threads,partial,networks,drives,
            top,errors.Distinct().ToArray(),hardware?HardwareMonitor.Capture(gpuOnly):null);
    }

    private static Dictionary<string,(ulong Idle,ulong Time)> ReadDiskActivity()
    {
        var counters=new Dictionary<string,(ulong,ulong)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            const string query="SELECT Name, PercentIdleTime, PercentIdleTime_Base FROM Win32_PerfRawData_PerfDisk_LogicalDisk";
            var options=new System.Management.EnumerationOptions
                {ReturnImmediately=true,Rewindable=false,Timeout=TimeSpan.FromMilliseconds(350)};
            using var search=new ManagementObjectSearcher(new ManagementScope("root\\cimv2"),new ObjectQuery(query),options);
            using var results=search.Get();
            foreach(ManagementBaseObject row in results)
            {
                using(row)
                    if(row["Name"] is string name && row["PercentIdleTime"] is ulong idle && row["PercentIdleTime_Base"] is ulong time)
                        counters[name]=(idle,time);
            }
        }
        catch(ManagementException){counters.Clear();}
        catch(COMException){counters.Clear();}
        catch(UnauthorizedAccessException){counters.Clear();}
        return counters;
    }

    internal static double? DiskActivity(MonitorDrive current,MonitorSnapshot? previous)
    {
        var drive=previous?.Drives.FirstOrDefault(d=>StringComparer.OrdinalIgnoreCase.Equals(d.DeviceId,current.DeviceId));
        if(current.Counters is not {} after || drive?.Counters is not {} before || after.Time<=before.Time || after.Idle<before.Idle)
            return null;
        // The precision timer measures idle time. Its complement is the fraction spent processing disk I/O.
        return Math.Clamp(100*(1-(after.Idle-before.Idle)/(double)(after.Time-before.Time)),0,100);
    }

    internal static (int? Count,int? Threads,bool Partial,MonitorProcess[] Top) CaptureProcesses(bool includeTop,Func<Process[]> enumerate)
    {
        int count=0,threads=0;bool partial=false;var top=new List<MonitorProcess>();
        try
        {
            foreach(var process in enumerate())
            {
                using(process)
                {
                    count++;
                    try{threads+=process.Threads.Count;}catch{partial=true;}
                    if(includeTop)try{top.Add(new(process.ProcessName,process.Id,process.WorkingSet64));}catch{}
                }
            }
        }
        catch{return (null,null,false,Array.Empty<MonitorProcess>());}
        return (count,threads,partial,top.OrderByDescending(p=>p.WorkingSet).Take(5).ToArray());
    }

    internal static string ProcessCounts(int? count,int? threads,bool partial)=>
        count is null || threads is null?Localization.Text("Unavailable"):$"{count} / {(partial?"≥ ":"")}{threads}";

    internal static double? Rate(long? current,long? previous,double seconds)=>current is long after && previous is long before && after>=before && seconds>0 && double.IsFinite(seconds)
        ?(after-before)/seconds:null;
    internal static double? Cpu(MonitorSnapshot current,MonitorSnapshot? previous)
    {
        if(previous?.CpuTotal is not long before || previous.Idle is not long beforeIdle || current.CpuTotal is not long after || current.Idle is not long afterIdle)return null;
        long elapsed=after-before,idle=afterIdle-beforeIdle;
        return elapsed>0 && idle>=0 && idle<=elapsed?100d*(elapsed-idle)/elapsed:null;
    }
    internal static string Bytes(double bytes)
    {
        if(!double.IsFinite(bytes) || bytes<0)return Localization.Text("Unavailable");
        string[] units={"B","KiB","MiB","GiB","TiB","PiB"};int unit=0;
        while(bytes>=1024 && unit<units.Length-1){bytes/=1024;unit++;}
        return bytes.ToString(unit==0?"0":"0.#",CultureInfo.InvariantCulture)+" "+Localization.Text(units[unit]);
    }
    internal static string RateText(double? rate)=>rate.HasValue?Bytes(rate.Value)+Localization.Text("/s"):Localization.Text("Sampling…");
    internal static string Uptime(TimeSpan value)=>Localization.Format("{0}d {1}h {2}m",(int)value.TotalDays,value.Hours,value.Minutes);
}
