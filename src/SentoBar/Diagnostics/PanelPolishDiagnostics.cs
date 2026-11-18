using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SentoBar;

internal static class PanelPolishDiagnostics
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);

    internal static async Task Run(App app,OverlayWindow island,Action<string,bool> check,Action<string> capture)
    {
        var s=app.Settings;
        s.CompactLayout=s.HoverPeek=s.HoverFollowsWidgets=s.PanelAutoHeight=s.PanelSwipe=s.ReduceMotion=true;
        s.HideMaximized=s.HideFullscreen=s.AlwaysTucked=s.SmartActivities=false;
        s.PanelWidth=526;s.PanelPadding=32;s.PanelScale=1.04;s.PanelMaxHeight=300;s.HoverWidth=500;s.HoverHeight=184;
        s.NotificationDelivery=s.CountdownSound="Off";
        s.ShowDate=s.ClockSeconds=true;
        string[] order={"media","system","timer","focus","tasks","shortcuts","battery","awake","notes","power","files","clips","clock"};
        s.Widgets=order.Select(id=>new WidgetSetting{Id=id}).Concat(Settings.Names.Keys.Except(order).Where(id=>id!="tools").Select(id=>new WidgetSetting{Id=id,Enabled=false})).ToList();
        app.Session.Timer.Reset();
        app.Session.Focus.Reset();
        s.TimerDurationSeconds=3;
        s.PanelNavigationStyle="Labels";
        s.Preset="iPhone";s.Radius=13;s.ClosedAmbient=s.CoverAmbient=true;s.ClosedLightColor="#83B5E9";
        app.ChangeSettings();
        island.Collapse();
        await Task.Delay(150);
        capture("glass-closed");
        island.Reveal(false);

        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(island)!;
        IEnumerable<T> Visible<T>() where T:FrameworkElement=>Diagnostics.Descendants(island).OfType<T>().Where(c=>c.IsVisible);
        T Named<T>(string name) where T:FrameworkElement=>Visible<T>().First(c=>AutomationProperties.GetName(c)==Localization.Text(name));
        void Click(string name)=>Named<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Settle()
        {
            await Task.Delay(130);
            island.UpdateLayout();
        }

        await Settle();
        var tabs=Field<Dictionary<string,Button>>("compactTabs");
        string[] expandedOrder=tabs.Keys.Select(id=>id switch{"status"=>"system","access"=>"shortcuts",_=>id}).ToArray();
        check("Expanded and mini share the configured widget order",expandedOrder.SequenceEqual(order) && island.HoverDeckIds.SequenceEqual(order));
        check("Expanded tabs show their names on glass pills",(tabs["timer"].Content as TextBlock)?.Text==Localization.Text("Timer") &&
            (tabs["media"].Content as TextBlock)?.Text==Localization.Text("Music") && tabs["timer"].Tag is CornerRadius);
        check("Settings and notifications have direct header buttons",Named<Button>("Settings").IsVisible &&
            Named<Button>("Notifications").IsVisible && !Visible<Button>().Any(b=>UI.Identity(b)=="Widgets"));
        check("Navigation labels have no tooltip",tabs.Values.All(tab=>tab.ToolTip==null));
        check("The link cleaner is removed from both panels",!Settings.Names.ContainsKey("links") &&
            !island.HoverDeckIds.Contains("links") && !tabs.ContainsKey("links"));
        s.PanelNavigationStyle="Dots";
        app.ChangeSettings();
        await Settle();
        check("Expanded navigation can use dots",tabs["timer"].Content is System.Windows.Shapes.Rectangle);
        island.Collapse();
        island.PreviewActivity();
        island.SelectPeek("focus");
        await Settle();
        var miniTabs=Field<Dictionary<string,Button>>("peekDots");
        check("Mini navigation uses the same dots setting without tooltips",
            miniTabs["focus"].Content is System.Windows.Shapes.Rectangle && miniTabs["focus"].ToolTip==null);
        capture("panel-navigation-dots");
        island.Reveal(false);
        await Settle();
        check("Expanding from the mini panel keeps the fourth widget",island.ActivePage=="focus");
        island.SelectPage("notes");
        island.SelectPage("timer");
        island.Collapse();
        island.PreviewActivity();
        await Settle();
        check("Mini panel opens at the widget selected in the expanded panel",island.PeekPage=="timer");
        island.Collapse();
        s.PanelNavigationStyle="Labels";
        app.ChangeSettings();
        island.Reveal(false);
        await Settle();

        island.SelectPage("status");island.SelectMonitorTab("overview");
        for(int i=0;i<50 && !island.MonitorReady;i++)await Task.Delay(100);
        await Settle();
        var overview=Field<ScrollViewer>("monitorDetailScroll");
        var outer=Field<ScrollViewer>("scroll");
        var rows=Field<StackPanel>("monitorRows");
        check($"Expanded system overview fits (rows {rows.ActualHeight:0.##}, panel {island.PaintedHeight:0.##}, viewport {outer.ViewportHeight:0.##}, extent {outer.ExtentHeight:0.##})",
            !overview.IsVisible && outer.ScrollableHeight<=1);
        check("Overview includes CPU memory and system uptime",Visible<TextBlock>().Any(text=>text.Text==Localization.Text("System uptime")) && rows.Children.Count>=6);
        var systemTabs=Field<Dictionary<string,Button>>("monitorTabs");
        var glassFill=island.FindResource("GlassControlFill");
        check("The active System tab uses the same glass button style as calendar actions",
            systemTabs["overview"].Style==island.FindResource("GlassPanelButton") &&
            Equals(systemTabs["overview"].Background,glassFill) && systemTabs["hardware"].Background==Brushes.Transparent);
        capture("system-overview");
        check("Wheel over overview advances to Timer",island.RoutePanelWheel(-120,false,Field<StackPanel>("monitorRows")) && island.ActivePage=="timer");
        await Settle();
        check("Timer has its own expanded panel and fits with a 300 DIP saved height",Visible<CountdownView>().Count()==1 && outer.ScrollableHeight<=1);
        check("Expanded timer retains the circular dial and its duration editor",Visible<ProgressRing>().Any(r=>r.ActualHeight==176) &&
            Visible<DurationPicker>().Count()==1);
        capture("expanded-timer");
        island.SelectPage("focus");await Settle();
        check("Expanded Focus and Break controls fit",Visible<DurationPicker>().Count()==2 && outer.ScrollableHeight<=1);
        capture("expanded-focus");

        island.Collapse();island.PreviewActivity();island.SelectPeek("timer");await Settle();
        Click("Edit timer");await Settle();
        var seconds=Visible<TextBox>().Single(t=>UI.Identity(t)=="timer Seconds");
        island.RoutePeekWheel(120,false,seconds,true);
        check("Ctrl with the wheel edits seconds in the mini Timer",seconds.Text=="04" && island.PeekPage=="timer" && island.IsPeeking);
        capture("mini-timer-editor");
        Click("Save duration");await Settle();
        check("Mini Timer saves the exact duration without opening expanded",s.TimerDurationSeconds==4 && island.IsPeeking && !island.IsExpanded);

        var timer=app.Session.Timer;
        s.TimerDurationSeconds=59*60+59;
        app.ChangeSettings();
        Click("Add one minute to timer");
        check("Mini minutes wrap from 59 to 00 without carrying or starting",s.TimerDurationSeconds==59 && !timer.Running);
        Click("Subtract one minute from timer");
        check("Mini minutes wrap from 00 to 59",s.TimerDurationSeconds==59*60+59);
        Click("Add one second to timer");
        check("Mini seconds wrap from 59 to 00 without changing minutes",s.TimerDurationSeconds==59*60);
        Click("Subtract one second from timer");
        check("Mini seconds wrap from 00 to 59",s.TimerDurationSeconds==59*60+59);
        timer.Start(119,DateTimeOffset.UtcNow);
        timer.Pause(DateTimeOffset.UtcNow);
        app.SaveSession();
        var run=timer.RunId;
        Click("Add one second to timer");
        check("Mini adjusts the paused timer without starting a new run",!timer.Running && timer.RunId==run &&
            Math.Ceiling(timer.Remaining(DateTimeOffset.UtcNow))==60);
        Click("Start or pause preview timer");
        Click("Add one minute to timer");
        check("Mini adjusts a running timer without pausing or changing its identity",timer.Running && timer.RunId==run &&
            timer.Remaining(DateTimeOffset.UtcNow)>118 && timer.Remaining(DateTimeOffset.UtcNow)<=120);
        timer.Reset();
        app.SaveSession();
        s.TimerDurationSeconds=59;
        app.ChangeSettings();
        Click("Add one second to timer");
        check("A zero-duration mini timer stays stopped until given time",s.TimerDurationSeconds==0 && !timer.Running &&
            !Named<Button>("Start or pause preview timer").IsEnabled);
        s.TimerDurationSeconds=600;
        app.ChangeSettings();
        await Settle();
        capture("glass-mini-timer");
        s.HoverWidth=300;app.ChangeSettings();await Settle();
        check("All mini Timer controls fit at the smallest supported width",island.HoverOverflow().Length==0);
        check("Mini navigation can show names without a black tooltip",
            (miniTabs["timer"].Content as TextBlock)?.Text==Localization.Text("Timer") && miniTabs["timer"].ToolTip==null);
        s.ReduceMotion=false;
        Motion.Configure(s);
        var peekViews=Field<Dictionary<string,FrameworkElement>>("peekViews");
        ProgressBar Progress(string id)=>Diagnostics.Descendants(peekViews[id]).OfType<ProgressBar>().Single();
        app.Session.Timer.Start(6,DateTimeOffset.UtcNow);
        app.Session.Focus.Start(6,DateTimeOffset.UtcNow,"focus");
        app.SaveSession();
        foreach(string id in new[]{"timer","focus"})
        {
            var clock=id=="timer"?app.Session.Timer:app.Session.Focus;
            island.SelectPeek(id);
            clock.Start(6,DateTimeOffset.UtcNow,id);
            app.SaveSession();
            var bar=Progress(id);
            await Task.Delay(130);
            var first=bar.Value;
            var indicator=(FrameworkElement)bar.Template.FindName("PART_Indicator",bar);
            var firstWidth=indicator.ActualWidth;
            var firstSecond=Math.Ceiling(clock.Remaining(DateTimeOffset.UtcNow));
            await Task.Delay(170);
            var second=bar.Value;
            double remaining=clock.Remaining(DateTimeOffset.UtcNow);
            check("Mini "+id+" drains smoothly within one displayed second",
                bar.IsVisible && firstSecond==Math.Ceiling(remaining) && first>second+.01 &&
                firstWidth>indicator.ActualWidth+1 && Math.Abs(second-remaining/clock.DurationSeconds)<.06 &&
                DependencyPropertyHelper.GetValueSource(bar,ProgressBar.ValueProperty).IsAnimated);

            Click(id=="timer"?"Start or pause preview timer":"Start or pause preview focus");
            var paused=bar.Value;
            await Task.Delay(100);
            check("Mini "+id+" progress freezes while paused",!clock.Running && Math.Abs(bar.Value-paused)<.001 &&
                Math.Abs(paused-clock.Remaining(DateTimeOffset.UtcNow)/clock.DurationSeconds)<.01);
            Click(id=="timer"?"Start or pause preview timer":"Start or pause preview focus");
            await Task.Delay(130);
            check("Mini "+id+" progress continues after resuming",clock.Running && bar.Value<paused-.01);

            island.SelectPeek("notes");
            await Task.Delay(130);
            check("Hidden mini "+id+" stops animating",!bar.IsVisible &&
                !DependencyPropertyHelper.GetValueSource(bar,ProgressBar.ValueProperty).IsAnimated);
        }
        island.SelectPeek("timer");
        await Task.Delay(130);
        check("Reopened mini Timer reflects the time elapsed while hidden",
            Math.Abs(Progress("timer").Value-timer.Remaining(DateTimeOffset.UtcNow)/timer.DurationSeconds)<.06);
        timer.Reset();
        app.Session.Focus.Reset();
        s.ReduceMotion=true;
        Motion.Configure(s);
        app.SaveSession();

        var samples=new List<int>();
        void SampleCountdown()
        {
            if((app.CurrentUpdate&RuntimeChange.Countdown)==0)return;
            int seconds=(int)Math.Ceiling(timer.Remaining(app.UpdateSampleTime));
            if(samples.Count==0 || samples[^1]!=seconds)samples.Add(seconds);
        }
        app.Updated+=SampleCountdown;
        await Task.Delay(240);
        timer.Start(4,DateTimeOffset.UtcNow);
        app.SaveSession();
        for(int i=0;i<100 && (samples.Count==0 || samples[^1]!=0);i++)await Task.Delay(60);
        app.Updated-=SampleCountdown;
        check("A live timer displays every second from 4 to 0 on its own deadline",samples.SequenceEqual(new[]{4,3,2,1,0}));
        timer.Reset();
        app.SaveSession();
        capture("glass-mini-timer-narrow");
        island.SelectPeek("focus");await Settle();
        Click("Edit focus");await Settle();
        check("Narrow mini Focus has both duration editors",Visible<DurationPicker>().Count()==2);
        Click("Cancel");await Settle();
        s.HoverWidth=500;app.ChangeSettings();
        for(int i=1;i<=6;i++)app.Productivity.AddTask("Task "+i);
        island.SelectPeek("tasks");await Settle();
        var tasks=Visible<ScrollViewer>().Single(list=>UI.Identity(list)=="Task list");
        check("Mini Tasks includes all six tasks and has room for three rows",Visible<CheckBox>().Count()==6 && tasks.ViewportHeight>=72 && tasks.ScrollableHeight>0);
        capture("mini-tasks");
        island.RoutePeekWheel(-120,false,Field<StackPanel>("peekTasks"),true);await Settle();
        check("Ctrl with the wheel scrolls Mini Tasks without changing tabs",tasks.VerticalOffset>0 && island.PeekPage=="tasks");
        check("Wheel above a long mini task list changes widgets",island.RoutePeekWheel(-120,false,Field<StackPanel>("peekTasks")) && island.PeekPage=="shortcuts");
        island.OpenFeaturePage("tasks");
        await Settle();
        var taskDraft=Named<TextBox>("New task");
        check("The task editor uses a glass frame",taskDraft.Style==island.FindResource("GlassPanelInput"));
        check("Wheel above the task input changes the expanded widget",island.RoutePanelWheel(-120,false,taskDraft) && island.ActivePage=="access");
        island.OpenFeaturePage("notes");
        await Settle();
        var note=Named<TextBox>("Quick notes");
        check("Both Notes fields use the glass input style",note.Style==island.FindResource("GlassPanelInput") &&
            Named<TextBox>("Note title").Style==note.Style);
        s.PanelMaxHeight=417;
        app.ChangeSettings();
        await Settle();
        capture("glass-note-fields");
        island.Collapse();
        island.PreviewActivity();

        island.SelectPeek("files");await Settle();
        check("The file shelf names its drop target",Visible<TextBlock>().Any(t=>t.Text==Localization.Text("Drop files onto this panel")));
        island.SelectPeek("battery");await Settle();Named<Button>("Power settings");capture("mini-battery");
        check("Mini Battery explicitly identifies the charge level or its availability",Field<TextBlock>("peekBatteryValue").Text.Length>0);
        island.SelectPeek("shortcuts");await Settle();
        check("Mini Shortcuts show Explorer as an outlined tile with a centered icon",
            Named<Button>("File Explorer").Content is DockPanel shortcut &&
            shortcut.Children.OfType<Grid>().First().VerticalAlignment==VerticalAlignment.Center &&
            Named<Button>("File Explorer").BorderThickness.Left>0);
        capture("mini-shortcuts");

        island.SelectPeek("awake");await Settle();
        var duration=Named<ComboBox>("Keep awake duration");
        duration.SelectedIndex=1;
        Named<CheckBox>("Keep the display on too").IsChecked=true;
        capture("mini-awake");
        try
        {
            Click("Start keep awake");
            check("The mini panel starts the selected one-hour awake session with display on",app.Awake.Active && app.Awake.KeepDisplayOn && app.Awake.Until>DateTimeOffset.UtcNow.AddMinutes(59));
            Click("Stop keep awake");
            check("The mini panel stops the same awake session",!app.Awake.Active);
        }
        finally{if(app.Awake.Active)app.Awake.Stop();}

        island.SelectPeek("clock");await Settle();
        check("Clock mini includes dates and seconds when enabled",Field<TextBlock>("peekDate").IsVisible && Field<Dictionary<string,TextBlock>>("extraPeekLabels")["clock"].Text.Count(ch=>ch==':')==2);
        capture("mini-clock");
        island.OpenPeekPage();await Settle();
        check("Opening mini Clock selects the dedicated expanded Clock tab",island.ActivePage=="clock" && Field<TextBlock>("clock").IsVisible);
        island.SelectPage("power");await Settle();
        var plans=Named<ComboBox>("Available Windows power plans");
        for(int i=0;i<80 && plans.Items.Count==0;i++)await Task.Delay(100);
        check("Power plans display an active Windows name",plans.Items.OfType<PowerPlan>().Any(p=>p.Active && !string.IsNullOrWhiteSpace(p.Name) && !p.Name.Contains('�')));
        var applyPlan=Named<Button>("Apply power plan");
        check("Power plan choices keep their fill and the action uses the outlined glass button style",
            plans.Style==island.FindResource("GlassPanelChoice") &&
            applyPlan.Style==island.FindResource("GlassPanelButton") &&
            applyPlan.Background==Brushes.Transparent &&
            Equals(plans.BorderBrush,applyPlan.BorderBrush));
        capture("power-plan");

        s.ClipboardHistory=true;
        app.ChangeSettings();
        for(int i=1;i<=30;i++)app.Productivity.Clips.Add(new(){Kind="text",Text="Clipboard entry "+i});
        island.SelectPage("clips");await Settle();
        check("Clipboard history tiles use the shared glass fill and border",
            Diagnostics.Descendants(Field<ScrollViewer>("clipboardScroll")).OfType<Border>()
                .Any(b=>b.Child is Grid tile && tile.Children.OfType<TextBlock>().Any(t=>t.Text.StartsWith("Clipboard entry ",StringComparison.Ordinal))
                    && Equals(b.Background,island.FindResource("GlassFieldFill")) && Equals(b.BorderBrush,island.FindResource("GlassControlBorder"))));
        foreach(double width in new[]{526d,300d})
        {
            s.PanelWidth=width;app.ChangeSettings();await Settle();
            var heading=Visible<TextBlock>().Single(t=>t.Text==Localization.Text("Smart clipboard"));
            var count=Visible<TextBlock>().Single(t=>t.Text=="30"+Localization.Text(" items"));
            var pause=Named<CheckBox>("Pause capture");
            Rect Bounds(FrameworkElement item)=>item.TransformToAncestor(island).TransformBounds(new Rect(item.RenderSize));
            bool Overlap(FrameworkElement left,FrameworkElement right)
            {
                var intersection=Rect.Intersect(Bounds(left),Bounds(right));
                return !intersection.IsEmpty && intersection.Width>.5 && intersection.Height>.5;
            }
            var more=Named<Button>("Clipboard actions");
            check("Clipboard title count and controls fit at width "+width,
                !Overlap(heading,count) && !Overlap(heading,pause) && !Overlap(count,pause) &&
                !Overlap(pause,more) && Bounds(more).Right<=(island.ActualWidth+island.PaintedWidth)/2-10);
            capture("clipboard-"+width);
        }

        async Task VerifyMouseScrollbar(ScrollViewer viewer,string caption)
        {
            viewer.ScrollToTop();
            await Settle();
            var bar=(ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar",viewer);
            var track=(Track)bar.Template.FindName("PART_Track",bar);
            bool visible=bar.IsVisible && bar.Opacity>.9 && track.Thumb.IsHitTestVisible && viewer.ScrollableHeight>0;
            string page=island.ActivePage;
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(0,28){RoutedEvent=Thumb.DragDeltaEvent});
            await Settle();
            check(caption+" has a visible draggable scroll thumb",
                visible && viewer.VerticalOffset>0 && island.ActivePage==page);
        }
        await VerifyMouseScrollbar(Field<ScrollViewer>("clipboardScroll"),"Clipboard");
        for(int i=0;i<12;i++)app.Productivity.AddTask("Scrollbar fixture "+i);
        island.SelectPage("tasks");await Settle();
        await VerifyMouseScrollbar(Field<ScrollViewer>("taskViewport"),"Tasks");

        island.SelectPage("access");await Settle();
        var commandLauncher=Named<Button>("Search commands  ·  Ctrl+K");
        check("The command search launcher has the common transparent background and glass border",
            commandLauncher.Background==Brushes.Transparent && Equals(commandLauncher.BorderBrush,island.FindResource("GlassControlBorder")));
        capture("glass-command-launcher");
        commandLauncher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
        check("Command search opens with the glass input",Equals(Field<TextBox>("commandInput").Background,island.FindResource("GlassFieldFill")) &&
            Field<TextBox>("commandInput").Style==island.FindResource("GlassPanelInput"));
        Visible<Button>().First(b=>UI.Identity(b)==Localization.Text("Back")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var cover=new CoverPalette(Colors.Red,Colors.Pink);
        s.ClosedLightColor="#43B581";s.ClosedLightFollowsMusic=true;s.ClosedLightKeepsLastCover=false;
        check("Closed playback uses cover, idle uses the chosen color",AmbientPolicy.ClosedColors(s,cover,true).Primary==Colors.Red && AmbientPolicy.ClosedColors(s,cover,false).Primary==Color.FromRgb(0x43,0xb5,0x81));
        s.ClosedLightFollowsMusic=false;s.ClosedLightKeepsLastCover=true;
        check("The two cover switches work independently",AmbientPolicy.ClosedColors(s,cover,true).Primary==Color.FromRgb(0x43,0xb5,0x81) && AmbientPolicy.ClosedColors(s,cover,false).Primary==Colors.Red);

        s.CoverAmbient=s.ClosedAmbient=true;
        s.AmbientStrength=.65;
        var artwork=new DrawingImage(new GeometryDrawing(Brushes.Red,null,new RectangleGeometry(new Rect(0,0,32,32))));
        var mediaArtwork=typeof(MediaService).GetProperty("Artwork")!;
        island.Collapse();
        app.Media.DiagnosticPlayback(true);
        mediaArtwork.SetValue(app.Media,artwork);
        app.ChangeSettings();
        await Settle();
        var surface=Field<IslandSurface>("surface");
        var chosen=Color.FromRgb(0x43,0xb5,0x81);
        check("Playback can use the fixed color on the actual dock",surface.AmbientVisible && surface.AmbientColor==chosen && !surface.AmbientAnimating);
        var remembered=app.AmbientAudio.LastCoverColors.Primary;
        app.Media.DiagnosticPlayback(false);
        mediaArtwork.SetValue(app.Media,null);
        app.PublishUpdate(RuntimeChange.Media);
        await Settle();
        check("Idle can keep the last cover after the media session clears its artwork",surface.AmbientColor==remembered && remembered!=chosen);
        s.ClosedLightKeepsLastCover=false;
        app.ChangeSettings();
        await Settle();
        check("Turning off last cover restores the chosen idle color",surface.AmbientVisible && surface.AmbientColor==chosen);
        capture("closed-chosen-color");

        s.AmbientScope="Music only";s.AmbientColorSource="Custom color";s.AmbientCustomColor="#8063CC";
        app.ChangeSettings();
        island.PreviewActivity();
        island.SelectPeek("timer");
        await Settle();
        check("Mini Timer inherits the chosen collapsed color even when expanded lighting is restricted to music",
            island.IsPeeking && surface.AmbientVisible && surface.AmbientColor==chosen);
        app.Media.DiagnosticPlayback(true);
        mediaArtwork.SetValue(app.Media,artwork);
        app.PublishUpdate(RuntimeChange.Media);
        await Settle();
        check("Mini keeps the manual color when playback following is off",
            surface.AmbientColor==chosen && !surface.AmbientAnimating);
        s.ClosedLightFollowsMusic=true;
        app.ChangeSettings();
        await Settle();
        check("Mini follows album artwork during playback when enabled",surface.AmbientColor==remembered);
        s.ClosedLightKeepsLastCover=true;
        app.Media.DiagnosticPlayback(false);
        mediaArtwork.SetValue(app.Media,null);
        app.PublishUpdate(RuntimeChange.Media);
        await Settle();
        check("Mini retains the last album color after playback ends when enabled",
            surface.AmbientColor==remembered && !surface.AmbientAnimating);
        s.ClosedLightKeepsLastCover=false;
        app.ChangeSettings();
        await Settle();
        capture("mini-chosen-color");
        check("Mini returns to the chosen color after disabling the last album color",surface.AmbientColor==chosen);
        s.ClosedAmbient=false;app.ChangeSettings();await Settle();
        check("The compact lighting switch also turns off lighting in the mini panel",!surface.AmbientVisible);
        s.ClosedAmbient=true;s.AmbientScope="All widgets";
        app.ChangeSettings();
        island.OpenPeekPage();
        await Settle();
        check("Expanded panel keeps its own ambient color",surface.AmbientColor==Color.FromRgb(0x80,0x63,0xcc));
        island.Collapse();

        var settings=new SettingsWindow(app){ShowActivated=false,Width=980,Height=820};
        try
        {
            settings.Show();
            settings.OpenCategory("Ambience");
            await Settle();
            var controls=Diagnostics.LogicalDescendants(settings).OfType<CheckBox>().Where(c=>c.IsVisible).ToArray();
            check("Both music switches and the color editor are visible under Ambience",
                controls.Any(c=>UI.Identity(c)=="Follow album artwork during playback") &&
                controls.Any(c=>UI.Identity(c)=="Keep the last album color when idle") &&
                Diagnostics.LogicalDescendants(settings).OfType<ColorPicker>().Any(c=>c.IsVisible));
            Diagnostics.LogicalDescendants(settings).OfType<Expander>().Single(e=>UI.Identity(e)=="Settings live preview").IsExpanded=true;
            var modes=(Dictionary<string,Button>)typeof(SettingsWindow).GetField("previewModeButtons",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(settings)!;
            modes["Hover"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            var preview=(IslandSurface)typeof(SettingsWindow).GetField("ambientPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(settings)!;
            check("Settings hover preview matches the mini panel color",preview.AmbientVisible && preview.AmbientColor==chosen);
            settings.OpenCategory("Controls");
            var navigation=Diagnostics.LogicalDescendants(settings).OfType<ComboBox>().Single(c=>UI.Identity(c)=="Panel navigation style");
            navigation.SelectedItem="Icons";
            for(int attempt=0;attempt<20 && app.Store.Load<Settings>("settings").PanelNavigationStyle!="Icons";attempt++)
                await Task.Delay(50);
            check("Settings saves and reloads the icon navigation style",app.Store.Load<Settings>("settings").PanelNavigationStyle=="Icons");
            navigation.SelectedItem="Labels";
        }
        finally{settings.Close();}

        s.DetachablePreviews=true;s.TwoStageHover=false;s.HoverWidth=300;
        app.ChangeSettings();island.Collapse();island.PreviewActivity();island.SelectPeek("timer");
        await Settle();
        var miniSettings=Named<Button>("Settings");
        check("The narrow mini panel has a visible settings button",miniSettings.IsVisible && miniSettings.ActualWidth>0 && island.HoverOverflow().Length==0);
        miniSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        var openedSettings=Application.Current.Windows.OfType<SettingsWindow>().SingleOrDefault(w=>w.IsVisible);
        check("The mini settings button opens Settings",openedSettings!=null);
        openedSettings?.Close();
        island.PreviewActivity();island.SelectPeek("timer");await Settle();
        var detach=Named<Button>("Detach preview");
        miniSettings=Named<Button>("Settings");
        var detachBounds=detach.TransformToAncestor(island).TransformBounds(new Rect(detach.RenderSize));
        var settingsBounds=miniSettings.TransformToAncestor(island).TransformBounds(new Rect(miniSettings.RenderSize));
        var footer=Field<Grid>("peekFooter");
        var pushpin=((Canvas)((Viewbox)Icons.Create("pushpin")).Child).Children.OfType<System.Windows.Shapes.Path>().Single();
        check("One accessible pushpin replaces both the paperclip and the drag grip",
            detach.Content is Viewbox {Child:Canvas glyph} &&
            glyph.Children.OfType<System.Windows.Shapes.Path>().SingleOrDefault() is {} icon && icon.Data==pushpin.Data &&
            detach.IsTabStop && !Visible<System.Windows.Controls.Primitives.Thumb>().Any(thumb=>footer.IsAncestorOf(thumb)));
        bool FooterFits(Button button)
        {
            var bounds=button.TransformToAncestor(footer).TransformBounds(new Rect(button.RenderSize));
            var clip=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(button);
            return bounds.Top>=0 && bounds.Bottom<=footer.ActualHeight+.25 &&
                (clip==null || clip.Bounds.Bottom>=button.ActualHeight-.25);
        }
        check("The minimum-height footer fits the entire detach button and leaves a gap before Settings",
            FooterFits(detach) && FooterFits(miniSettings) && detachBounds.Width<=26 &&
            settingsBounds.Left-detachBounds.Right>=8 && island.HoverOverflow().Length==0);
        capture("mini-detach");
        detach.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        check("The mini Timer Detach button opens its own window",app.Previews.AnyVisible("timer"));
        foreach(var window in Application.Current.Windows.OfType<DetachedPreviewWindow>().ToArray())window.Close();

        island.PreviewActivity();island.SelectPeek("timer");await Settle();
        var drag=typeof(OverlayWindow).GetMethod("TryDetachPeekDrag",BindingFlags.Instance|BindingFlags.NonPublic)!
            .CreateDelegate<Func<Point,Point,bool>>(island);
        var work=Native.ChooseScreen(s).WorkingArea;
        var pointer=new Point(work.Left+64,work.Top+64);
        var local=island.PointFromScreen(pointer);
        check("Small pointer movement does not detach the widget",!drag(local-new Vector(3,1),local) &&
            island.IsPeeking && !app.Previews.AnyVisible("timer"));
        bool started=drag(local-new Vector(18,0),local);
        await Settle();
        var dragged=Application.Current.Windows.OfType<DetachedPreviewWindow>().SingleOrDefault(w=>w.IsVisible);
        check("Dragging the pushpin opens the widget at the pointer",started && !island.IsPeeking && dragged!=null &&
            new Rect(dragged.RenderSize).Contains(dragged.PointFromScreen(pointer)));
        dragged?.Close();

        app.Media.DiagnosticPlayback(true);island.PreviewActivity();island.SelectPeek("media");await Settle();
        check("The minimum media preview fits its playback controls and both footer buttons",
            FooterFits(Named<Button>("Detach preview")) && FooterFits(Named<Button>("Settings")) && island.HoverOverflow().Length==0);
        capture("mini-music-pushpin");
        island.Collapse();app.Media.DiagnosticPlayback(false);

        var voice=new AssistantVoiceView(app);
        var voiceWindow=new Window{Content=voice,Width=500,Height=430,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            voiceWindow.Show();await Task.Delay(80);voiceWindow.UpdateLayout();
            const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
            var privacy=(TextBlock)typeof(AssistantVoiceView).GetField("privacy",fields)!.GetValue(voice)!;
            var opening=(ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs),fields,null,new object[]{true},null)!;
            privacy.RaiseEvent(opening);
            check("Voice status stays visible inline and its WPF hover popup is blocked",
                privacy.IsVisible && privacy.Text.Length>0 && opening.Handled);
        }
        finally{voiceWindow.Close();}

        island.DiagnosticHotkeyRegistration=(_,_,_,_)=>true;
        try
        {
            s.HotkeyEnabled=true;s.AlwaysTucked=true;
            app.ChangeSettings();island.Collapse();island.CheckEnvironment();
            var handle=new System.Windows.Interop.WindowInteropHelper(island).Handle;
            check("The registered panel shortcut starts with the dock tucked",island.IsTucked && island.HasGlobalHotkey);
            PostMessage(handle,0x312,new IntPtr(0x4C55),IntPtr.Zero);
            await Settle();island.CheckEnvironment();
            check("The Windows shortcut message opens the dock and keeps it visible",island.IsExpanded && !island.IsTucked);
            PostMessage(handle,0x312,new IntPtr(0x4C55),IntPtr.Zero);
            await Settle();
            check("The same shortcut returns the dock to its tucked state",!island.IsExpanded && island.IsTucked);

            // The initial settings can already be applied when the native HWND is created.
            int startupRegistrations=0;
            var freshDock=new OverlayWindow(app){DiagnosticRun=true,DiagnosticHotkeyRegistration=(_,_,_,_)=>{startupRegistrations++;return true;}};
            try
            {
                freshDock.Show();
                check("Shortcut registration runs when the native window first becomes available",
                    startupRegistrations==1 && freshDock.HasGlobalHotkey);
            }
            finally{freshDock.Cleanup();freshDock.Hide();}
        }
        finally
        {
            s.HotkeyEnabled=false;s.AlwaysTucked=false;
            app.ChangeSettings();
            island.DiagnosticHotkeyRegistration=null;
        }
    }
}
