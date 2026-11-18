using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SentoBar;

internal static class SystemDisplayDiagnostics
{
    internal static async Task RunLive(App app,OverlayWindow island,Action<string,bool> check)
    {
        var settings=app.Settings;
        settings.CompactLayout=true;
        settings.ReduceMotion=true;
        settings.DetachablePreviews=true;
        settings.SystemDisplay.Gpu=settings.SystemDisplay.Storage=settings.SystemDisplay.Network=true;
        settings.Widgets.Single(widget=>widget.Id=="system").Enabled=true;
        app.ChangeSettings();
        island.OpenFeaturePage("system");
        var sw=Stopwatch.StartNew();
        while(sw.ElapsedMilliseconds<9500)
        {
            var samples=island.SystemDisplaySamples;
            if(samples.Current is {} value && SystemMonitor.Cpu(value,samples.Previous).HasValue
                && value.Hardware?.Gpus.Any(gpu=>gpu.Utilization.HasValue)==true
                && value.Drives.All(drive=>SystemMonitor.DiskActivity(drive,samples.Previous).HasValue))break;
            await Task.Delay(160);
        }
        var (current,previous)=island.SystemDisplaySamples;
        check("Windows supplies two CPU samples in the open monitor",current!=null && SystemMonitor.Cpu(current,previous).HasValue);
        check("Windows supplies RAM, fixed volumes and a connected interface",current is {Total:>0} &&
            current.Drives.Count>0 && current.Networks.Any(n=>n.Received.HasValue));
        var gpus=current?.Hardware?.Gpus??Array.Empty<HardwareGpu>();
        foreach(var gpu in gpus)
            check((gpu.Model??"Unidentified graphics adapter")+" measured "+HardwareMonitor.Percent(gpu.Utilization),gpu.Utilization.HasValue);
        check("Windows supplies live GPU load in the open monitor",gpus.Any(gpu=>gpu.Utilization.HasValue));
        check("Software graphics adapter is excluded from the live inventory",gpus.All(gpu=>!HardwareMonitor.IsSoftwareAdapter(gpu.AdapterPath)));
        foreach(var drive in current?.Drives??Array.Empty<MonitorDrive>())
        {
            check("Fixed volume "+drive.Name+" is shown in Overview",Diagnostics.Descendants(island).OfType<TextBlock>()
                .Any(text=>text.IsVisible && text.Text==drive.Name));
            var activity=SystemMonitor.DiskActivity(drive,previous);
            check("Windows reports live I/O activity for "+drive.Name+" ("+(activity?.ToString("0.#")??"unavailable")+"%)",
                activity is >=0 and <=100);
        }

        settings.SystemDisplay.Gpu=settings.SystemDisplay.Storage=settings.SystemDisplay.Network=false;
        app.ChangeSettings();
        app.Previews.Open("system");
        var detached=Application.Current.Windows.OfType<DetachedPreviewWindow>().Single(window=>window.IsVisible);
        island.Collapse();
        int before=island.MonitorResults;
        sw.Restart();
        bool HasBackgroundSample()
        {
            var (latest,earlier)=island.SystemDisplaySamples;
            return island.MonitorResults>before && latest is {Total:>0} && SystemMonitor.Cpu(latest,earlier).HasValue;
        }
        while(!HasBackgroundSample() && sw.ElapsedMilliseconds<6500)await Task.Delay(160);
        check("A CPU and RAM only window keeps graph sampling after the main panel closes",HasBackgroundSample() &&
            Diagnostics.Descendants(detached).OfType<MonitorHistoryChart>().Count(chart=>chart.IsVisible)==2);
        detached.Close();
    }

