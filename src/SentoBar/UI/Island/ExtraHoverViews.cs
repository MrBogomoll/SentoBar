using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Forms=System.Windows.Forms;
using System.Windows.Media;
namespace SentoBar;
public sealed partial class OverlayWindow
{
    private readonly Dictionary<string,TextBlock> extraPeekLabels=new();
    private readonly TextBlock peekBatteryValue=UI.Text("",24);
    private readonly TextBlock peekBatteryStatus=UI.Text("",11,UI.Muted);
    private readonly ProgressBar peekBatteryLevel=new(){Minimum=0,Maximum=100,Height=5,BorderThickness=new(0),Margin=new(0,6,0,3)};
    private readonly Grid peekShortcutTiles=new();
    private Shortcut[] shownPeekShortcuts=Array.Empty<Shortcut>();
    private Action? refreshPeekAwake;
    private TextBlock? peekDate;

    private FrameworkElement BuildExtraPeek(string id)
    {
        if(id=="weather")return new WeatherGlance(app);
        if(id=="awake")return BuildPeekAwake();
        if(id=="battery")return BuildPeekBattery();
        if(id=="shortcuts")return BuildPeekShortcuts();
        var page=PeekSummary(id);
        var detail=UI.Text("",id=="clock"?30:12,UI.Muted);
        detail.Margin=new(0,6,0,id=="clock"?3:8);
        page.Children.Add(detail);extraPeekLabels[id]=detail;
        if(id=="volume")
        {
            var slider=new Slider{Minimum=0,Maximum=100,Height=26,Margin=new(0,4,0,4)};UI.Named(slider,Localization.Text("Hover output volume"));bool sync=false;slider.ValueChanged+=(_,_)=>{if(!sync)app.Audio.SetVolume(slider.Value/100);};
            page.IsVisibleChanged+=(_,_)=>{if(page.IsVisible){sync=true;slider.Value=app.Audio.Volume*100;sync=false;}};page.Children.Add(slider);
        }
        else if(id=="clock")
        {
            detail.FontWeight=FontWeights.SemiBold;
            peekDate=UI.Text("",11,UI.Muted);
            peekDate.Margin=new(0,0,0,8);
            page.Children.Add(peekDate);
        }
        string caption=id=="power"?"Choose a plan":Localization.Text("Open ")+PeekName(id);
        var open=GlassPeekButton(caption,caption,OpenPeekPage);
        open.HorizontalAlignment=HorizontalAlignment.Left;
        if(id=="clock")open.Margin=new(0,3,0,0);
        page.Children.Add(open);
        return page;
    }
    private StackPanel PeekSummary(string id)
    {
        var page=new StackPanel();
        var heading=UI.Heading(PeekName(id),13);
        page.Children.Add(heading);
        return page;
    }
    private void UpdateExtraPeek()
    {
        // Keep hidden previews dormant; values update when their page is selected.
        if(peekId=="battery"){UpdatePeekBattery();return;}
        if(peekId=="shortcuts"){UpdatePeekShortcuts();return;}
        if(peekId=="awake"){refreshPeekAwake?.Invoke();return;}
        if(!extraPeekLabels.TryGetValue(peekId,out var label))return;
        var now=DateTime.Now;
        if(peekId=="clock" && peekDate!=null)
        {
            peekDate.Visibility=app.Settings.ShowDate?Visibility.Visible:Visibility.Collapsed;
            Motion.Text(peekDate,DateLabel(now));
        }
        string text=peekId switch
        {
            "clock"=>ExpandedClockText(now),
            "volume"=>app.Audio.Available?app.Audio.Volume.ToString("P0")+" · "+Localization.Text(app.Audio.Muted?"Muted":"Output volume"):Localization.Text("No output device"),
            "power"=>Localization.Text("Open to choose a Windows power plan"),
            "awake"=>Localization.Text("Keep your computer awake while you work"),
            "voice"=>app.Assistant.VoiceConnected?Localization.Text("Voice session active"):Localization.Text("A dedicated space to talk with Sento"),
            _=>""
        };
        Motion.Text(label,text);
    }

    private string ExpandedClockText(DateTime now)
    {
        string format=app.Settings.Clock24Hour
            ? (app.Settings.ClockSeconds?"HH:mm:ss":"HH:mm")
            : (app.Settings.ClockSeconds?"h:mm:ss tt":"h:mm tt");
        return now.ToString(format,Localization.Culture);
    }

