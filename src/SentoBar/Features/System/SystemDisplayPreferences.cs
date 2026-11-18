using System;
using System.Collections.Generic;
using System.Linq;

namespace SentoBar;

public sealed partial class Settings
{
    public SystemDisplayPreferences SystemDisplay {get;set;}=new();
}

public sealed record MonitorDeviceChoice(string Id,string Name);

public sealed class SystemDisplayPreferences
{
    public bool Cpu {get;set;}=true;
    public bool Memory {get;set;}=true;
    // Null preserves the presentation of profiles created before the display settings.
    public bool? Gpu {get;set;}
    public bool? Storage {get;set;}
    public bool? Network {get;set;}

    // Null follows the current inventory, while an empty list selects no devices.
    public List<MonitorDeviceChoice>? GpuDevices {get;set;}
    public List<MonitorDeviceChoice>? StorageDevices {get;set;}
    public List<MonitorDeviceChoice>? NetworkDevices {get;set;}
    public SystemChartPreferences Charts {get;set;}=new();

    internal void Normalize()
    {
        static List<MonitorDeviceChoice>? Clean(List<MonitorDeviceChoice>? devices)=>
            devices?.Where(device=>device!=null && !string.IsNullOrWhiteSpace(device.Id) && device.Id.Length<=512)
                .DistinctBy(device=>device.Id,StringComparer.OrdinalIgnoreCase).Take(256)
                .Select(device=>new MonitorDeviceChoice(device.Id,string.IsNullOrWhiteSpace(device.Name)?device.Id:device.Name[..Math.Min(device.Name.Length,256)]))
                .ToList();
        GpuDevices=Clean(GpuDevices);
        GpuDevices?.RemoveAll(device=>HardwareMonitor.IsSoftwareAdapter(device.Id));
        StorageDevices=Clean(StorageDevices);
        NetworkDevices=Clean(NetworkDevices);
        Charts??=new();
        Charts.Normalize();
    }

    internal bool ShowChart(MonitorMetric metric,string? deviceId=null)=>
        (deviceId==null?null:Charts.DeviceOverride(metric,deviceId))??Charts.DefaultFor(metric);

    internal static bool Includes(List<MonitorDeviceChoice>? devices,string id)=>
        devices==null || devices.Any(device=>StringComparer.OrdinalIgnoreCase.Equals(device.Id,id));
}

public sealed class SystemChartPreferences
{
    private const int MaxDeviceOverrides=256;

    public bool Cpu {get;set;}=true;
    public bool Memory {get;set;}=true;
    public bool Gpu {get;set;}=true;
    public bool Storage {get;set;}=true;
    public bool Network {get;set;}=true;

    // An absent entry inherits the format of its group.
    public Dictionary<string,bool> GpuDevices {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,bool> StorageDevices {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,bool> NetworkDevices {get;set;}=new(StringComparer.OrdinalIgnoreCase);

    internal bool DefaultFor(MonitorMetric metric)=>metric switch
    {
        MonitorMetric.Cpu=>Cpu,
        MonitorMetric.Memory=>Memory,
        MonitorMetric.Gpu=>Gpu,
        MonitorMetric.Storage=>Storage,
        MonitorMetric.Network=>Network,
        _=>true
    };

    internal void SetDefault(MonitorMetric metric,bool chart)
    {
        switch(metric)
        {
            case MonitorMetric.Cpu: Cpu=chart; break;
            case MonitorMetric.Memory: Memory=chart; break;
            case MonitorMetric.Gpu: Gpu=chart; break;
            case MonitorMetric.Storage: Storage=chart; break;
            case MonitorMetric.Network: Network=chart; break;
            default: throw new ArgumentOutOfRangeException(nameof(metric));
        }
    }

    internal bool? DeviceOverride(MonitorMetric metric,string deviceId)=>
        DevicesFor(metric)?.TryGetValue(deviceId,out bool chart)==true?chart:null;

    internal void SetDeviceOverride(MonitorMetric metric,string deviceId,bool? chart)
    {
        var devices=DevicesFor(metric);
        if(devices==null || !ValidDeviceId(deviceId))return;
        if(chart is not bool value)
        {
            devices.Remove(deviceId);
            return;
        }
        if(devices.Count>=MaxDeviceOverrides && !devices.ContainsKey(deviceId))
            devices.Remove(devices.First().Key);
        devices[deviceId]=value;
    }

    internal void Normalize()
    {
        GpuDevices=Clean(GpuDevices);
        foreach(string id in GpuDevices.Keys.Where(HardwareMonitor.IsSoftwareAdapter).ToArray())
            GpuDevices.Remove(id);
        StorageDevices=Clean(StorageDevices);
        NetworkDevices=Clean(NetworkDevices);
    }

    private Dictionary<string,bool>? DevicesFor(MonitorMetric metric)=>metric switch
    {
        MonitorMetric.Gpu=>GpuDevices,
        MonitorMetric.Storage=>StorageDevices,
        MonitorMetric.Network=>NetworkDevices,
        _=>null
    };

    private static bool ValidDeviceId(string id)=>!string.IsNullOrWhiteSpace(id) && id.Length<=512;

    private static Dictionary<string,bool> Clean(Dictionary<string,bool>? devices)
    {
        var clean=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        if(devices==null)return clean;
        foreach(var (id,chart) in devices)
        {
            if(ValidDeviceId(id))clean.TryAdd(id,chart);
            if(clean.Count==MaxDeviceOverrides)break;
        }
        return clean;
    }
}
