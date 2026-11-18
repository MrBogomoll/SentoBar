using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SentoBar;

public sealed partial class OverlayWindow
{
    private readonly StackPanel monitorRows=new();
    private string monitorTab="overview";
    private ScrollViewer? monitorDetailScroll;
    private Action? sizeMonitorViewport;
    private readonly Dictionary<string,Button> monitorTabs=new();
    private SystemDisplaySettingsWindow? monitorDisplayWindow;
    private DispatcherTimer? monitorBackgroundTick;
    private bool detachedMonitorVisible;
    internal event Action? DetachedMonitorUpdated;
    internal (MonitorSnapshot? Current,MonitorSnapshot? Previous) SystemDisplaySamples=>(monitorLatest,monitorPrevious);
    internal (MonitorSnapshot Current,MonitorSnapshot? Previous)? SystemDisplayFixture {get;set;}
    internal string MonitorTab=>monitorTab;
    internal void SelectMonitorTab(string id)
    {
        if(!monitorTabs.ContainsKey(id))return;
        if(monitorTab!=id)
        {
            fittedExtent=null;
            monitorGeneration++;
            monitorReadAt=0;
            if(!detachedMonitorVisible)
            {
                monitorPrevious=monitorLatest=null;
                if(id=="hardware")HardwareMonitor.ResetSampling();
            }
        }
        monitorTab=id;
        RenderSystemMonitor();
        monitorDetailScroll?.ScrollToTop();
        foreach(var pair in monitorTabs)
        {
            var button=pair.Value;
            bool selected=pair.Key==id;
            if(selected)
            {
                button.SetResourceReference(Control.BackgroundProperty,Accessibility.HighContrast?"ControlFill":"GlassControlFill");
                button.ClearValue(Control.ForegroundProperty);
            }
            else
            {
                button.Background=Brushes.Transparent;
                button.SetResourceReference(Control.ForegroundProperty,"MutedText");
            }
            System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?Localization.Text("Selected"):"");
        }
        sizeMonitorViewport?.Invoke();
        if(SystemMonitorVisible)ResizeToolPanel();
        UpdateSystemMonitor();
    }
    private readonly Queue<MonitorHistorySample> monitorHistory=new();
    private MonitorSnapshot? monitorPrevious,monitorLatest;
    internal bool MonitorPolling=>monitorActive;
    internal bool MonitorReady=>monitorLatest!=null && monitorPrevious!=null;
    internal bool MonitorBusy=>monitorBusy;
    internal int MonitorReads {get;private set;}
    internal int MonitorResults {get;private set;}
    internal int MonitorRejected {get;private set;}
    internal Task? MonitorDeliveryGate {get;set;}
    private bool monitorBusy,monitorActive,monitorClosed;
    private int monitorGeneration;
    private long monitorReadAt;

    internal static void DetachMonitorRows(StackPanel rows)
    {
        if(rows.Parent is ScrollViewer owner)owner.Content=null;
        else if(rows.Parent is ContentControl content)content.Content=null;
        else if(rows.Parent is Panel parent)parent.Children.Remove(rows);
    }

    private FrameworkElement BuildSystemMonitor()
    {
        DetachMonitorRows(monitorRows);
        var panel=new StackPanel();
        var nav=new DockPanel{Margin=new(0,0,0,8)};
        var settings=SystemDisplaySettingsWindow.SettingsButton(()=>ShowSystemDisplaySettings(this),true);
        settings.Margin=new(4,0,0,0);settings.VerticalAlignment=VerticalAlignment.Top;
        DockPanel.SetDock(settings,Dock.Right);nav.Children.Add(settings);
        var tabs=new WrapPanel();monitorTabs.Clear();
        var display=app.Settings.SystemDisplay;
        foreach(var (id,label) in new[]{("overview",Localization.Text("Overview")),("hardware",Localization.Text("Hardware")),("network",Localization.Text("Network")),("storage",Localization.Text("Storage")),("processes",Localization.Text("Processes"))})
        {
            if(id=="network" && !(display.Network??app.Settings.MonitorNetwork) ||
                id=="storage" && !(display.Storage??app.Settings.MonitorStorage) ||
                id=="processes" && !app.Settings.MonitorProcesses)continue;
            var button=UI.Button(label,()=>SelectMonitorTab(id));button.FontSize=10;button.Padding=new(7,5,7,5);button.MinHeight=28;button.Margin=new(0,0,GlassDockAppearance.ActionGap,3);UI.Named(button,Localization.Text("Monitor ")+label);monitorTabs[id]=button;tabs.Children.Add(button);
        }
        nav.Children.Add(tabs);panel.Children.Add(nav);
        monitorDetailScroll=UI.Scroll(monitorRows);
        monitorDetailScroll.Padding=new(0,0,2,0);
        UI.Named(monitorDetailScroll,Localization.Text("System details · scroll or use Page Down"));
        var viewport=monitorDetailScroll;
        panel.Children.Add(viewport);
        sizeMonitorViewport=()=>
        {
            nav.Measure(new Size(Math.Max(1,bodyWidth),double.PositiveInfinity));
            double reserved=49+nav.DesiredSize.Height;
            bool overview=monitorTab=="overview";
            double height=Math.Clamp((app.Settings.PanelMaxHeight-app.Settings.Height-32)/app.Settings.PanelScale-reserved,92,overview?530:240);
            viewport.Height=overview?double.NaN:height;
            viewport.MaxHeight=overview?height:double.PositiveInfinity;
        };
        bodyReflow.Add(sizeMonitorViewport);
        SelectMonitorTab(monitorTabs.ContainsKey(monitorTab)?monitorTab:"overview");
        return panel;
    }

    private bool ExtraOverviewReadings=>SystemDisplayRows.HasExtraReadings(app.Settings.SystemDisplay);
    private bool SystemMonitorVisible=>!monitorClosed && expanded && (app.Settings.CompactLayout || hoverPanelWidget=="system") && !commandOpen && activePage=="status" && WidgetOn("system") && IsVisible && monitorRows.IsVisible && monitorRows.IsDescendantOf(body);
    private bool SystemPeekVisible=>IsVisible && peeking && !assistantPeeking && !noticeShowing && peekId=="system";
    private bool SystemChartsVisible=>detachedMonitorVisible || (SystemPeekVisible && peekDetailed) ||
        (SystemMonitorVisible && monitorTab=="overview");
    private bool NeedsMonitorSample=>!monitorClosed &&
        (SystemMonitorVisible || SystemChartsVisible || monitorDisplayWindow!=null ||
            ExtraOverviewReadings && (ClassicSystemCardVisible || SystemPeekVisible));

    private void RefreshPeekSystem()
    {
        if(!SystemPeekVisible)return;
        ExpireMonitorHistory(Stopwatch.GetTimestamp());
        peekSystem?.Refresh(app.Settings,SystemSummaryUsage(),monitorLatest,monitorPrevious,monitorHistory);
    }

    internal void RefreshSystemCharts(SystemMonitorDashboard dashboard)
    {
        ExpireMonitorHistory(Stopwatch.GetTimestamp());
        dashboard.Refresh(app.Settings,monitorLatest,monitorPrevious,monitorHistory);
    }

    internal void SetDetachedMonitorVisible(bool visible)
    {
        detachedMonitorVisible=visible;
        ReconcileBackgroundMonitor();
    }

    internal void ShowSystemDisplaySettings(Window? owner)
    {
        if(monitorDisplayWindow!=null){monitorDisplayWindow.Activate();return;}
        var window=new SystemDisplaySettingsWindow(app,app.ChangeSettings);
        if(owner?.IsVisible==true)window.Owner=owner;
        monitorDisplayWindow=window;
        window.Closed+=(_,_)=>{monitorDisplayWindow=null;ReconcileBackgroundMonitor();};
        window.UpdateDevices(null);
        window.Show();
        window.Activate();
        ReconcileBackgroundMonitor();
    }

    private void ReconcileBackgroundMonitor()
    {
        if(!monitorClosed && (monitorDisplayWindow!=null || detachedMonitorVisible))
        {
            if(monitorBackgroundTick==null)
            {
                monitorBackgroundTick=new(){Interval=TimeSpan.FromSeconds(2)};
                monitorBackgroundTick.Tick+=(_,_)=>UpdateSystemMonitor();
            }
            monitorBackgroundTick.Start();
        }
        else monitorBackgroundTick?.Stop();
        UpdateSystemMonitor();
    }

    private void RefreshSystemDisplay()
    {
        monitorDisplayWindow?.UpdateDevices(null);
        DetachedMonitorUpdated?.Invoke();
        RefreshClassicSystemCard();
        RefreshPeekSystem();
        if(!SystemMonitorVisible)return;
        sizeMonitorViewport?.Invoke();
        RenderSystemMonitor();
        ResizeToolPanel();
    }

    private void ApplySystemDisplaySettings()
    {
        RefreshSystemDisplay();
        ReconcileBackgroundMonitor();
    }

    internal (double cpu,double used,double total) SystemSummaryUsage()
    {
        if(!DiagnosticRun || SystemDisplayFixture is not { } fixture)return app.Usage;
        var sample=fixture.Current;
        return (SystemMonitor.Cpu(sample,fixture.Previous)??double.NaN,
            sample.Total.HasValue && sample.Available.HasValue?(sample.Total.Value-sample.Available.Value)/1073741824d:0,
            sample.Total.GetValueOrDefault()/1073741824d);
    }

    private void InvalidateSystemMonitor()
    {
        if(detachedMonitorVisible && !monitorClosed)return;
        monitorActive=false;
        monitorGeneration++;
        monitorPrevious=monitorLatest=null;
        monitorReadAt=0;
        if(monitorClosed)monitorHistory.Clear();
        HardwareMonitor.ResetSampling();
    }

    private void ExpireMonitorHistory(long now)
    {
        while(monitorHistory.TryPeek(out var oldest) &&
            Stopwatch.GetElapsedTime(oldest.Timestamp,now).TotalSeconds>MonitorHistorySample.DurationSeconds)
            monitorHistory.Dequeue();
    }

    private async void UpdateSystemMonitor()
    {
        if(!NeedsMonitorSample)
        {
            if(monitorActive)InvalidateSystemMonitor();
            return;
        }
        if(!monitorActive)HardwareMonitor.ResetSampling();
        monitorActive=true;
        if(monitorBusy || (monitorReadAt!=0 && Stopwatch.GetElapsedTime(monitorReadAt).TotalSeconds<2))return;
        monitorBusy=true;MonitorReads++;monitorReadAt=Stopwatch.GetTimestamp();int generation=monitorGeneration;
        var display=app.Settings.SystemDisplay;
        string? tab=SystemMonitorVisible?monitorTab:null;
        bool discover=monitorDisplayWindow!=null;
        bool charts=SystemChartsVisible;
        bool summary=charts || ClassicSystemCardVisible || SystemPeekVisible;
        bool network=discover || tab=="network" || summary && display.Network==true;
        bool diskActivity=tab=="storage" || summary && display.Storage==true;
        bool storage=discover || diskActivity;
        bool processes=tab=="processes" && app.Settings.MonitorProcesses;
        bool hardware=tab=="hardware" || discover || charts && display.Gpu!=false || summary && display.Gpu==true;
        var fixture=DiagnosticRun?SystemDisplayFixture:null;
        bool sampleCpu=charts && display.Cpu,sampleMemory=charts && display.Memory;
        try
        {
            var snapshot=fixture?.Current??await Task.Run(()=>SystemMonitor.Capture(network,storage,processes,processes,
                hardware,tab!="hardware",includeOffline:discover,sampleCpu:sampleCpu,sampleMemory:sampleMemory,sampleDiskActivity:diskActivity));
            // Diagnostics can hold delivery to exercise navigation during an in-flight read.
            if(DiagnosticRun && MonitorDeliveryGate is Task gate)await Task.WhenAny(gate,Task.Delay(3000));
            // A quick return to Status must not revive a read from the detached body.
            if(generation!=monitorGeneration || !NeedsMonitorSample){MonitorRejected++;return;}
            MonitorResults++;
            monitorPrevious=fixture?.Previous??monitorLatest;monitorLatest=snapshot;
            if(charts && SystemChartsVisible)
            {
                monitorHistory.Enqueue(new(monitorReadAt,snapshot,monitorPrevious));
                ExpireMonitorHistory(monitorReadAt);
            }
            if(discover)monitorDisplayWindow?.UpdateDevices(snapshot);
            DetachedMonitorUpdated?.Invoke();
            RefreshClassicSystemCard();
            RefreshPeekSystem();
            if(SystemMonitorVisible)
            {
                RenderSystemMonitor();Dimensions(true);
                if(Math.Abs(PaintedHeight-expandedHeight)>.5)motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));
            }
        }
        catch
        {
            if(generation==monitorGeneration && SystemMonitorVisible){monitorRows.Children.Clear();monitorRows.Children.Add(UI.Text(Localization.Text("System information unavailable. Retrying…"),11,UI.Muted));}
        }
        finally{monitorBusy=false;if(generation!=monitorGeneration && NeedsMonitorSample)UpdateSystemMonitor();}
    }

    private void StopSystemMonitor()
    {
        monitorClosed=true;
        app.SettingsChanged-=ApplySystemDisplaySettings;
        monitorBackgroundTick?.Stop();
        monitorDisplayWindow?.Close();
        InvalidateSystemMonitor();
    }

    private void RenderSystemMonitor()
    {
        BeginMonitorRender();var s=monitorLatest;
        if(monitorTab=="overview")
        {
            RefreshSystemCharts(monitorDashboard);
            AddMonitorElement(monitorDashboard);
            if(s!=null)MonitorRow(Localization.Text("System uptime"),SystemMonitor.Uptime(s.Uptime));
            Finish();
            return;
        }
        if(s==null){AddMonitorElement(UI.Text(Localization.Text("Sampling system information…"),11,UI.Muted));Finish();return;}
        var display=app.Settings.SystemDisplay;
        if(monitorTab=="network" && (display.Network??app.Settings.MonitorNetwork))
        {
            MonitorSection(Localization.Text("NETWORK · RECEIVE / SEND"));
            RenderDeviceRows(SystemDisplayRows.Network(s,monitorPrevious,display.NetworkDevices),display.NetworkDevices,Localization.Text("Active interfaces"));
        }
        if(monitorTab=="storage" && (display.Storage??app.Settings.MonitorStorage))
        {
            MonitorSection(Localization.Text("Disk activity"));
            RenderDeviceRows(SystemDisplayRows.StorageActivity(s,monitorPrevious,display.StorageDevices),display.StorageDevices,Localization.Text("Volumes"));
            MonitorSection(Localization.Text("FIXED VOLUMES · FREE / TOTAL"));
            RenderDeviceRows(SystemDisplayRows.Storage(s,display.StorageDevices),display.StorageDevices,Localization.Text("Volumes"));
        }
        if(monitorTab=="processes" && app.Settings.MonitorProcesses)
        {
            MonitorRow(Localization.Text("Processes / threads"),SystemMonitor.ProcessCounts(s.ProcessCount,s.ThreadCount,s.PartialThreads));
            MonitorSection(Localization.Text("TOP PROCESSES · WORKING SET"));
            foreach(var process in s.Processes)MonitorRow(process.Name+" · "+process.Id,SystemMonitor.Bytes(process.WorkingSet));
            if(s.Processes.Count==0)MonitorRow(Localization.Text("Processes"),Localization.Text("Unavailable"));
            var note=UI.Text(Localization.Text("Working sets can share memory; they do not sum to total RAM usage."),10,UI.Muted);note.Margin=new(0,6,0,0);AddMonitorElement(note);
        }
        if(monitorTab=="hardware")RenderHardware(s.Hardware);
        Finish();

        void Finish()
        {
            if(s is {Errors.Length:>0})
            {
                var errors=UI.Text(string.Join(" · ",s.Errors),10,UI.Muted);
                errors.Margin=new(0,8,0,0);
                AddMonitorElement(errors);
            }
            CommitMonitorRender();
            ApplyHeadingTypography(monitorRows);
        }
    }

    private void RenderDeviceRows(IEnumerable<MonitorDisplayRow> readings,List<MonitorDeviceChoice>? selection,string fallback)
    {
        var rows=readings.ToArray();
        foreach(var row in rows)MonitorRow(row.Name,row.Value);
        if(rows.Length==0)MonitorRow(fallback,Localization.Text(selection is {Count:0}?"No devices selected":"None available"));
    }

    private void RenderHardware(MonitorHardware? hardware)
    {
        if(hardware==null){MonitorRow(Localization.Text("Hardware"),Localization.Text("Unavailable"));return;}
        var display=app.Settings.SystemDisplay;
        if(display.Cpu)
        {
            MonitorSection(Localization.Text("PROCESSOR"));
            foreach(var cpu in hardware.Cpus)
            {
                var model=UI.Heading(cpu.Name.Length>0?cpu.Name:Localization.Text("Processor unavailable"),12);model.TextWrapping=TextWrapping.NoWrap;model.TextTrimming=TextTrimming.CharacterEllipsis;model.ToolTip=cpu.Name;AddMonitorElement(model);
                MonitorRow(Localization.Text("Cores / threads"),(cpu.Cores?.ToString()??"—")+" / "+(cpu.Threads?.ToString()??"—")+(cpu.MaxClock is double mhz && mhz>0?$" · {mhz:0} MHz":""));
            }
            if(hardware.Cpus.Count==0)MonitorRow(Localization.Text("CPU details"),Localization.Text("Unavailable"));
        }
        var shownGpus=display.Gpu==false?Array.Empty<HardwareGpu>():hardware.Gpus
            .Where(gpu=>SystemDisplayPreferences.Includes(display.GpuDevices,gpu.DeviceId)).ToArray();
        if(display.Gpu!=false)
        {
            MonitorSection(Localization.Text("GRAPHICS"));
            if(display.GpuDevices==null)
            {
                foreach(string model in hardware.GpuModels){var text=UI.Heading(model,12);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.ToolTip=model;text.Margin=new(0,0,0,4);AddMonitorElement(text);}
                if(hardware.GpuModels.Count==0)MonitorRow(Localization.Text("GPU model"),Localization.Text("Unavailable"));
            }
            RenderDeviceRows(SystemDisplayRows.Gpu(hardware,display.GpuDevices),display.GpuDevices,Localization.Text("GPU utilization"));
            foreach(var gpu in shownGpus)
            {
                var name=gpu.Model??gpu.Id;
                MonitorRow(name+" · "+Localization.Text("Dedicated memory"),gpu.Dedicated is ulong dedicated?SystemMonitor.Bytes(dedicated):Localization.Text("Unavailable"));
            }
            if(shownGpus.Length==0 && display.GpuDevices==null)MonitorRow(Localization.Text("Dedicated / shared memory"),Localization.Text("Unavailable"));
        }
        if(hardware.Sensors.Count>0){MonitorSection(Localization.Text("TEMPERATURES & FANS"));foreach(var sensor in hardware.Sensors)MonitorRow(sensor.Name,HardwareMonitor.SensorValue(sensor));}
        var technical=new StackPanel();
        foreach(var gpu in shownGpus){technical.Children.Add(UI.Text(Localization.Text("Counter: ")+gpu.Id,10,UI.Muted));technical.Children.Add(UI.Text(Localization.Text("Shared memory: ")+(gpu.Shared is ulong shared?SystemMonitor.Bytes(shared):Localization.Text("Unavailable")),11,UI.Muted));}
        if(shownGpus.Length>0)technical.Children.Add(UI.Text(Localization.Text("Utilization uses the busiest GPU engine."),11,UI.Muted));
        technical.Children.Add(UI.Text(hardware.Sensors.Count>0?Localization.Text("Sensors: ")+string.Join(" / ",hardware.Sensors.Select(s=>s.Provider).Distinct()):Localization.Text("Temperature and fan sensors need an already running LibreHardwareMonitor or OpenHardwareMonitor WMI provider."),11,UI.Muted));
        if(display.Gpu!=false && hardware.Status!=null)technical.Children.Add(UI.Text(hardware.Status,10,UI.Muted));
        if(monitorTechnical==null){monitorTechnical=new Expander{Header=Localization.Text("Sensor availability & technical details"),Foreground=UI.Muted,FontSize=11,Margin=new(0,14,0,0)};monitorTechnical.Expanded+=(_,_)=>hardwareDetailsOpen=true;monitorTechnical.Collapsed+=(_,_)=>hardwareDetailsOpen=false;}
        monitorTechnical.Content=technical;monitorTechnical.IsExpanded=hardwareDetailsOpen;AddMonitorElement(monitorTechnical);
    }
    private bool hardwareDetailsOpen;
    private Expander? monitorTechnical;

    private void MonitorSection(string title){var label=UI.Label(title);label.Margin=new(0,15,0,7);AddMonitorElement(label);}
    private void MonitorRow(string label,string value,bool prominent=false)
    {
        var row=GetMonitorRow();var name=(TextBlock)row.Children[0];var text=(TextBlock)row.Children[1];
        Motion.Text(name,label);name.ToolTip=label;Motion.Text(text,value);text.FontSize=prominent?15:11;text.MaxWidth=Math.Max(90,bodyWidth*.61);text.ToolTip=value;AddMonitorElement(row);
    }
}