    private static string DateLabel(DateTime now)=>now.ToString("ddd, dd MMM",Localization.Culture);

    private FrameworkElement BuildPeekBattery()
    {
        var page=PeekSummary("battery");
        var summary=new StackPanel{Margin=new(0,6,0,0)};
        peekBatteryStatus.Margin=new(0,3,0,0);
        summary.Children.Add(peekBatteryValue);
        summary.Children.Add(peekBatteryStatus);
        page.Children.Add(summary);
        page.Children.Add(peekBatteryLevel);
        var settings=GlassPeekButton("Power settings","Power settings",()=>OpenPowerSettings());
        settings.HorizontalAlignment=HorizontalAlignment.Left;
        settings.Margin=new(0,8,0,0);
        page.Children.Add(settings);
        return page;
    }

    private void OpenPowerSettings()
    {
        try{ShortcutService.Open(new(){Target="ms-settings:powersleep"});}
        catch(Exception ex){app.Notify(Localization.Text("Could not open shortcut"),ex.Message);}
    }

    private readonly TextBlock batteryOverviewValue=UI.Text("",28),batteryOverviewStatus=UI.Text("",12,UI.Muted);
    private readonly ProgressBar batteryOverviewLevel=new(){Minimum=0,Maximum=100,Height=5,Margin=new(0,8,0,10),BorderThickness=new(0)};

    private FrameworkElement BuildBatteryOverview()
    {
        var page=new StackPanel();
        DetachMediaControl(batteryOverviewValue);
        DetachMediaControl(batteryOverviewStatus);
        DetachMediaControl(batteryOverviewLevel);
        page.Children.Add(UI.Caption("Battery",17));
        batteryOverviewValue.Margin=new(0,8,0,3);
        page.Children.Add(batteryOverviewValue);
        page.Children.Add(batteryOverviewStatus);
        page.Children.Add(batteryOverviewLevel);
        var settings=UI.CaptionButton("Power settings",OpenPowerSettings);
        settings.HorizontalAlignment=HorizontalAlignment.Left;
        settings.Margin=new(0,10,0,0);
        page.Children.Add(settings);
        UpdateBatteryOverview();
        return page;
    }

    private void UpdatePeekBattery()=>UpdateBatteryLabel(peekBatteryValue,peekBatteryStatus,peekBatteryLevel,24);
    private void UpdateBatteryOverview()=>UpdateBatteryLabel(batteryOverviewValue,batteryOverviewStatus,batteryOverviewLevel,28);