    internal static async Task Run(App app,OverlayWindow island,Action<string,bool> check,Action<string> capture)
    {
        var settings=app.Settings;
        settings.CompactLayout=true;settings.DetachablePreviews=true;settings.ReduceMotion=true;
        settings.PanelWidth=520;settings.PanelMaxHeight=570;
        settings.Widgets.Single(widget=>widget.Id=="system").Enabled=true;
        const long GiB=1073741824;
        var drives=new[]{new MonitorDrive(@"C:\",200*GiB,480*GiB,"volume-first",(172,200)),
            new MonitorDrive(@"D:\",800*GiB,1800*GiB,"volume-second",(195,200))};
        var network=new[]{new MonitorNetwork("ethernet","Ethernet test",9000,1800),new MonitorNetwork("wifi","Wi-Fi test",11000,7100)};
        var graphics=new MonitorHardware(Array.Empty<HardwareCpu>(),Array.Empty<string>(),new[]
        {
            new HardwareGpu("counter-first",21,0,0,"graphics-first","Graphics one"),
            new HardwareGpu("counter-second",37,0,0,"graphics-second","Graphics two")
        },Array.Empty<HardwareSensor>(),null);
        var current=new MonitorSnapshot(Stopwatch.Frequency*3,30,100,18*GiB,32*GiB,TimeSpan.FromHours(7),null,null,false,
            network,drives,Array.Empty<MonitorProcess>(),Array.Empty<string>(),graphics);
        var previous=current with{Timestamp=Stopwatch.Frequency,Idle=12,CpuTotal=40,
            Networks=network.Select(n=>n with{Received=0,Sent=0}).ToArray(),
            Drives=drives.Select(disk=>disk with{Counters=(100,100)}).ToArray()};
        island.SystemDisplayFixture=(current,previous);
        app.ChangeSettings();
        island.OpenFeaturePage("system");

        Button? OpenButton(Window owner)=>Diagnostics.Descendants(owner).OfType<Button>()
            .FirstOrDefault(button=>button.IsVisible && UI.Identity(button)==SystemDisplaySettingsWindow.Caption);
        static CheckBox? Box(Window owner,string caption)=>Diagnostics.Descendants(owner).OfType<CheckBox>()
            .FirstOrDefault(box=>box.IsVisible && (UI.Identity(box)==caption || UI.Identity(box)==Localization.Text(caption)));
        static bool HasText(Window owner,string label)=>Diagnostics.Descendants(owner).OfType<TextBlock>()
            .Any(text=>text.IsVisible && text.Text.StartsWith(label,StringComparison.Ordinal));
        static SystemDisplaySettingsWindow? DisplayWindow()=>Application.Current.Windows.OfType<SystemDisplaySettingsWindow>()
            .FirstOrDefault(window=>window.IsVisible);
        static async Task<bool> WaitFor(Func<bool> match)
        {
            var time=Stopwatch.StartNew();
            while(!match() && time.ElapsedMilliseconds<4500)await Task.Delay(50);
            return match();
        }

        check("Expanded system widget exposes its display settings",await WaitFor(()=>OpenButton(island)!=null));
        OpenButton(island)?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var panel=DisplayWindow();
        check("The expanded widget opens the settings window",panel?.Owner==island);
        if(panel==null)return;
        check("Settings discovers distinct disks, adapters and GPUs",await WaitFor(()=>Box(panel,@"D:\")!=null && Box(panel,"Wi-Fi test")!=null && Box(panel,"Graphics two")!=null));
        foreach(string name in new[]{"Graphics (GPU)","Disks","Network adapters"})
        {
            var box=Box(panel,name);
            check(name+" initially shows the default display state",box!=null && box.IsChecked==null);
            if(box!=null && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(box)?
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle) is System.Windows.Automation.Provider.IToggleProvider toggle)
                toggle.Toggle();
        }
        check("The first toggle enables the optional readings",settings.SystemDisplay is {Gpu:true,Storage:true,Network:true});
        app.Previews.Open("system");
        var detached=Application.Current.Windows.OfType<DetachedPreviewWindow>().Single(window=>window.Title.EndsWith(DetachedPreviewWindow.NameFor("system"),StringComparison.Ordinal));
        check("Both surfaces show the enabled GPU, disk and network readings",await WaitFor(()=>
            HasText(island,"Graphics two") && HasText(detached,"Graphics two") && HasText(island,@"D:\") && HasText(detached,@"D:\")
            && HasText(island,"Wi-Fi test") && HasText(detached,"Wi-Fi test")));
        MonitorHistoryChart[] Charts(DependencyObject host)=>Diagnostics.Descendants(host).OfType<MonitorHistoryChart>().Where(chart=>chart.IsVisible).ToArray();
        bool HasAxis(MonitorHistoryChart chart,string label)=>Diagnostics.Descendants(chart).OfType<TextBlock>().Any(text=>text.Text==label);
        var charts=Charts(island);
        var detachedCharts=Charts(detached);
        check("Overview and separate window have graphs for CPU, RAM, two GPUs, two disks, and two network adapters",
            charts.Length==8 && detachedCharts.Length==8);
        check("Percent graphs share the explicit 0 and 100 percent bounds",
            charts.Count(chart=>HasAxis(chart,"0%") && HasAxis(chart,"100%"))==6 &&
            detachedCharts.Count(chart=>HasAxis(chart,"0%") && HasAxis(chart,"100%"))==6);
        check("Network graphs have a throughput scale starting at zero",
            charts.Count(chart=>HasAxis(chart,SystemMonitor.RateText(0)))==2 &&
            charts.Any(chart=>HasAxis(chart,SystemMonitor.RateText(8192))) &&
            charts.Any(chart=>HasAxis(chart,SystemMonitor.RateText(16384))));
        check("Overview and separate window caption used and total RAM",
            HasText(island,SystemDisplayRows.UsedCapacity(14*GiB,32*GiB)) &&
            HasText(detached,SystemDisplayRows.UsedCapacity(14*GiB,32*GiB)));
        check("Disk charts show active I/O percentages from the two separate volumes",
            HasText(island,"28%") && HasText(detached,"28%") && HasText(island,"5%") && HasText(detached,"5%"));
        capture("system-charts");
        if(charts.LastOrDefault() is {} last)
        {
            last.BringIntoView();
            await Task.Delay(100);
            capture("system-charts-more");
        }

        ComboBox Format(string id)=>Diagnostics.Descendants(panel).OfType<ComboBox>()
            .Single(combo=>UI.Identity(combo)=="System reading format "+id);
        var cpuFormat=Format("Cpu");
        var disksFormat=Format("Storage");
        var secondDiskFormat=Format("Storage volume-second");
        cpuFormat.SelectedIndex=disksFormat.SelectedIndex=1;
        secondDiskFormat.SelectedIndex=1;
        check("Numbers mode leaves readings visible and the disk override retains its graph",
            Charts(island).Length==6 && Charts(detached).Length==6 && HasText(island,"CPU") &&
            HasText(detached,"28%") && HasText(detached,"5%"));
        var formats=app.Store.Load<Settings>("settings").SystemDisplay;
        check("Numbers and per-device graph overrides are retained after loading settings",
            !formats.ShowChart(MonitorMetric.Cpu) && !formats.ShowChart(MonitorMetric.Storage,"volume-first") &&
            formats.ShowChart(MonitorMetric.Storage,"volume-second"));
        cpuFormat.SelectedIndex=disksFormat.SelectedIndex=secondDiskFormat.SelectedIndex=0;

        foreach(string name in new[]{@"D:\","Wi-Fi test","Graphics two"})
        {
            var box=Box(panel,name);
            if(box==null)continue;
            box.IsChecked=false;
            box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        var cpu=Box(panel,"Processor (CPU)");
        if(cpu!=null)cpu.IsChecked=false;
        var memory=Box(panel,"Memory (RAM)");
        if(memory!=null)memory.IsChecked=false;
        check("Both surfaces hide deselected readings immediately",!HasText(island,"CPU") && !HasText(detached,"CPU")
            && !HasText(island,Localization.Text("Memory")) && !HasText(detached,Localization.Text("Memory"))
            && !HasText(island,@"D:\") && !HasText(detached,@"D:\")
            && !HasText(island,"Wi-Fi test") && !HasText(detached,"Wi-Fi test")
            && !HasText(island,"Graphics two") && !HasText(detached,"Graphics two"));
        check("Deselecting a metric or a device removes its graph from both views",Charts(island).Length==3 && Charts(detached).Length==3);
        var saved=app.Store.Load<Settings>("settings").SystemDisplay;
        check("Actual control changes persist stable selections",!saved.Cpu && !saved.Memory &&
            saved.StorageDevices?.SingleOrDefault()?.Id=="volume-first" &&
            saved.NetworkDevices?.SingleOrDefault()?.Id=="ethernet" &&
            saved.GpuDevices?.SingleOrDefault()?.Id=="graphics-first");

        panel.Close();
        OpenButton(detached)?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check("The separate system window opens the same display settings",DisplayWindow()?.Owner==detached);
        DisplayWindow()?.Close();
        island.Collapse();
        check("The separate window keeps its samples and graphs when the island closes",island.MonitorReady && Charts(detached).Length==3 &&
            HasText(detached,"Graphics one") && HasText(detached,"Ethernet test") && HasText(detached,@"C:\"));
        int before=island.MonitorResults;
        check("Background sampling continues for the separate system window",await WaitFor(()=>island.MonitorResults>before));
        detached.Close();
        settings.HoverFollowsWidgets=true;
        app.ChangeSettings();
        island.PreviewActivity();
        island.SelectPeek("system");
        var mini=Diagnostics.Descendants(island).OfType<SystemPeekView>().FirstOrDefault(view=>view.IsVisible);
        check("Mini System shows the same enabled metrics and device selection",mini!=null &&
            await WaitFor(()=>HasText(island,"Graphics one") && HasText(island,"Ethernet test") && HasText(island,@"C:\")) &&
            !HasText(island,"Graphics two") && !HasText(island,"Wi-Fi test") && !HasText(island,@"D:\"));
        check("Detailed Mini System shows graphs and bounded percentage scales for its selected devices",
            mini!=null && Charts(mini).Length==3 && Charts(mini).Count(chart=>HasAxis(chart,"0%") && HasAxis(chart,"100%"))==2);
        check("Mini System detail fits its viewport and scrolls when metrics overflow",
            mini!=null && Diagnostics.Descendants(mini).OfType<ScrollViewer>().Any(view=>view.IsVisible && view.ViewportHeight>0 && view.ScrollableHeight>0));

        settings.SystemDisplay.Cpu=settings.SystemDisplay.Memory=true;
        app.ChangeSettings();
        if(mini==null)return;
        check("Detailed Mini System includes CPU and RAM graphs",Charts(mini).Length==5);
        capture("system-mini-charts");
        settings.SystemDisplay.Charts.SetDefault(MonitorMetric.Memory,false);
        app.ChangeSettings();
        check("Mini System immediately applies numbers-only display while retaining RAM usage",
            Charts(mini).Length==4 && HasText(island,SystemDisplayRows.UsedCapacity(14*GiB,32*GiB)));
        mini.SetCompactLayout(true);
        mini.UpdateLayout();
        check("Brief System preview shows CPU and RAM on one line",
            Diagnostics.Descendants(mini).OfType<TextBlock>().Any(text=>text.IsVisible && text.Text.StartsWith("CPU",StringComparison.Ordinal) &&
            text.Text.Contains(Localization.Text("RAM  {0:0.0} / {1:0.0} GB").Split('{')[0].Trim(),StringComparison.Ordinal) &&
            !text.Text.Contains('\n')));
    }
}
