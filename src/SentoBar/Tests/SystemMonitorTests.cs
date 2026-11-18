using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace SentoBar;

internal static class SystemMonitorTests
{
    internal static void Run(Action<string,bool> check)
    {
        HardwareMonitorTests.Run(check);
        CheckDisplayPreferences(check);
        var unavailable=SystemMonitor.CaptureProcesses(false,()=>throw new InvalidOperationException("Enumeration failed"));
        check("Failed process enumeration leaves both counts unavailable",unavailable.Count==null && unavailable.Threads==null && unavailable.Top.Length==0);
        check("Unavailable process counts render explicitly",SystemMonitor.ProcessCounts(unavailable.Count,unavailable.Threads,unavailable.Partial)=="Unavailable");
        var empty=SystemMonitor.CaptureProcesses(false,()=>Array.Empty<Process>());
        check("Successful empty enumeration remains a measured zero",empty.Count==0 && empty.Threads==0 && !empty.Partial && SystemMonitor.ProcessCounts(empty.Count,empty.Threads,empty.Partial)=="0 / 0");
        // Unassociated Process objects cannot expose Threads: no native process inspection is needed.
        var partial=SystemMonitor.CaptureProcesses(false,()=>new[]{new Process()});
        check("Per-process thread failure retains available process count",partial.Count==1 && partial.Threads==0 && partial.Partial);
        check("Partial thread count retains its lower-bound marker",SystemMonitor.ProcessCounts(partial.Count,partial.Threads,partial.Partial)=="1 / ≥ 0");
        check("Network baseline is sampling",SystemMonitor.Rate(100,null,2)==null);
        check("Network delta uses elapsed seconds",SystemMonitor.Rate(1100,100,2)==500);
        check("Unchanged network counter is a real zero",SystemMonitor.Rate(100,100,2)==0);
        check("Counter reset starts a new baseline",SystemMonitor.Rate(10,100,2)==null);
        check("Invalid elapsed intervals do not produce rates",SystemMonitor.Rate(100,10,0)==null && SystemMonitor.Rate(100,10,double.NaN)==null);
        check("Binary byte units are explicit",SystemMonitor.Bytes(0)=="0 B" && SystemMonitor.Bytes(1024)=="1 KiB" && SystemMonitor.Bytes(1073741824)=="1 GiB");
        check("Unprimed rate is never shown as zero",SystemMonitor.RateText(null)=="Sampling…" && SystemMonitor.RateText(0)=="0 B/s");
        check("Uptime retains days",SystemMonitor.Uptime(TimeSpan.FromMinutes(1501))=="1d 1h 1m");
        MonitorSnapshot Sample(long idle,long total)=>new(0,idle,total,1,2,TimeSpan.Zero,0,0,false,Array.Empty<MonitorNetwork>(),Array.Empty<MonitorDrive>(),Array.Empty<MonitorProcess>(),Array.Empty<string>());
        check("CPU requires a baseline",SystemMonitor.Cpu(Sample(10,20),null)==null);
        check("CPU excludes idle time",SystemMonitor.Cpu(Sample(40,200),Sample(10,100))==70);
        check("CPU reset or zero interval is unavailable",SystemMonitor.Cpu(Sample(1,2),Sample(10,100))==null && SystemMonitor.Cpu(Sample(10,100),Sample(10,100))==null);
    }