    private static void UpdateBatteryLabel(TextBlock value,TextBlock status,ProgressBar level,double size)
    {
        var power=Forms.SystemInformation.PowerStatus;
        var charge=power.BatteryChargeStatus;
        bool known=charge!=Forms.BatteryChargeStatus.Unknown;
        bool present=known && !charge.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery);
        double percent=power.BatteryLifePercent*100;
        bool showPercent=present && percent>=0 && percent<=100;
        bool charging=present && charge.HasFlag(Forms.BatteryChargeStatus.Charging);
        string source=charging?"Charging":power.PowerLineStatus switch
        {
            Forms.PowerLineStatus.Online=>"Connected to power",
            Forms.PowerLineStatus.Offline=>"On battery",
            _=>"Power source unavailable"
        };
        value.FontSize=showPercent?size:15;
        Motion.Text(value,showPercent?Localization.Format("{0:0}%",percent):Localization.Text(present || !known?"Charge level unavailable":"No battery detected"));
        var remaining=TimeSpan.FromSeconds(Math.Max(0,power.BatteryLifeRemaining));
        string detail=Localization.Text(source);
        if(present && power.PowerLineStatus==Forms.PowerLineStatus.Offline && remaining.TotalSeconds>0)
            detail+=" · "+Localization.Format("About {0:0} min remaining",Math.Ceiling(remaining.TotalMinutes));
        Motion.Text(status,detail);
        level.Visibility=showPercent?Visibility.Visible:Visibility.Collapsed;
        if(showPercent)level.Value=percent;
    }

    private FrameworkElement BuildPeekShortcuts()
    {
        var page=PeekSummary("shortcuts");
        for(int i=0;i<2;i++)peekShortcutTiles.ColumnDefinitions.Add(new());
        peekShortcutTiles.Margin=new(0,6,0,0);
        page.Children.Add(peekShortcutTiles);
        var open=GlassPeekButton("All shortcuts","All shortcuts",OpenPeekPage);
        open.HorizontalAlignment=HorizontalAlignment.Left;
        open.FontSize=11;
        var add=GlassPeekButton("Add shortcut","Add shortcut",()=>app.ShowSettings("Launch"));
        var actions=GlassDockAppearance.ActionRow(open,add);
        actions.Margin=new(0,4,0,0);
        page.Children.Add(actions);
        return page;
    }

    private void UpdatePeekShortcuts()
    {
        if(!peekViews.ContainsKey("shortcuts"))return;
        var shortcuts=app.Settings.Shortcuts.Take(4).ToArray();
        if(peekShortcutTiles.Children.Count>0 && shownPeekShortcuts.SequenceEqual(shortcuts))return;
        shownPeekShortcuts=shortcuts;
        peekShortcutTiles.Children.Clear();
        peekShortcutTiles.RowDefinitions.Clear();
        if(shortcuts.Length==0)
        {
            var empty=UI.Caption("Add shortcuts in Settings.",11,UI.Muted);
            Grid.SetColumnSpan(empty,2);
            peekShortcutTiles.Children.Add(empty);
            return;
        }
        for(int i=0;i<shortcuts.Length;i++)
        {
            var shortcut=shortcuts[i];
            if(i%2==0)peekShortcutTiles.RowDefinitions.Add(new(){Height=GridLength.Auto});
            string name=shortcut switch
            {
                {Name:"Explorer",Target:"explorer.exe"}=>Localization.Text("File Explorer"),
                {Name:"Settings",Target:"ms-settings:"}=>Localization.Text("Settings"),
                _=>shortcut.Name
            };
            var button=GlassPeekButton(name,name,()=>
            {
                try{ShortcutService.Open(shortcut);Collapse();}
                catch(Exception ex){app.Notify(Localization.Text("Could not open shortcut"),ex.Message);}
            });
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            button.Padding=new(8,2,6,2);
            button.Height=31;
            button.Margin=new(0,0,6,4);
            button.ToolTip=name;
            var row=new DockPanel{LastChildFill=true,IsHitTestVisible=false};
            var icon=new ShortcutIcon(shortcut);
            var fallback=Icons.Create(shortcut.Target=="explorer.exe"?"folder":"nav-launch",16);
            icon.AvailabilityChanged+=available=>fallback.Visibility=available?Visibility.Collapsed:Visibility.Visible;
            var plate=new Grid{Width=24,Height=24,Margin=new(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};
            plate.Children.Add(fallback);
            plate.Children.Add(icon);
            row.Children.Add(plate);
            var label=UI.Text(name,11);
            label.TextWrapping=TextWrapping.NoWrap;
            label.TextTrimming=TextTrimming.CharacterEllipsis;
            label.VerticalAlignment=VerticalAlignment.Center;
            row.Children.Add(label);
            button.Content=row;
            Grid.SetRow(button,i/2);
            Grid.SetColumn(button,i%2);
            peekShortcutTiles.Children.Add(button);
        }
    }

    private FrameworkElement BuildPeekAwake()
    {
        var page=PeekSummary("awake");
        var status=UI.Text("",11,UI.Muted);
        status.Margin=new(0,4,0,2);
        page.Children.Add(status);
        var duration=CreateAwakeDuration();
        duration.MinHeight=26;
        duration.Margin=new(0,2,0,2);
        duration.Padding=new(7,2,7,2);
        var display=new CheckBox{Content=UI.Caption("Keep the display on too",11)};
        display.Margin=new(0,2,0,3);
        UI.Named(display,Localization.Text("Keep the display on too"));
        var toggle=GlassPeekButton("Start keep awake","Start keep awake",()=>
        {
            MutateUtility(()=>{if(app.Awake.Active)app.Awake.Stop();else StartAwake(duration,display);});
            refreshPeekAwake?.Invoke();
        });
        toggle.HorizontalAlignment=HorizontalAlignment.Left;
        toggle.FontSize=11;
        page.Children.Add(duration);
        page.Children.Add(display);
        page.Children.Add(toggle);
        refreshPeekAwake=()=>
        {
            bool active=app.Awake.Active;
            Motion.Text(status,active?AwakeSummary(DateTimeOffset.UtcNow).Replace("\n"," · "):Localization.Text("Off"));
            var optionVisibility=active?Visibility.Collapsed:peekDetailed?Visibility.Visible:Visibility.Hidden;
            duration.Visibility=display.Visibility=optionVisibility;
            string action=Localization.Text(active?"Stop keep awake":"Start keep awake");
            if(!Equals(toggle.Content,action)){toggle.Content=action;UI.Named(toggle,action);}
        };
        refreshPeekAwake();
        return page;
    }
}

