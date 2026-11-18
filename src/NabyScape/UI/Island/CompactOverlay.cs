using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private bool peeking;
    private string activePage="media";
    private readonly Dictionary<string,FrameworkElement> compactPages=new();
    private readonly Dictionary<string,Button> compactTabs=new();
    private readonly Grid compactPageHost=new(),peekPanel=new(),timelinePanel=new();
    private readonly TextBlock peekTitle=UI.Caption("",13),peekSubtitle=UI.Caption("",11,UI.Muted);
    private readonly Image peekArt=new(){Width=48,Height=48,Stretch=Stretch.UniformToFill};
    private readonly ProgressBar peekProgress=new(){Minimum=0,Maximum=1,Height=3,Background=UI.Brush("#34363D"),Foreground=Brushes.White,BorderThickness=new(0)};
    private readonly TextBlock peekElapsed=UI.Caption("",11,UI.Muted),peekDuration=UI.Caption("",11,UI.Muted);
    private readonly TranslateTransform peekArtSlide=new();
    private readonly System.Windows.Controls.Grid peekCoverContent=new();
    private FrameworkElement peekFallback=null!;
    private readonly ActivityVisual peekWave=new(){Width=58,Height=16,WaveOnly=true};
    private Button? peekPlay;
    private readonly Slider seekSlider=new(){Minimum=0,Maximum=1,TickFrequency=.001,SmallChange=.01,LargeChange=.05,IsMoveToPointEnabled=true,Height=24,Margin=new(0)};
    private readonly TextBlock elapsed=UI.Caption("0:00",10,UI.Muted),duration=UI.Caption("0:00",10,UI.Muted);
    private readonly DispatcherTimer hoverDelay=new(){Interval=TimeSpan.FromMilliseconds(180)},leaveDelay=new(){Interval=TimeSpan.FromMilliseconds(300)},seekDelay=new(){Interval=TimeSpan.FromMilliseconds(140)};
    private double HostHeight
    {
        get
        {
            if(assistantOpen || VoiceRoomOpen)return AssistantView.PanelSize(app.Settings,VoiceRoomOpen).Height;
            if(!app.Settings.CompactLayout)return expandedHeight;
            var screen=Native.ChooseScreen(app.Settings);return Math.Min(Math.Max(app.Settings.PanelMaxHeight,expandedHeight),screen.WorkingArea.Height/Native.Scale(screen)-16);
        }
    }
    internal bool IsPeeking=>peeking;
    internal string ActivePage=>activePage;
    private static string PageFor(string widget)=>widget switch
    {
        "system"=>"status",
        "shortcuts"=>"access",
        _=>widget
    };
    private static string PageName(string page)=>Localization.Text(page switch
    {
        "media"=>"Music",
        "focus"=>"Focus",
        "status"=>"System monitor",
        "access"=>"Shortcuts",
        _=>Settings.Names.GetValueOrDefault(page)??page
    });
    private string? hoverPanelWidget,selectedPanelWidget;
    private static string WidgetForPage(string page)=>page switch{"status"=>"system","access"=>"shortcuts",_=>page};
    private void RememberPanel(string widget)
    {
        if(Settings.Names.ContainsKey(widget) && !fileDragActive && !shelfDropBrowsing)selectedPanelWidget=widget;
    }
    private bool WidgetOn(string id)=>id==hoverPanelWidget || app.Settings.Widgets.Any(w=>w.Enabled && w.Id==id);
    private IEnumerable<WidgetSetting> PanelWidgets=>app.Settings.Widgets.Where(w=>w.Enabled || w.Id==hoverPanelWidget);
    private void OpenHoverPanel(string widget,bool activate=true)
    {
        LeaveAssistantPanel();
        string page=PageFor(widget);
        bool hidden=!app.Settings.Widgets.Any(w=>w.Enabled && w.Id==widget) || !app.Settings.CompactLayout && Settings.ToolNames.ContainsKey(widget);
        bool leavingMixer=classicMixerOpen;classicMixerOpen=false;
        if(leavingMixer || hoverPanelWidget!=(hidden?widget:null))
        {FlushNotes();hoverPanelWidget=hidden?widget:null;activePage=page;BuildBody();}
        Reveal(activate,followSelection:false);
        if(app.Settings.CompactLayout)SelectPage(page,false);
        scroll.ScrollToTop();UpdateSystemMonitor();
    }
    private void InitializeCompactInteraction()
    {
        ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            // Prepare only the lightweight preview shell after startup has painted.
            // Editors and other widget pages remain lazy. Pointer entry starts its
            // delay immediately, without constructing controls on the input path.
            if(!DiagnosticRun && !app.Quitting && app.Settings.HoverPeek && app.Settings.CompactLayout)EnsurePeekDeck();
        }));
        root.MouseEnter+=(_,_)=>{if(DiagnosticRun)return;if(IsTucked){RevealTuckedPreview();return;}leaveDelay.Stop();if(peeking){automaticPreview=false;activityDismiss.Stop();}else{previewKind=activityTitle=activityDetail="";}if(!coverGesture.Pressed && !expanded && !peeking && app.Settings.HoverPeek && app.Settings.CompactLayout && !userHidden && !TuckRequested)hoverDelay.Start();};
        root.MouseLeave+=(_,_)=>{if(DiagnosticRun)return;hoverDelay.Stop();if(peeking || TuckRequested)leaveDelay.Start();};
        hoverDelay.Tick+=(_,_)=>{hoverDelay.Stop();if(assistantCharacterButton.IsMouseOver){PreviewAssistant();return;}if(PointerInside() && !expanded && !userHidden && !TuckRequested)PreviewActivity();};
        leaveDelay.Tick+=(_,_)=>
        {
            leaveDelay.Stop();if(!(peeking || TuckRequested) || AssistantReplyVisible || PointerInside() || fileDragActive)return;
            // A drop-down hanging outside the panel keeps it open; collapse once it closes with the pointer outside.
            if(OwnedPopupOpen){leaveDelay.Start();return;}
            if(!PeekInteractionActive)Collapse();
        };
        peekMedia.HorizontalAlignment=HorizontalAlignment.Center;peekMedia.VerticalAlignment=VerticalAlignment.Top;peekMedia.Width=286;peekMedia.Height=100;peekMedia.Margin=new(0);
        // Separate the transport timeline, time labels, and audio wave.
        foreach(double height in new[]{48d,14d,15d,19d})
            peekMedia.RowDefinitions.Add(new(){Height=new GridLength(height)});
        peekMedia.ColumnDefinitions.Add(new(){Width=GridLength.Auto});peekMedia.ColumnDefinitions.Add(new());peekMedia.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        peekArt.RenderTransform=peekArtSlide;
        peekCoverContent.Children.Add(WrapArtwork(peekArt));peekFallback=Icons.Create("activity",28);peekCoverContent.Children.Add(peekFallback);
        var cover=new Border{CornerRadius=new(14),Child=peekCoverContent,Background=UI.Brush("#16FFFFFF"),Width=48,Height=48,Margin=new(0,0,16,0)};
        cover.Clip=new RectangleGeometry(new Rect(0,0,48,48),14,14);peekMedia.Children.Add(cover);
        peekTitle.FontSize=15;peekSubtitle.FontSize=12;peekSubtitle.Margin=new(0,6,0,0);
        peekTitle.TextWrapping=TextWrapping.NoWrap;peekTitle.TextTrimming=TextTrimming.CharacterEllipsis;peekTitle.FontWeight=FontWeights.SemiBold;
        peekSubtitle.TextWrapping=TextWrapping.NoWrap;peekSubtitle.TextTrimming=TextTrimming.CharacterEllipsis;
        var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};info.Children.Add(WrapTitle(peekTitle));info.Children.Add(WrapTitle(peekSubtitle));Grid.SetColumn(info,1);peekMedia.Children.Add(info);
        peekPlay=Icons.Button("play",Localization.Text("Play or pause from preview"),()=>_=app.Media.Command("toggle"),true,38);peekPlay.Margin=new(16,0,0,0);Grid.SetColumn(peekPlay,2);peekMedia.Children.Add(peekPlay);
        Grid.SetRow(peekProgress,1);Grid.SetColumnSpan(peekProgress,3);peekProgress.VerticalAlignment=VerticalAlignment.Center;peekMedia.Children.Add(peekProgress);
        var peekTimes=new DockPanel();DockPanel.SetDock(peekDuration,Dock.Right);peekTimes.Children.Add(peekDuration);peekTimes.Children.Add(peekElapsed);Grid.SetRow(peekTimes,2);Grid.SetColumnSpan(peekTimes,3);peekMedia.Children.Add(peekTimes);
        Grid.SetRow(peekWave,3);Grid.SetColumnSpan(peekWave,3);peekWave.VerticalAlignment=VerticalAlignment.Center;peekWave.HorizontalAlignment=HorizontalAlignment.Center;peekMedia.Children.Add(peekWave);

        timelinePanel.RowDefinitions.Add(new(){Height=GridLength.Auto});timelinePanel.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var labels=new DockPanel();DockPanel.SetDock(duration,Dock.Right);labels.Children.Add(duration);labels.Children.Add(elapsed);timelinePanel.Children.Add(labels);
        Grid.SetRow(seekSlider,1);timelinePanel.Children.Add(seekSlider);UI.LocalizedName(seekSlider,"Playback position");
        seekSlider.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            if(!seekSlider.IsEnabled || updating)return;
            seekSlider.ApplyTemplate();
            if(seekSlider.Template.FindName("PART_Track",seekSlider) is not System.Windows.Controls.Primitives.Track track)return;
            // Thumb gestures keep native dragging. Track clicks seek without depending
            // on whether WPF has assigned keyboard focus before ValueChanged fires.
            if(track.Thumb?.IsMouseOver==true)return;
            double target=track.ValueFromPoint(e.GetPosition(track));if(!double.IsFinite(target))return;
            seekSlider.Focus();seekSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty,Math.Clamp(target,seekSlider.Minimum,seekSlider.Maximum));
            seekDelay.Stop();seekDelay.Start();e.Handled=true;
        };
        seekSlider.ValueChanged+=(_,_)=>{if(!updating && (seekSlider.IsMouseCaptureWithin || seekSlider.IsKeyboardFocusWithin)){seekDelay.Stop();seekDelay.Start();}};
        seekDelay.Tick+=async(_,_)=>{seekDelay.Stop();await app.Media.Seek(seekSlider.Value);};
        InitializeMediaTimeline();
    }
    private void StopHoverTimers(){hoverDelay.Stop();leaveDelay.Stop();detailDelay.Stop();assistantHover.Stop();}
    internal void PreviewActivity()
    {
        if(app.Notifications.Current!=null && !expanded && !peeking && !fileDragActive && !userHidden){OpenNotice();return;}
        if(PeekPages().Length==0 || (!app.Settings.CompactLayout && !tuckPreview && !fileDragActive) || (!app.Settings.HoverPeek && !automaticPreview && !tuckPreview && !fileDragActive) || expanded || peeking || userHidden || (TuckRequested && !tuckPreview && !fileDragActive))return;
        peeking=true;RefreshNoticeEdge();SyncAssistantVisibility();Show();Dimensions();ConfigurePerformance();peekDetailed=!app.Settings.TwoStageHover || automaticPreview;detailDelay.Stop();if(!peekDetailed)detailDelay.Start();
        scroll.Visibility=Visibility.Collapsed;compact.IsHitTestVisible=false;Motion.Fade(compact,0,80);
        PreparePeekDeck(restoreSelection:true);
        peekPanel.Visibility=Visibility.Visible;peekPanel.IsHitTestVisible=true;Motion.Fade(peekPanel,1,160);
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,PeekSurfaceWidth,app.Settings.AnimationMs);
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,peekDetailed?PeekExpandedHeight:100,app.Settings.AnimationMs);motion.Go(surface,IslandSurface.GlassLiftProperty,1,app.Settings.AnimationMs);motion.OnComplete(()=>{});UpdatePeek();WakeLiveRendering();
    }
    private void UpdatePeek()
    {
        if(!peeking || noticeShowing || assistantPeeking)return;
        UpdatePeekDeck();
        bool media=app.Media.Available;ApplyMediaTypography(peekTitle,media && app.Settings.BoldMediaTitle);
        peekArt.Visibility=media && app.Media.Artwork!=null?Visibility.Visible:Visibility.Collapsed;SwapArtwork(peekArt,media?app.Media.Artwork:null);
        peekFallback.Visibility=peekArt.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
        var now=DateTimeOffset.Now;
        string caption=media?app.Media.Title:WidgetOn("focus") && app.Session.Focus.Remaining(now)>0?Localization.Text("Focus · ")+app.Session.Focus.Display(now):WidgetOn("timer") && app.Session.Timer.Remaining(now)>0?Localization.Text("Timer · ")+app.Session.Timer.Display(now):now.ToString("HH:mm");
        if(activityDetail==Localization.Text("Ending soon"))caption=(previewKind=="timer"?Localization.Text("Timer · ")+app.Session.Timer.Display(now):Localization.Text("Focus · ")+app.Session.Focus.Display(now));
        if(media)SwapText(peekTitle,caption);else Motion.Text(peekTitle,caption);
        string detail=media?app.Media.Artist:ActiveCountdown()!=null?Localization.Text("Session running · click for controls"):now.ToString("dddd, d MMMM",Localization.Culture);
        if(media)SwapText(peekSubtitle,detail);else Motion.Text(peekSubtitle,detail);
        if(peekPlay!=null){peekPlay.Visibility=media?Visibility.Visible:Visibility.Collapsed;peekPlay.IsEnabled=app.Media.CanToggle;Motion.Icon(peekPlay,app.Media.Playing?"pause":"play",18,Brushes.Black);}
        var position=media?app.Media.Timeline():(ActiveCountdown() is Countdown c?(c.DurationSeconds-c.Remaining(now),c.DurationSeconds):(0d,0d));
        peekProgress.Visibility=position.Item2>0?Visibility.Visible:Visibility.Collapsed;
        peekElapsed.Visibility=peekDuration.Visibility=Visibility.Visible;
        if(!peekSeeking)peekProgress.Value=position.Item2>0?Math.Clamp(position.Item1/position.Item2,0,1):0;
        var format=position.Item2>=3600?@"h\:mm\:ss":@"m\:ss";
        peekElapsed.Text=TimeSpan.FromSeconds(Math.Max(0,position.Item1)).ToString(format);peekDuration.Text=TimeSpan.FromSeconds(Math.Max(0,position.Item2)).ToString(format);
        if(position.Item2<=0){peekElapsed.Text=media?Localization.Text("This player doesn't share its position"):Localization.Text("No active session");peekDuration.Text=media?"":Localization.Text("Open →");}
        if(media && !peekSeeking){timelineAt=DateTimeOffset.UtcNow;timelineElapsed=position.Item1;timelineDuration=position.Item2;}
        peekWave.Visibility=peekId=="media" && media && app.Settings.LiveActivity?Visibility.Visible:Visibility.Collapsed;
        UpdateClosedLyricsDemand();ApplyPeekGeometry();
    }
    private void BuildCompactBody()
    {
        if(timelinePanel.Parent is Panel old)old.Children.Remove(timelinePanel);
        title.FontSize=18;artist.FontSize=12;artwork.Width=artwork.Height=88;clock.FontSize=26;
        var navigation=new Grid{Tag="panel-navigation"};
        Grid.SetIsSharedSizeScope(navigation,true);
        navigation.ColumnDefinitions.Add(new(){Width=GridLength.Auto,SharedSizeGroup="PanelHeaderEdge"});
        navigation.ColumnDefinitions.Add(new());
        navigation.ColumnDefinitions.Add(new(){Width=GridLength.Auto,SharedSizeGroup="PanelHeaderEdge"});
        var launcher=AssistantLauncher();
        launcher.Margin=new(0,0,8,0);launcher.HorizontalAlignment=HorizontalAlignment.Left;
        navigation.Children.Add(launcher);

        var settings=Icons.Button("settings",Localization.Text("Settings"),app.ShowSettings,false,28);
        settings.Margin=new(4,0,0,0);
        settings.Background=Brushes.Transparent;
        var trailing=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(6,0,0,0),HorizontalAlignment=HorizontalAlignment.Right};
        trailing.Children.Add(NotificationBell(28));
        trailing.Children.Add(settings);
        Grid.SetColumn(trailing,2);
        navigation.Children.Add(trailing);

        var tabRow=new StackPanel{Orientation=Orientation.Horizontal};
        var navScroll=compactNavScroll=new ScrollViewer{Content=tabRow,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,PanningMode=PanningMode.HorizontalOnly};
        navScroll.SizeChanged+=(_,_)=>{if(app.Settings.PanelNavigationStyle=="Dots")foreach(var tab in compactTabs)StyleCategory(tab.Value,tab.Key,tab.Key==(activePage=="mixer"?"media":activePage));};
        UI.LocalizedName(navScroll,"Panel navigation");
        KeepTabsWithinViewport(navScroll,tabRow);
        Grid.SetColumn(navScroll,1);
        KeyboardNavigation.SetDirectionalNavigation(tabRow,KeyboardNavigationMode.Cycle);
        navScroll.PreviewMouseWheel+=(_,e)=>
        {
            if(navScroll.ScrollableWidth<=0)return;
            navScroll.ScrollToHorizontalOffset(navScroll.HorizontalOffset-e.Delta*.5);
            e.Handled=true;
        };
        navigation.Children.Add(navScroll);
        var header=new Border{Child=navigation,BorderBrush=GlassDockAppearance.Stroke,BorderThickness=new(0,0,0,1),
            Padding=new(0,0,0,8),Margin=new(0,0,0,app.Settings.SectionGap)};
        body.Children.Add(header);
        body.Children.Add(compactPageHost);
        bodyReflow.Add(()=>header.Margin=new(0,0,0,app.Settings.SectionGap));
        foreach(string page in PanelWidgets.Select(w=>PageFor(w.Id)).Distinct())
        {
            FrameworkElement view;if(page==activePage)view=BuildCompactPage(page);else{view=new Border();pendingCompactPages.Add(page);}view.Visibility=Visibility.Collapsed;compactPages[page]=view;compactPageHost.Children.Add(view);
            var tab=UI.Button(PageName(page),()=>SelectPage(page));tab.Padding=new(8,6,8,6);tab.FontSize=10.5;tab.MinHeight=30;tab.Margin=new(0,0,4,0);tab.Tag=new CornerRadius(15);StyleCategory(tab,page);compactTabs[page]=tab;tabRow.Children.Add(tab);
        }
        if(compactPages.Count==0){var empty=UI.Caption("Enable widgets in Settings.",12,UI.Muted);compactPages["media"]=empty;compactPageHost.Children.Add(empty);}
        var mixer=new Border{Visibility=Visibility.Collapsed};pendingCompactPages.Add("mixer");compactPages["mixer"]=mixer;compactPageHost.Children.Add(mixer);
        if(!compactPages.ContainsKey(activePage))activePage=compactPages.Keys.FirstOrDefault()??"media";
        SelectPage(activePage,false,false);
    }
    internal void SelectPage(string page,bool animate=true,bool remember=true)
    {
        LeaveAssistantPanel();
        mediaControls?.Close(false);EnsureBody();

        if(!app.Settings.CompactLayout || commandOpen || !compactPages.ContainsKey(page))return;
        if(remember)RememberPanel(WidgetForPage(page));
        if(animate && activePage==page && compactPages[page].Visibility==Visibility.Visible)return;
        FlushNotes();EnsureCompactPage(page);InvalidateSystemMonitor();
        if(activePage!=page){fittedExtent=null;scroll.ScrollToTop();}
        activePage=page;
        if(Settings.ToolNames.ContainsKey(page))
        {
            toolsMode=page;calendarEditing=false;
            if(toolsContent.Parent is Panel owner)owner.Children.Remove(toolsContent);
            ((Panel)compactPages[page]).Children.Add(toolsContent);RefreshTools();
        }
        foreach(var entry in compactPages){entry.Value.Visibility=entry.Key==page?Visibility.Visible:Visibility.Collapsed;}
        UpdateSystemMonitor();
        foreach(var entry in compactTabs)StyleCategory(entry.Value,entry.Key,entry.Key==(page=="mixer"?"media":page));
        if(compactTabs.TryGetValue(page,out var selectedTab))selectedTab.BringIntoView();
        if(animate)Motion.Enter(compactPages[page],4);
        Dimensions();
        if(expanded){motion.Go(surface,IslandSurface.SurfaceWidthProperty,expandedWidth,Math.Min(300,app.Settings.AnimationMs));motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));motion.OnComplete(()=>{});}
    }
    private FrameworkElement BuildCompactPage(string page)
    {
        if(Settings.ToolNames.ContainsKey(page))return new StackPanel();
        if(page=="tools")return BuildToolsPage();
        if(page=="media")
        {
            mediaLyricsLayout=null;lyricsAside=null;mediaLyrics=null;lyricsButton=null;
            var panel=new StackPanel();
            if(WidgetOn("media"))
            {
                var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;artist.TextWrapping=TextWrapping.NoWrap;artist.TextTrimming=TextTrimming.CharacterEllipsis;artist.Margin=new(0,4,0,0);info.Children.Add(WrapTitle(title));info.Children.Add(WrapTitle(artist));
                var cover=new Border{Child=WrapArtwork(artwork),Width=88,Height=88,Margin=new(0,0,20,0),Clip=new RectangleGeometry(new Rect(0,0,88,88),15,15)};
                var row=new Grid{Margin=new(0,0,0,6)};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.Children.Add(cover);
                previous=Icons.Button("previous",Localization.Text("Previous track"),()=>_=app.Media.Command("previous"),false,38);play=Icons.Button("play",Localization.Text("Play or pause"),()=>_=app.Media.Command("toggle"),true,46);playIcon="";next=Icons.Button("next",Localization.Text("Next track"),()=>_=app.Media.Command("next"),false,38);previous.Background=next.Background=Brushes.Transparent;
                previous.Margin=new(0,0,8,0);play.Margin=new(0);next.Margin=new(8,0,0,0);
                lyricsButton=Icons.Button("lyrics",Localization.Text("Show or hide synced lyrics"),ToggleLyrics,false,28);lyricsButton.Margin=new(16,0,0,0);lyricsButton.Background=Brushes.Transparent;
                var buttons=GlassDockAppearance.ActionRow(previous,play,next);buttons.HorizontalAlignment=HorizontalAlignment.Center;var playbackRow=new Grid{Margin=new(0)};playbackRow.Children.Add(buttons);lyricsButton.HorizontalAlignment=HorizontalAlignment.Right;lyricsButton.VerticalAlignment=VerticalAlignment.Center;lyricsButton.Margin=new(0);playbackRow.Children.Add(lyricsButton);
                Grid.SetColumn(info,1);row.Children.Add(info);panel.Children.Add(row);panel.Children.Add(playbackRow);row.Loaded+=(_,_)=>QueueMediaLayout();row.IsVisibleChanged+=(_,_)=>{if(row.IsVisible)QueueMediaLayout();};
                row.SizeChanged+=(_,_)=>{bool narrow=row.ActualWidth<310;double size=narrow?64:88;cover.Width=cover.Height=artwork.Width=artwork.Height=size;cover.Margin=new(0,0,narrow?14:20,0);if(previousArtwork.TryGetValue(artwork,out var outgoing))outgoing.Width=outgoing.Height=size;musicWave.Width=narrow?112:164;cover.Clip=new RectangleGeometry(new Rect(0,0,size,size),narrow?11:15,narrow?11:15);previous.Width=previous.Height=next.Width=next.Height=narrow?28:38;play.Width=play.Height=narrow?38:46;title.FontSize=narrow?15:18;previous.Margin=new(0,0,narrow?2:8,0);next.Margin=new(narrow?2:8,0,0,0);};
                if(app.Settings.LiveActivity){if(musicWave.Parent is Panel oldWave)oldWave.Children.Remove(musicWave);musicWave.Height=22;musicWave.Margin=new(0,0,0,8);var waveHost=new Grid{Margin=new(0,3,0,0)};waveHost.Children.Add(musicWave);panel.Children.Add(waveHost);}mediaLyrics=new LyricsView(app);SyncLyrics();timelinePanel.Margin=new(0,0,0,10);panel.Children.Add(timelinePanel);
            }
            var controls=BuildMediaControls(panel);
            return WidgetOn("media")?WrapMediaLyrics(controls):controls;
        }
        if(page=="voice")return new AssistantVoiceView(app);
        if(page=="stopwatch")return new StopwatchView(app,centered:true);
        if(page is "timer" or "focus")
            return new CountdownView(app,page=="focus");
        if(page=="status")return BuildSystemMonitor();
        if(page=="clock")
        {
            var clockPage=new StackPanel();
            if(clock.Parent is Panel clockOwner)clockOwner.Children.Remove(clock);
            if(date.Parent is Panel dateOwner)dateOwner.Children.Remove(date);
            clock.TextAlignment=date.TextAlignment=TextAlignment.Center;
            clockPage.Children.Add(clock);
            date.Margin=new(0,3,0,12);
            clockPage.Children.Add(date);
            var now=DateTime.Now;
            clock.Text=ExpandedClockText(now);
            date.Text=DateLabel(now);
            date.Visibility=app.Settings.ShowDate?Visibility.Visible:Visibility.Collapsed;
            return clockPage;
        }
        if(page=="battery")return BuildBatteryOverview();
        if(page=="volume")return BuildVolumePage();
        var launch=new StackPanel{Margin=new(0,2,0,8)};var search=UI.CaptionButton("Search commands  ·  Ctrl+K",()=>ShowCommands());search.Padding=new(12,8,12,8);search.Margin=new(0,0,0,16);search.HorizontalContentAlignment=HorizontalAlignment.Stretch;var searchRow=new DockPanel();var keys=UI.Caption("Ctrl+K",10);keys.ClearValue(TextBlock.ForegroundProperty);keys.Opacity=.72;DockPanel.SetDock(keys,Dock.Right);searchRow.Children.Add(keys);var searchIcon=Icons.Create("search",16,UI.Muted);searchIcon.Margin=new(0,0,12,0);DockPanel.SetDock(searchIcon,Dock.Left);searchRow.Children.Add(searchIcon);var searchLabel=UI.Caption("Search commands",12);searchLabel.ClearValue(TextBlock.ForegroundProperty);searchRow.Children.Add(searchLabel);search.Content=searchRow;launch.Children.Add(search);
        var shortcuts=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};KeyboardNavigation.SetDirectionalNavigation(shortcuts,KeyboardNavigationMode.Cycle);launch.Children.Add(shortcuts);foreach(var shortcut in app.Settings.Shortcuts)
        {
            shortcuts.Children.Add(LaunchButton(shortcut));
        }
        if(app.Settings.Shortcuts.Count==0)shortcuts.Children.Add(UI.Caption("Add shortcuts in Settings.",12,UI.Muted));return launch;
    }
    private void UpdateTimeline()
    {
        if(!app.Settings.CompactLayout || !WidgetOn("media"))return;
        var position=app.Media.Timeline();elapsed.Text=TimeSpan.FromSeconds(position.elapsed).ToString(position.duration>=3600?@"h\:mm\:ss":@"m\:ss");duration.Text=TimeSpan.FromSeconds(position.duration).ToString(position.duration>=3600?@"h\:mm\:ss":@"m\:ss");
        timelineAt=DateTimeOffset.UtcNow;timelineElapsed=position.elapsed;timelineDuration=position.duration;
        ApplyTimelineState(app.Media.Available?MediaTimelinePolicy.State(position.duration,app.Media.CanSeek):MediaTimelineState.None,app.Media.Available);
        if(!seekSlider.IsMouseCaptureWithin && !seekSlider.IsKeyboardFocusWithin && !seekDelay.IsEnabled)seekSlider.Value=position.duration>0?position.elapsed/position.duration:0;
    }
    private bool mediaLayoutPending;
    private void QueueMediaLayout()
    {
        if(mediaLayoutPending)return;mediaLayoutPending=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>{mediaLayoutPending=false;if(expanded && app.Settings.CompactLayout && activePage=="media" && !assistantOpen && !commandOpen)ResizeToolPanel();});
    }
}
