using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SentoBar;

internal sealed class SystemDisplaySettingsWindow : Window
{
    internal const string Caption="System display settings";
    private readonly App app;
    private readonly Action changed;
    private readonly List<DeviceGroup> devices=new();
    private readonly List<Action<SystemDisplayPreferences>> refreshReadings=new();
    private bool refreshing;

    internal SystemDisplaySettingsWindow(App app,Action changed)
    {
        this.app=app;
        this.changed=changed;
        Title=Localization.Text(Caption);
        UI.LocalizedName(this,Caption);
        Width=430;Height=520;MinWidth=330;MinHeight=330;
        MaxHeight=SystemParameters.WorkArea.Height;
        ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty,"ControlFill");
        SetResourceReference(ForegroundProperty,"ControlText");
        FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI");
        GlassDockAppearance.ApplyControls(this);
        SourceInitialized+=(_,_)=>Native.DarkTitleBar(this);
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};

        var layout=new DockPanel{Margin=new(20,18,20,16)};
        var title=UI.Heading(Title,19);title.Margin=new(0,0,0,10);
        DockPanel.SetDock(title,Dock.Top);layout.Children.Add(title);
        var intro=UI.Caption("Choose readings for the expanded panel, mini panel and separate window. A dash means the default display.",12,UI.Muted);
        intro.Margin=new(0,0,0,12);
        DockPanel.SetDock(intro,Dock.Top);layout.Children.Add(intro);
        var footer=new DockPanel{Margin=new(0,14,0,0),LastChildFill=false};
        DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        var close=UI.CaptionButton("Done",Close,true);
        DockPanel.SetDock(close,Dock.Right);footer.Children.Add(close);
        var contents=new StackPanel();
        layout.Children.Add(UI.Scroll(contents));
        Content=layout;

        AddReading(contents,MonitorMetric.Cpu,"Processor (CPU)",p=>p.Cpu,(p,value)=>p.Cpu=value==true);
        AddReading(contents,MonitorMetric.Memory,"Memory (RAM)",p=>p.Memory,(p,value)=>p.Memory=value==true);
        AddDeviceGroup(contents,MonitorMetric.Gpu,"Graphics (GPU)",p=>p.Gpu,(p,value)=>p.Gpu=value,p=>p.GpuDevices,(p,value)=>p.GpuDevices=value,
            s=>s.Hardware is {Gpus.Count:>0} hardware?hardware.Gpus.Select(SystemDisplayRows.GpuChoice):null);
        AddDeviceGroup(contents,MonitorMetric.Storage,"Disks",p=>p.Storage,(p,value)=>p.Storage=value,p=>p.StorageDevices,(p,value)=>p.StorageDevices=value,
            s=>s.Drives.Select(drive=>new MonitorDeviceChoice(drive.DeviceId,drive.Name)));
        AddDeviceGroup(contents,MonitorMetric.Network,"Network adapters",p=>p.Network,(p,value)=>p.Network=value,p=>p.NetworkDevices,(p,value)=>p.NetworkDevices=value,
            s=>s.Networks.Select(network=>new MonitorDeviceChoice(network.Id,network.Name)));
    }

    internal static Button SettingsButton(Action open,bool icon=false)
    {
        var button=icon?Icons.Button("settings",Localization.Text(Caption),open,size:28):UI.CaptionButton(Caption,open);
        UI.LocalizedName(button,Caption);
        button.ToolTip=Localization.Text(Caption);
        return button;
    }

    private void AddReading(Panel contents,MonitorMetric metric,string caption,Func<SystemDisplayPreferences,bool?> read,Action<SystemDisplayPreferences,bool?> write,
        bool useDefault=false)
    {
        var check=CreateCheck(caption,read(app.Settings.SystemDisplay),isReading:useDefault);
        var format=CreateFormatSelector(metric,Localization.Text(caption));
        check.IsThreeState=useDefault;
        check.Checked+=ReadState;
        check.Unchecked+=ReadState;
        check.Indeterminate+=ReadState;
        refreshReadings.Add(settings=>
        {
            check.IsChecked=read(settings);
            format.SelectedIndex=FormatIndex(settings,metric);
        });
        void ReadState(object sender,RoutedEventArgs e)
        {
            if(refreshing)return;
            write(app.Settings.SystemDisplay,check.IsChecked);
            changed();
        }
        contents.Children.Add(FormatRow(check,format));
    }

    private void AddDeviceGroup(Panel contents,MonitorMetric metric,string caption,
        Func<SystemDisplayPreferences,bool?> enabled,Action<SystemDisplayPreferences,bool?> setEnabled,
        Func<SystemDisplayPreferences,List<MonitorDeviceChoice>?> read,Action<SystemDisplayPreferences,List<MonitorDeviceChoice>?> write,
        Func<MonitorSnapshot,IEnumerable<MonitorDeviceChoice>?> discover)
    {
        AddReading(contents,metric,caption,enabled,setEnabled,true);
        var list=new StackPanel{Margin=new(20,0,0,8),Visibility=Visibility.Collapsed};
        contents.Children.Add(list);
        devices.Add(new(metric,list,enabled,setEnabled,read,write,discover));
    }

    private static int FormatIndex(SystemDisplayPreferences settings,MonitorMetric metric,string? deviceId=null)=>
        deviceId==null?(settings.ShowChart(metric)?0:1):settings.Charts.DeviceOverride(metric,deviceId) switch
        {
            true=>1,
            false=>2,
            _=>0
        };

    private ComboBox CreateFormatSelector(MonitorMetric metric,string caption,string? deviceId=null)
    {
        string[] options=deviceId==null?["Graph","Numbers"]:["Use group setting","Graph","Numbers"];
        string name=Localization.Format("Display format for {0}",caption);
        var format=new ComboBox
        {
            ItemsSource=options.Select(Localization.Text).ToArray(),
            SelectedIndex=FormatIndex(app.Settings.SystemDisplay,metric,deviceId),
            MinWidth=45+85*Accessibility.TextScale,
            MinHeight=30,
            Margin=new(10,2,0,2),
            VerticalAlignment=VerticalAlignment.Center,
            ToolTip=name
        };
        UI.Named(format,name);
        AutomationProperties.SetAutomationId(format,deviceId==null
            ?$"System reading format {metric}":$"System reading format {metric} {deviceId}");
        format.SelectionChanged+=(_,_)=>
        {
            int selected=format.SelectedIndex;
            if(refreshing || selected<0)return;
            var charts=app.Settings.SystemDisplay.Charts;
            if(deviceId==null)charts.SetDefault(metric,selected==0);
            else charts.SetDeviceOverride(metric,deviceId,selected==0?null:selected==1);
            changed();
        };
        return format;
    }

    private static Grid FormatRow(CheckBox check,ComboBox format)
    {
        var row=new Grid();
        row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        Grid.SetColumn(format,1);
        row.Children.Add(check);
        row.Children.Add(format);
        return row;
    }

    private static CheckBox CreateCheck(string caption,bool? selected,bool localize=true,bool isReading=false)
    {
        CheckBox check=isReading?new ReadingCheckBox():new CheckBox();
        check.IsChecked=selected;
        check.Content=UI.Text(localize?Localization.Text(caption):caption,13);
        check.MinHeight=28;
        check.VerticalContentAlignment=VerticalAlignment.Center;
        check.Margin=new(0,2,0,2);
        check.Padding=new(5,0,0,0);
        if(isReading)check.SetResourceReference(StyleProperty,typeof(CheckBox));
        if(localize)UI.LocalizedName(check,caption);else UI.Named(check,caption);
        return check;
    }

    private sealed class ReadingCheckBox : CheckBox
    {
        // One activation from the standard display always turns the reading on in both windows.
        protected override void OnToggle()=>SetCurrentValue(IsCheckedProperty,IsChecked switch {null=>true,true=>false,false=>(bool?)null});
    }

    internal void UpdateDevices(MonitorSnapshot? snapshot)
    {
        var settings=app.Settings.SystemDisplay;
        refreshing=true;
        try
        {
            foreach(var refresh in refreshReadings)refresh(settings);
            foreach(var group in devices)UpdateDeviceGroup(group,snapshot,settings);
        }
        finally{refreshing=false;}
    }

    private void UpdateDeviceGroup(DeviceGroup group,MonitorSnapshot? snapshot,SystemDisplayPreferences settings)
    {
        if(snapshot!=null && group.Discover(snapshot) is {} discovered)
        {
            var choices=discovered.DistinctBy(device=>device.Id,StringComparer.OrdinalIgnoreCase)
                .OrderBy(device=>device.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
            if(choices.Length>0)group.Available=choices;
            group.LastSeen=choices.Select(device=>device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        group.Panel.IsEnabled=group.Enabled(settings)!=false;
        var saved=group.Read(settings);
        var available=group.LastSeen;
        var known=group.Available.Select(device=>device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choicesForEdit=group.Available.Concat(saved?.Where(device=>!known.Contains(device.Id))
            ??Enumerable.Empty<MonitorDeviceChoice>()).ToArray();
        if(choicesForEdit.Length==0 && saved==null)
        {
            group.Panel.Visibility=Visibility.Collapsed;
            return;
        }
        var allVisibility=choicesForEdit.Length>1 || saved!=null?Visibility.Visible:Visibility.Collapsed;
        group.Panel.Visibility=Visibility.Visible;
        if(group.All!=null && group.LastChoices.SequenceEqual(choicesForEdit) && group.LastAvailable.SetEquals(available))
        {
            group.All.IsChecked=saved==null;
            group.All.Visibility=allVisibility;
            foreach(var (device,check,format) in group.Checks)
            {
                check.IsChecked=SystemDisplayPreferences.Includes(saved,device.Id);
                format.SelectedIndex=FormatIndex(settings,group.Metric,device.Id);
            }
            return;
        }
        group.LastChoices=choicesForEdit;group.LastAvailable=available;
        group.Panel.Children.Clear();
        var all=CreateCheck("All devices",saved==null);
        all.Visibility=allVisibility;
        all.Click+=(_,_)=>ChangeSelection(group,all.IsChecked==true?null:choicesForEdit.ToList());
        group.All=all;
        group.Checks.Clear();
        group.Panel.Children.Add(all);
        foreach(var device in choicesForEdit)
        {
            string caption=available.Contains(device.Id)?device.Name:Localization.Format("{0} (unavailable)",device.Name);
            var check=CreateCheck(caption,SystemDisplayPreferences.Includes(saved,device.Id),false);
            var format=CreateFormatSelector(group.Metric,caption,device.Id);
            check.Margin=new(14,0,0,0);
            check.Click+=(_,_)=>
            {
                var next=(group.Read(app.Settings.SystemDisplay)??choicesForEdit.ToList()).ToList();
                next.RemoveAll(item=>StringComparer.OrdinalIgnoreCase.Equals(item.Id,device.Id));
                if(check.IsChecked==true)next.Add(device);
                ChangeSelection(group,next);
            };
            group.Checks.Add((device,check,format));
            group.Panel.Children.Add(FormatRow(check,format));
        }
    }

    private void ChangeSelection(DeviceGroup group,List<MonitorDeviceChoice>? selection)
    {
        var settings=app.Settings.SystemDisplay;
        group.SetEnabled(settings,true);
        group.Write(settings,selection);
        changed();
    }

    private sealed record DeviceGroup(MonitorMetric Metric,StackPanel Panel,Func<SystemDisplayPreferences,bool?> Enabled,
        Action<SystemDisplayPreferences,bool?> SetEnabled,Func<SystemDisplayPreferences,List<MonitorDeviceChoice>?> Read,
        Action<SystemDisplayPreferences,List<MonitorDeviceChoice>?> Write,Func<MonitorSnapshot,IEnumerable<MonitorDeviceChoice>?> Discover)
    {
        internal MonitorDeviceChoice[] Available=Array.Empty<MonitorDeviceChoice>();
        internal HashSet<string> LastSeen=new(StringComparer.OrdinalIgnoreCase);
        internal MonitorDeviceChoice[] LastChoices=Array.Empty<MonitorDeviceChoice>();
        internal HashSet<string> LastAvailable=new(StringComparer.OrdinalIgnoreCase);
        internal CheckBox? All;
        internal readonly List<(MonitorDeviceChoice Device,CheckBox Check,ComboBox Format)> Checks=new();
    }
}