internal sealed class WeatherGlance : UserControl
{
    private readonly App app;
    private readonly Grid scene=new();
    private readonly TextBlock temperature=UI.Text("—°",34),detail=UI.Text(Localization.Text("Choose a city in Weather"),11,UI.Muted);
    private readonly WeatherArtwork art=new(){Width=84,Height=82,DecorativeMotion=false};
    private System.Threading.CancellationTokenSource? lifetime;
    private WeatherSnapshot? shown;
    private DateTimeOffset shownAt;
    private bool busy;
    internal WeatherGlance(App app)
    {
        this.app=app;var content=new Grid{VerticalAlignment=VerticalAlignment.Top};content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new(){Width=new GridLength(90)});var info=new StackPanel();info.Children.Add(UI.Heading(Localization.Text("Weather"),13));info.Children.Add(temperature);info.Children.Add(detail);content.Children.Add(info);Grid.SetColumn(art,1);content.Children.Add(art);scene.Children.Add(content);Content=scene;
        Loaded+=(_,_)=>{lifetime??=new();app.Updated-=Update;app.SettingsChanged-=Changed;app.Updated+=Update;app.SettingsChanged+=Changed;Watch(IsVisible);Changed();};
        Unloaded+=(_,_)=>{Watch(false);app.Updated-=Update;app.SettingsChanged-=Changed;lifetime?.Cancel();lifetime?.Dispose();lifetime=null;};
        IsVisibleChanged+=(_,_)=>{if(!IsLoaded)return;Watch(IsVisible);if(IsVisible)Changed();};
    }
    private void Watch(bool on){app.Weather.SetVisible(this,on);app.Weather.Changed-=ServiceChanged;if(on)app.Weather.Changed+=ServiceChanged;}
    private void ServiceChanged()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(ServiceChanged);return;}
        if(IsLoaded && IsVisible && app.Weather.Saved(app.Settings) is {} saved)Show(saved);
    }
    // The minute tick refreshes only the age label; downloads follow the app's weather schedule.
    private void Update(){if(IsVisible && shown!=null && (app.CurrentUpdate&RuntimeChange.Clock)!=0 && DateTimeOffset.UtcNow-shownAt>=TimeSpan.FromMinutes(1))Show(shown);}
    private void Show(WeatherSnapshot data)
    {
        shownAt=DateTimeOffset.UtcNow;string? stale=WeatherSchedule.StaleLabel(shownAt,data.Updated);
        Motion.Text(temperature,$"{data.Temperature:0}°");Motion.Text(detail,WeatherService.Condition(data.Code)+(stale!=null?" · "+stale:Localization.Format(" · H {0:0}° L {1:0}°",data.High,data.Low)));
        if(ReferenceEquals(data,shown))return;shown=data;art.Code=data.Code;art.Day=data.Day;art.ReduceMotion=true;art.InvalidateVisual();
        if(scene.Children[0] is WeatherSky old)scene.Children.Remove(old);scene.Children.Insert(0,new WeatherSky{Code=data.Code,Day=data.Day});
    }
    private async void Changed()
    {
        if(!IsVisible || lifetime==null || busy)return;string key=WeatherService.Key(app.Settings);
        if(shown!=null && shown.Key!=key){shown=null;Motion.Text(temperature,"—°");}
        if(key.Length==0){Motion.Text(detail,Localization.Text("Choose a city in Weather"));return;}
        // Show the saved outlook at once instead of a blank glance while a due refresh runs.
        if(app.Weather.Saved(app.Settings) is {} saved)Show(saved);
        busy=true;var token=lifetime.Token;
        try
        {
            var data=await app.Weather.Get(app.Settings,false,token);if(!IsLoaded || key!=WeatherService.Key(app.Settings))return;
            Show(data);
        }
        catch(System.OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception){if(IsLoaded && shown==null)Motion.Text(detail,Localization.Text("Open Weather for your outlook"));}
        finally{busy=false;}
    }
}
