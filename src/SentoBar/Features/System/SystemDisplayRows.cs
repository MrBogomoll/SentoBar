using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SentoBar;

internal enum MonitorMetric { Cpu, Memory, Gpu, Storage, Network }
internal sealed record MonitorDisplayRow(string Name,string Value,string? DeviceId=null);

internal static class SystemDisplayRows
{
    internal static bool HasExtraReadings(SystemDisplayPreferences preferences)=>
        preferences is {Gpu:true} or {Storage:true} or {Network:true};

    internal static MonitorDeviceChoice GpuChoice(HardwareGpu gpu,int index)=>
        new(gpu.DeviceId,gpu.Model??Localization.Format("Graphics adapter {0}",index+1));

    // A device can be disconnected or temporarily unreported. Keep explicitly selected names visible.
    private static IEnumerable<MonitorDisplayRow> SelectedRows<T>(IEnumerable<T> available,List<MonitorDeviceChoice>? selection,
        Func<T,MonitorDeviceChoice> describe,Func<T,string> value)
    {
        var shown=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var device in available)
        {
            var details=describe(device);
            if(SystemDisplayPreferences.Includes(selection,details.Id) && shown.Add(details.Id))
                yield return new(details.Name,value(device),details.Id);
        }
        if(selection!=null)
            foreach(var selected in selection.Where(device=>!shown.Contains(device.Id)))
                yield return new(selected.Name,Localization.Text("Unavailable"),selected.Id);
    }

    internal static IEnumerable<MonitorDisplayRow> Gpu(MonitorHardware? hardware,List<MonitorDeviceChoice>? selection)
    {
        var gpus=hardware?.Gpus??Array.Empty<HardwareGpu>();
        return SelectedRows(gpus.Select((gpu,index)=>(gpu,index)),selection,
            row=>GpuChoice(row.gpu,row.index),row=>HardwareMonitor.Percent(row.gpu.Utilization));
    }

    internal static IEnumerable<MonitorDisplayRow> Storage(MonitorSnapshot snapshot,List<MonitorDeviceChoice>? selection)=>
        SelectedRows(snapshot.Drives,selection,drive=>new(drive.DeviceId,drive.Name),
            drive=>SystemMonitor.Bytes(drive.Free)+" / "+SystemMonitor.Bytes(drive.Total));

    internal static IEnumerable<MonitorDisplayRow> StorageActivity(MonitorSnapshot snapshot,MonitorSnapshot? previous,List<MonitorDeviceChoice>? selection)=>
        SelectedRows(snapshot.Drives,selection,drive=>new(drive.DeviceId,drive.Name),
            drive=>SystemMonitor.DiskActivity(drive,previous) is double busy?$"{busy:0.#}%":
                Localization.Text(drive.Counters.HasValue?"Sampling…":"Unavailable"));

    internal static string UsedCapacity(double used,double total)=>
        Localization.Format("Used {0} of {1}",SystemMonitor.Bytes(used),SystemMonitor.Bytes(total));

    internal static IEnumerable<MonitorDisplayRow> Network(MonitorSnapshot snapshot,MonitorSnapshot? previous,List<MonitorDeviceChoice>? selection)
    {
        return SelectedRows(snapshot.Networks.Where(network=>network.Received.HasValue && network.Sent.HasValue),selection,
            network=>new(network.Id,network.Name),network=>
            {
                var (received,sent)=Rates(snapshot,previous,network);
                if(received==null && sent==null)return Localization.Text("Sampling…");
                return SystemMonitor.RateText(received)+" ↓  "+SystemMonitor.RateText(sent)+" ↑";
            });
    }
    internal static (double? Received,double? Sent) Rates(MonitorSnapshot snapshot,MonitorSnapshot? previous,MonitorNetwork network)
    {
        if(previous==null)return (null,null);
        var before=previous.Networks.FirstOrDefault(n=>StringComparer.OrdinalIgnoreCase.Equals(n.Id,network.Id));
        double seconds=(snapshot.Timestamp-previous.Timestamp)/(double)Stopwatch.Frequency;
        return (SystemMonitor.Rate(network.Received,before?.Received,seconds),SystemMonitor.Rate(network.Sent,before?.Sent,seconds));
    }

    internal static IEnumerable<(MonitorMetric Metric,string Heading,IEnumerable<MonitorDisplayRow> Rows)> Extra(SystemDisplayPreferences settings,
        MonitorSnapshot? snapshot,MonitorSnapshot? previous,bool overview=false)
    {
        IEnumerable<MonitorDisplayRow> Missing(List<MonitorDeviceChoice>? chosen,IEnumerable<MonitorDisplayRow>? rows)
        {
            if(chosen is {Count:0})return new MonitorDisplayRow[]{new("",Localization.Text("No devices selected"))};
            if(snapshot==null)return new MonitorDisplayRow[]{new("",Localization.Text("Sampling…"))};
            if(rows?.ToArray() is {Length:>0} readings)return readings;
            return new MonitorDisplayRow[]{new("",Localization.Text("None available"))};
        }
        var hardware=snapshot?.Hardware;
        bool autoGpu=overview && settings.Gpu==null &&
            hardware?.Gpus.Any(gpu=>SystemDisplayPreferences.Includes(settings.GpuDevices,gpu.DeviceId))==true;
        if(settings.Gpu==true || autoGpu)
            yield return (MonitorMetric.Gpu,Localization.Text("Graphics (GPU)"),Missing(settings.GpuDevices,Gpu(hardware,settings.GpuDevices)));
        if(settings.Storage==true)
            yield return (MonitorMetric.Storage,Localization.Text("Disk activity"),
                Missing(settings.StorageDevices,snapshot==null?null:StorageActivity(snapshot,previous,settings.StorageDevices)));
        if(settings.Network==true)
            yield return (MonitorMetric.Network,Localization.Text(overview?"Network · total traffic":"Network adapters"),
                Missing(settings.NetworkDevices,snapshot==null?null:Network(snapshot,previous,settings.NetworkDevices)));
    }
}