    private static void CheckDisplayPreferences(Action<string,bool> check)
    {
        var settings=JsonSerializer.Deserialize<Settings>("{}")!;
        settings.Normalize();
        var display=settings.SystemDisplay;
        check("Old profiles retain CPU, memory and their network and storage detail tabs",
            display.Cpu && display.Memory && settings.MonitorNetwork && settings.MonitorStorage);

        var volumeA=new MonitorDeviceChoice("volume-a","C:\\");
        var volumeB=new MonitorDeviceChoice("volume-b","D:\\");
        display.StorageDevices=null;
        check("Unrestricted device selection includes new devices",
            SystemDisplayPreferences.Includes(display.StorageDevices,volumeB.Id));
        display.StorageDevices=new(){volumeA,new MonitorDeviceChoice("VOLUME-A","Old label"),new MonitorDeviceChoice("missing","G:\\"),new MonitorDeviceChoice("","Empty")};
        display.Cpu=false;
        settings.Normalize();
        var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        restored.Normalize();
        check("Display choices survive preference persistence with disconnected devices and case-insensitive IDs",
            !restored.SystemDisplay.Cpu && restored.SystemDisplay.StorageDevices?.Select(d=>d.Id).SequenceEqual(new[]{"volume-a","missing"})==true &&
            SystemDisplayPreferences.Includes(restored.SystemDisplay.StorageDevices,"VOLUME-A") &&
            !SystemDisplayPreferences.Includes(restored.SystemDisplay.StorageDevices,"volume-b"));
        check("An explicit empty selection never means all devices",
            !SystemDisplayPreferences.Includes(new(),volumeA.Id));

        var disks=new[]{new MonitorDrive("K:\\",512,1024,"VOLUME-A",(125,200)),new MonitorDrive("C:\\",512,1024,"different-volume")};
        var network=new[]{new MonitorNetwork("offline","Office Wi-Fi",null,null),new MonitorNetwork("other","VPN",2048,1024)};
        var snapshot=new MonitorSnapshot(0,null,null,null,null,TimeSpan.Zero,null,null,false,network,disks,
            Array.Empty<MonitorProcess>(),Array.Empty<string>());
        var diskRows=SystemDisplayRows.Storage(snapshot,restored.SystemDisplay.StorageDevices).ToArray();
        check("Disk readings follow volume identity after a drive letter change",
            diskRows.Length==2 && diskRows[0]==new MonitorDisplayRow("K:\\","512 B / 1 KiB","VOLUME-A") &&
            diskRows[1]==new MonitorDisplayRow("G:\\","Unavailable","missing"));
        var networkRows=SystemDisplayRows.Network(snapshot,null,new(){new("offline","Office Wi-Fi")}).ToArray();
        check("A disconnected selected adapter is unavailable and doesn't show another adapter",
            networkRows.Length==1 && networkRows[0]==new MonitorDisplayRow("Office Wi-Fi","Unavailable","offline"));
        var absentRows=SystemDisplayRows.Gpu(null,new(){new("gpu-a","Selected GPU")}).ToArray();
        var returnedGpus=new MonitorHardware(Array.Empty<HardwareCpu>(),Array.Empty<string>(),
            new[]{new HardwareGpu("new-counter-id",50,null,null,"gpu-a","Selected GPU"),new HardwareGpu("another-counter",21,null,null,"gpu-b","Other GPU")},
            Array.Empty<HardwareSensor>(),null);
        var resumedRows=SystemDisplayRows.Gpu(returnedGpus,new(){new("gpu-a","Selected GPU")}).ToArray();
        check("Temporary GPU loss retains its selection and a new counter identity still supplies its reading",
            absentRows.SequenceEqual(new[]{new MonitorDisplayRow("Selected GPU","Unavailable","gpu-a")}) &&
            resumedRows.SequenceEqual(new[]{new MonitorDisplayRow("Selected GPU","50%","gpu-a")}));

        var before=snapshot with {Timestamp=Stopwatch.Frequency,Idle=100,CpuTotal=200,
            Networks=new[]{new MonitorNetwork("other","VPN",1024,768)},
            Drives=new[]{disks[0] with {Name="E:\\",Counters=(100,100)}}};
        var after=snapshot with {Timestamp=Stopwatch.Frequency*3,Idle=140,CpuTotal=400,Available=0,Total=2048,Hardware=returnedGpus};
        var sample=new MonitorHistorySample(after.Timestamp,after,before);
        check("Dashboard charts use CPU and occupied RAM with actual percent endpoints",
            sample.Reading(MonitorMetric.Cpu,null)==80 && sample.Reading(MonitorMetric.Memory,null)==100 &&
            (sample with {Current=after with {Available=2048}}).Reading(MonitorMetric.Memory,null)==0);
        check("Dashboard chart history tracks individual GPUs and volumes by stable ID",
            sample.Reading(MonitorMetric.Gpu,"gpu-a")==50 && sample.Reading(MonitorMetric.Gpu,"gpu-b")==21 &&
            sample.Reading(MonitorMetric.Gpu,"unknown")==null && sample.Reading(MonitorMetric.Storage,"volume-a")==75);
        check("Disk activity uses elapsed idle time, including 0%, 100% and counter resets",
            SystemMonitor.DiskActivity(disks[0] with {Counters=(200,200)},before)==0 &&
            SystemMonitor.DiskActivity(disks[0] with {Counters=(100,200)},before)==100 &&
            SystemMonitor.DiskActivity(disks[0] with {Counters=(99,200)},before)==null &&
            SystemMonitor.DiskActivity(disks[0],null)==null);
        check("Network chart adds receive and send rates and has a gap for unavailable adapters",
            sample.Reading(MonitorMetric.Network,"other")==640 && sample.Reading(MonitorMetric.Network,"offline")==null &&
            (sample with {Previous=null}).Reading(MonitorMetric.Network,"other")==null);
    }
}
