using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SentoBar;

public sealed partial class OverlayWindow
{
    private const double PeekContentHeight=124,PeekFooterHeight=28;
    private const double PeekArrowWidth=22,DetachSlotWidth=30,PeekSettingsWidth=28;
    private const double PeekFooterEndWidth=PeekArrowWidth+DetachSlotWidth+PeekSettingsWidth;
    private double PeekExpandedHeight
    {
        get
        {
            double height=peekDurationEditor==null?app.Settings.HoverHeight:Math.Max(app.Settings.HoverHeight,PeekWidth<300?300:225);
            var screen=IslandScreen;
            return Math.Min(height,screen.WorkingArea.Height/Native.Scale(screen)-16);
        }
    }
    private ScrollViewer? peekDurationEditor;
    private double PeekSurfaceWidth=>Math.Min(app.Settings.HoverWidth,IslandScreen.WorkingArea.Width/Native.Scale(IslandScreen)-24);
    private double WindowHostWidth {get{double scale=Native.Scale(IslandScreen);return Math.Ceiling(Math.Max(Math.Max(expandedWidth,app.Settings.Width),PeekSurfaceWidth)*scale)/scale;}}
    private double PeekWidth=>PeekSurfaceWidth-64;
    private Button peekDetachButton=null!;
    private readonly Grid peekMedia=new(),peekViewport=new(){ClipToBounds=true,Height=PeekContentHeight,Background=Brushes.Transparent};
    private readonly Dictionary<string,FrameworkElement> peekViews=new();
    private readonly Dictionary<string,TranslateTransform> peekMoves=new();
    private readonly Dictionary<string,Button> peekDots=new();
    private readonly TextBlock peekPageName=UI.Text(Localization.Text("Music"),10,UI.Muted);
    private readonly Dictionary<string,(TextBlock Time,TextBlock Heading,TextBlock Status,Button Toggle,Button Edit,Button Reset,ProgressBar Progress)> peekClocks=new();
    private (ProgressBar Bar,DateTimeOffset Deadline,double Duration,int FrameRate)? peekCountdownAnimation;
    private readonly TextBlock peekEventTitle=UI.Text("",16),peekEventTime=UI.Text("",11,UI.Muted),peekShelfName=UI.Text("",14),peekShelfCount=UI.Text("",11,UI.Muted);
    private TextBox peekNote=null!;
    private TextBox peekNoteTitle=null!;
    private Button? peekNoteSave;
    private Button peekPrevious=null!,peekNext=null!,peekJoin=null!,peekShelfFile=null!,peekMute=null!,peekTrackPrevious=null!,peekTrackNext=null!,peekOutput=null!;
    private Slider peekVolume=null!;
    private string peekId="media";
    private StackPanel peekTabStrip=null!;
    private ScrollViewer peekTabScroll=null!;
    private Point? peekDown;
    private bool peekDragging,peekTouching,peekTouchBlocked;
    private double peekOffset,peekDragOrigin;
    private readonly PeekWheelAccumulator peekWheel=new();
    private Grid peekFooter=null!;
    private bool peekSuppressClick;
    internal bool PeekDetailed=>peekDetailed;
    internal Rect PeekContentBounds=>new((ActualWidth-PeekWidth)/2,peekPanel.Margin.Top,PeekWidth,peekPanel.Height);
    private CalendarEvent? peekEvent;
    private string? peekFilePath;
    private string? peekShelfArtPath;
    private readonly Image peekShelfArt=new(){Width=44,Height=44,Stretch=Stretch.Uniform,Margin=new(0,0,12,0),Visibility=Visibility.Collapsed};
    private bool PeekInteractionActive=>OwnedPopupOpen || dockEditing || peekDragging || peekTouching || peekSeeking || peekDetachButton?.IsMouseCaptured==true || peekDurationEditor!=null || peekNote?.IsKeyboardFocusWithin==true || peekNoteTitle?.IsKeyboardFocusWithin==true;
    internal string PeekPage=>peekId;
    internal string[] HoverDeckIds=>PeekPages();
    internal bool PeekTabSafe=>peekViews.Where(pair=>pair.Key!=peekId).All(pair=>KeyboardNavigation.GetTabNavigation(pair.Value)==KeyboardNavigationMode.None);
    internal bool PeekMoving=>peekMoves.Values.Any(move=>move.HasAnimatedProperties);
    private string[]? hoverDeckCache;
    private void InvalidateHoverDeck(){hoverDeckCache=null;if(peekInitialized && peeking && !assistantPeeking){PreparePeekDeck();UpdatePeek();}}
    private string[] PeekPages()
    {
        var ids=hoverDeckCache??=HoverPagePolicy.Pages(app.Settings);

        if((fileDragActive && !shortcutDropActive || shelfDropBrowsing) && !ids.Contains("files"))ids=ids.Append("files").ToArray();
        return ids;
    }
    private static string PeekName(string id)=>id=="media"?Localization.Text("Music"):Localization.Text(Settings.Names.GetValueOrDefault(id)??id);
    private bool peekInitialized;
    private void EnsurePeekDeck(){if(peekInitialized)return;peekInitialized=true;InitializePeekDeck();}
    private void InitializePeekDeck()
    {
        app.SettingsChanged+=InvalidateHoverDeck;
        peekPanel.HorizontalAlignment=HorizontalAlignment.Center;peekPanel.VerticalAlignment=VerticalAlignment.Top;peekPanel.Width=PeekWidth;peekPanel.Height=PeekContentHeight+PeekFooterHeight;peekPanel.Margin=new(0,20,0,0);peekPanel.Visibility=Visibility.Collapsed;
        peekPanel.RowDefinitions.Add(new(){Height=new GridLength(PeekContentHeight)});peekPanel.RowDefinitions.Add(new(){Height=new GridLength(PeekFooterHeight)});peekPanel.Children.Add(peekViewport);
        peekMedia.Height=PeekContentHeight;peekMedia.Width=PeekWidth;peekMedia.Visibility=Visibility.Visible;peekMedia.RowDefinitions.Add(new(){Height=new GridLength(28)});
        var controls=new Grid();controls.ColumnDefinitions.Add(new(){Width=new GridLength(34)});controls.ColumnDefinitions.Add(new(){Width=new GridLength(34)});controls.ColumnDefinitions.Add(new(){Width=new GridLength(34)});controls.ColumnDefinitions.Add(new());controls.ColumnDefinitions.Add(new(){Width=GridLength.Auto});controls.ColumnDefinitions.Add(new(){Width=new GridLength(28)});
        controls.ColumnDefinitions.Add(new(){Width=new GridLength(28)});
        var previous=PeekIcon("previous",Localization.Text("Previous track in preview"),()=>_=app.Media.Command("previous"));var next=PeekIcon("next",Localization.Text("Next track in preview"),()=>_=app.Media.Command("next"));var mute=PeekIcon("volume",Localization.Text("Mute preview audio"),()=>{app.Audio.ToggleMute();app.Audio.Read();Update();});var output=PeekIcon("chevron",Localization.Text("Choose preview audio output"),OpenAudioMenu);
        peekTrackPrevious=previous;peekTrackNext=next;peekMute=mute;peekOutput=output;controls.Children.Add(previous);Grid.SetColumn(next,1);controls.Children.Add(next);Grid.SetColumn(mute,2);controls.Children.Add(mute);
        var favorite=new SpotifyLikeButton(app){Width=28,MinWidth=28,Height=26,MinHeight=26,Margin=new(0)};
        // Keep the current hover stage when liking a track.
        favorite.PreviewMouseDown+=(_,_)=>detailDelay.Stop();favorite.PreviewTouchDown+=(_,_)=>detailDelay.Stop();Grid.SetColumn(favorite,4);controls.Children.Add(favorite);
        peekVolume=new Slider{Minimum=0,Maximum=100,Height=26,Margin=new(7,0,9,0),VerticalAlignment=VerticalAlignment.Center};UI.Named(peekVolume,Localization.Text("Preview volume"));peekVolume.ValueChanged+=(_,_)=>{if(!updating){app.Audio.SetVolume(peekVolume.Value/100);}};Grid.SetColumn(peekVolume,3);controls.Children.Add(peekVolume);Grid.SetColumn(output,5);controls.Children.Add(output);var mixerButton=PeekIcon("mixer",Localization.Text("Open mixer from preview"),OpenMixer);Grid.SetColumn(mixerButton,6);controls.Children.Add(mixerButton);Grid.SetRow(controls,4);Grid.SetColumnSpan(controls,3);peekMedia.Children.Add(controls);AddPeekView("media",peekMedia);
        peekPageName.TextWrapping=TextWrapping.NoWrap;peekPageName.TextTrimming=TextTrimming.CharacterEllipsis;peekPageName.VerticalAlignment=VerticalAlignment.Center;
        var footer=peekFooter=new Grid{Margin=new(0,2,0,2)};
        footer.ColumnDefinitions.Add(new(){Width=new(PeekFooterEndWidth)});
        footer.ColumnDefinitions.Add(new());
        footer.ColumnDefinitions.Add(new(){Width=new(PeekArrowWidth)});
        footer.ColumnDefinitions.Add(new(){Width=new(DetachSlotWidth)});
        footer.ColumnDefinitions.Add(new(){Width=new(PeekSettingsWidth)});
        peekPageName.Margin=new(0,0,PeekArrowWidth+3,0);footer.Children.Add(peekPageName);
        peekTabStrip=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        peekTabScroll=new ScrollViewer{Content=peekTabStrip,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,PanningMode=PanningMode.HorizontalOnly};
        KeepTabsWithinViewport(peekTabScroll,peekTabStrip);
        Grid.SetColumn(peekTabScroll,1);
        footer.Children.Add(peekTabScroll);
        foreach(var id in HoverPagePolicy.Ids)
            peekDots[id]=UI.Button(PeekName(id),()=>SelectPeek(id));
        peekPrevious=PeekArrow(false,()=>NavigatePeek(-1));
        peekPrevious.HorizontalAlignment=HorizontalAlignment.Right;
        footer.Children.Add(peekPrevious);
        peekNext=PeekArrow(true,()=>NavigatePeek(1));
        Grid.SetColumn(peekNext,2);footer.Children.Add(peekNext);
        var detach=peekDetachButton=Icons.Button("pushpin","Detach preview",
            ()=>DetachPeekAt(new Point((ActualWidth-PeekWidth)/2+24,app.Settings.Height+24),false),size:24);
        if(detach.Content is FrameworkElement glyph)glyph.Width=glyph.Height=16;
        detach.Padding=new(2);
        detach.HorizontalContentAlignment=HorizontalAlignment.Center;
        detach.Margin=new(2,0,4,0);
        detach.VerticalAlignment=VerticalAlignment.Center;
        detach.Visibility=Visibility.Hidden;
        Point? dragStart=null;
        detach.PreviewMouseLeftButtonDown+=(_,e)=>dragStart=e.GetPosition(this);
        detach.PreviewMouseLeftButtonUp+=(_,_)=>dragStart=null;
        detach.LostMouseCapture+=(_,_)=>dragStart=null;
        detach.PreviewMouseMove+=(_,e)=>
        {
            if(dragStart is Point start && detach.IsMouseCaptured && e.LeftButton==MouseButtonState.Pressed)
                e.Handled=TryDetachPeekDrag(start,e.GetPosition(this));
        };
        Grid.SetColumn(detach,3);
        footer.Children.Add(detach);
        var settings=Icons.Button("settings","Settings",app.ShowSettings,false,24);
        settings.Margin=new(4,0,0,0);
        settings.VerticalAlignment=VerticalAlignment.Center;
        settings.Padding=new(3);
        settings.Background=Brushes.Transparent;
        settings.BorderThickness=new(0);
        Grid.SetColumn(settings,4);
        footer.Children.Add(settings);
        Grid.SetRow(footer,1);peekPanel.Children.Add(footer);visuals.Children.Add(peekPanel);
        peekPanel.Focusable=true;peekPanel.PreviewMouseLeftButtonDown+=(_,e)=>{if(peeking){Activate();if(!PeekGestureBlocked(e.OriginalSource as DependencyObject))peekPanel.Focus();}};
        peekViewport.PreviewTouchDown+=(_,e)=>peekTouchBlocked=PeekGestureBlocked(e.OriginalSource as DependencyObject);
        peekViewport.PreviewMouseLeftButtonDown+=(_,e)=>{if(PeekGestureBlocked(e.OriginalSource as DependencyObject))return;peekSuppressClick=false;peekDown=e.GetPosition(peekViewport);peekDragOrigin=peekMoves[peekId].X;};
        peekViewport.PreviewMouseMove+=(_,e)=>
        {
            if(!peekDown.HasValue || e.LeftButton!=MouseButtonState.Pressed)return;var delta=e.GetPosition(peekViewport)-peekDown.Value;
            if(!peekDragging){if(Math.Abs(delta.Y)>10 && Math.Abs(delta.Y)>Math.Abs(delta.X)){peekDown=null;return;}if(Math.Abs(delta.X)<8)return;peekDragging=true;peekSuppressClick=true;peekViewport.CaptureMouse();PromotePeek();}
            DragPeek(delta.X+peekDragOrigin);e.Handled=true;
        };
        peekViewport.PreviewMouseLeftButtonUp+=(_,e)=>{if(!peekDragging){peekDown=null;return;}double offset=peekOffset;peekDragging=false;peekDown=null;peekViewport.ReleaseMouseCapture();FinishPeekSwipe(offset);e.Handled=true;};
        peekViewport.LostMouseCapture+=(_,_)=>{if(peekDragging){peekDragging=false;peekDown=null;LayoutPeek(0,true);}};
        peekViewport.IsManipulationEnabled=true;
        peekViewport.ManipulationStarting+=(_,e)=>{if(peekTouchBlocked || PeekGestureBlocked(e.OriginalSource as DependencyObject)){e.Cancel();return;}peekTouching=true;peekSuppressClick=true;peekOffset=0;e.ManipulationContainer=peekViewport;e.Mode=ManipulationModes.TranslateX;PromotePeek();e.Handled=true;};
        peekViewport.ManipulationDelta+=(_,e)=>{if(peekTouching){DragPeek(e.CumulativeManipulation.Translation.X);e.Handled=true;}};
        peekViewport.ManipulationCompleted+=(_,e)=>{if(peekTouching){peekTouching=false;FinishPeekSwipe(peekOffset);e.Handled=true;}};
        peekViewport.MouseLeftButtonUp+=(_,e)=>{if(peekSuppressClick){peekSuppressClick=false;e.Handled=true;return;}if(!PeekGestureBlocked(e.OriginalSource as DependencyObject)){OpenPeekPage();e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{if(!peeking || assistantPeeking || Keyboard.FocusedElement is TextBox or Slider)return;if(e.Key is Key.Left or Key.Right){NavigatePeek(e.Key==Key.Right?1:-1);e.Handled=true;}};
    }
    private bool TryDetachPeekDrag(Point start,Point current)
        =>(current-start).LengthSquared>100 && DetachPeekAt(current,true);

    private bool DetachPeekAt(Point point,bool drag)
    {
        var kind=peekId;
        if(!peeking || app.Previews?.CanOpen(kind)!=true)return false;
        var anchor=PointToScreen(point);
        if(drag)peekDetachButton.ReleaseMouseCapture();
        Collapse();
        app.Previews.OpenAt(kind,anchor,drag);
        return true;
    }

    internal static bool PeekGestureBlocked(DependencyObject? source)
    {
        for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is ButtonBase or TextBoxBase or Slider or ComboBox or ScrollBar or Thumb or PeekSeekStrip)return true;return false;
    }
    private Button PlainPeekButton(string name,Action action)
    {var button=UI.Button(name,action);button.Template=(ControlTemplate)FindResource("ToolRowTemplate");button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.Padding=new(3);button.Margin=new(0);button.MinHeight=22;button.HorizontalContentAlignment=HorizontalAlignment.Center;button.MouseEnter+=(_,_)=>PeekButtonLight(button,.075);button.MouseLeave+=(_,_)=>PeekButtonLight(button,button.IsKeyboardFocusWithin?.075:0);button.GotKeyboardFocus+=(_,_)=>PeekButtonLight(button,.075);button.LostKeyboardFocus+=(_,_)=>PeekButtonLight(button,button.IsMouseOver?.075:0);return button;}
    private static void PeekButtonLight(Button button,double opacity){if(button.Template.FindName("RowLight",button) is Border light)Motion.Fade(light,opacity);}
    private Button PeekIcon(string icon,string name,Action action)
    {var button=PlainPeekButton(name,action);button.Content=Icons.Create(icon,14);button.Width=28;button.Height=28;button.ToolTip=name;return button;}
    private Button PeekArrow(bool right,Action action)
    {var button=PlainPeekButton(right?Localization.Text("Next preview"):Localization.Text("Previous preview"),action);button.Width=PeekArrowWidth;button.Height=22;button.VerticalAlignment=VerticalAlignment.Center;button.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(right?"M1,1 L5,5 1,9":"M5,1 L1,5 5,9"),Stroke=UI.Brush("#BFC2CA"),StrokeThickness=1.3,Width=5,Height=9};button.ToolTip=right?Localization.Text("Next · drag left"):Localization.Text("Previous · drag right");return button;}
    private void AddPeekView(string id,FrameworkElement view)
    {view.Width=PeekWidth;view.Height=PeekContentHeight;view.Visibility=Visibility.Collapsed;var move=new TranslateTransform();view.RenderTransform=move;peekMoves[id]=move;peekViews[id]=view;peekViewport.Children.Add(view);}
    private void EnsurePeekView(string id)
    {
        if(peekViews.ContainsKey(id))return;
        if(id is "timer" or "focus")AddPeekClock(id);
        else
        {
            var view=id switch
            {
                "stopwatch"=>(FrameworkElement)new StopwatchView(app),"calendar"=>BuildPeekCalendar(),"notes"=>BuildPeekNote(),"files"=>BuildPeekShelf(),"system"=>BuildPeekSystem(),"clips" or "tasks"=>BuildToolPeek(id),_=>BuildExtraPeek(id)
            };
            AddPeekView(id,view);
        }
        // A newly visited page must receive the current safe bounds immediately,
        // including compact first-stage height, before its first layout/transition.
        ApplyPeekGeometry();
    }
    private void PreparePeekDeck(bool restoreSelection=false)
    {
        EnsurePeekDeck();
        EndPeekGesture();var ids=PeekPages();if(!ids.Contains(peekId))ClearPeekClockEditor();if(ids.Length==0)return;
        peekPanel.Width=peekViewport.Width=PeekWidth;foreach(var view in peekViews.Values)view.Width=PeekWidth;ApplyPeekGeometry();hoverDelay.Interval=TimeSpan.FromMilliseconds(app.Settings.HoverDelayMs);detailDelay.Interval=TimeSpan.FromMilliseconds(app.Settings.HoverDetailDelayMs);
        if(automaticPreview && ids.Contains(previewKind))peekId=previewKind;
        else if(restoreSelection && selectedPanelWidget is {} widget && ids.Contains(widget))peekId=widget;
        else if(!ids.Contains(peekId))peekId=ids[0];
        peekTabStrip.Children.Clear();foreach(var id in ids)if(peekDots.TryGetValue(id,out var tab))peekTabStrip.Children.Add(tab);
        LayoutPeek(0,false);UpdatePeekDeck();
        outside?.Dispose();outside=DiagnosticRun?null:new OutsideClick(this,Collapse,p=>surface.Contour.FillContains(p) || audioMenu?.IsOpen==true);
    }
    private void PromotePeek()
    {if(!peeking || assistantPeeking)return;detailDelay.Stop();peekDetailed=true;automaticPreview=false;activityDismiss.Stop();double height=PeekExpandedHeight;if(Height<height)Height=height;ApplyPeekGeometry();UpdateToolPeeks();motion.Go(surface,IslandSurface.SurfaceHeightProperty,height,app.Settings.AnimationMs);}
    internal void SelectPeek(string id)
    {if(noticeShowing || !peeking || assistantPeeking || !PeekPages().Contains(id))return;RememberPanel(id);ClearPeekClockEditor();PromotePeek();var ids=PeekPages();int old=Array.IndexOf(ids,peekId),next=Array.IndexOf(ids,id);double start=(next-old)*PeekWidth;if(Math.Abs(next-old)>1){peekId=id;LayoutPeek(0,false);peekViews[id].Opacity=.65;Motion.Fade(peekViews[id],1,160);UpdatePeekDeck();return;}if(next==old){LayoutPeek(0,true);UpdatePeekDeck();return;}peekId=id;LayoutPeek(start,false);UpdatePeekDeck();LayoutPeek(0,true);WakeLiveRendering();}
    internal void NavigatePeek(int direction,bool wheel=false)
    {var ids=PeekPages();int index=Array.IndexOf(ids,peekId);if(ids.Length>0)SelectPeek(ids[WheelTabNavigation.Target(index,direction,ids.Length,wheel && app.Settings.WheelWrapTabs)]);}
    private void LayoutPeek(double offset,bool animate)
    {
        var ids=PeekPages();int index=Array.IndexOf(ids,peekId);
        EnsurePeekView(peekId);
        int neighbor=index+(offset>0?-1:1);if(Math.Abs(offset)>.01 && neighbor>=0 && neighbor<ids.Length)EnsurePeekView(ids[neighbor]);
        foreach(var pair in peekViews)
        {
            int i=Array.IndexOf(ids,pair.Key);double x=(i-index)*PeekWidth+offset;pair.Value.Visibility=i>=0 && (Math.Abs(x)<PeekWidth-.1 || (animate && Math.Abs(peekMoves[pair.Key].X)<PeekWidth-.1))?Visibility.Visible:Visibility.Collapsed;pair.Value.IsHitTestVisible=pair.Key==peekId;KeyboardNavigation.SetTabNavigation(pair.Value,pair.Key==peekId?KeyboardNavigationMode.Continue:KeyboardNavigationMode.None);if(pair.Key!=peekId && pair.Value.IsKeyboardFocusWithin)peekPanel.Focus();
            if(animate && pair.Value.Visibility==Visibility.Visible)Motion.To(peekMoves[pair.Key],TranslateTransform.XProperty,x,300,0,()=>{if(pair.Key!=peekId)pair.Value.Visibility=Visibility.Collapsed;});else{peekMoves[pair.Key].BeginAnimation(TranslateTransform.XProperty,null);peekMoves[pair.Key].X=x;}
        }
        UpdatePeekFooter();
        UpdatePeekTabs(ids);
        peekPageName.Text=PeekName(peekId);peekPrevious.IsEnabled=index>0;peekNext.IsEnabled=index<ids.Length-1;peekPrevious.Opacity=peekPrevious.IsEnabled?1:.25;peekNext.Opacity=peekNext.IsEnabled?1:.25;
    }
    private (string Style,string Widget,double Width,string Deck)? peekTabLayout;
    private void UpdatePeekTabs(string[] ids)
    {
        string mode=app.Settings.PanelNavigationStyle;
        double space=peekPanel.Width-2*PeekFooterEndWidth;
        var key=(mode,peekId,space,string.Join("|",ids));
        if(peekTabLayout==key)return;
        peekTabLayout=key;
        double width=double.IsFinite(space)?Math.Clamp(space/Math.Max(1,ids.Length),2,14):14;
        peekTabStrip.HorizontalAlignment=mode=="Labels"?HorizontalAlignment.Left:HorizontalAlignment.Center;
        foreach(string id in ids)
            StyleNavigationTab(peekDots[id],PeekName(id),id,id==peekId,mode,width,22);
        if(peekDots.TryGetValue(peekId,out var selected))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
            {
                if(peeking && peekTabLayout==key)selected.BringIntoView();
            }));
    }

    internal void DragPeek(double distance)
    {var ids=PeekPages();int index=Array.IndexOf(ids,peekId);peekOffset=Math.Clamp(distance,-PeekWidth,PeekWidth);if((index==0 && distance>0)||(index==ids.Length-1 && distance<0))peekOffset*=.22;LayoutPeek(Motion.Enabled?peekOffset:0,false);}
    internal void FinishPeekSwipe(double offset)
    {var ids=PeekPages();int index=Array.IndexOf(ids,peekId);int next=Math.Clamp(index+(offset < -app.Settings.SwipeDistance?1:offset>app.Settings.SwipeDistance?-1:0),0,Math.Max(0,ids.Length-1));if(ids.Length==0)return;peekId=ids[next];RememberPanel(peekId);LayoutPeek(offset+(next-index)*PeekWidth,false);UpdatePeekDeck();LayoutPeek(0,true);peekOffset=0;WakeLiveRendering();if(!DiagnosticRun && !PointerInside() && !PeekInteractionActive)leaveDelay.Start();}
    private void EndPeekGesture(){CancelPeekSeek();peekDown=null;peekDragging=peekTouching=false;peekOffset=0;peekWheel.Reset();if(peekViewport.IsMouseCaptured)peekViewport.ReleaseMouseCapture();}
    internal bool RoutePeekWheel(int delta,bool horizontal,DependencyObject? source,bool? scrollContent=null)
    {
        if(!peeking || noticeShowing || assistantPeeking)return false;
        if(!horizontal && (scrollContent??Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            return ScrollPanelContent(delta,source);
        int direction=peekWheel.Push(horizontal?delta:-delta,Environment.TickCount64);
        if(direction!=0)NavigatePeek(direction,true);
        return true;
    }
    private void UpdatePeekFooter()
    {
        bool canDetach=app.Previews?.CanOpen(peekId)==true;
        bool showPageName=app.Settings.PanelNavigationStyle=="Dots" && peekPanel.Width>=270 && peekId is not ("timer" or "focus");
        peekPageName.Visibility=showPageName?Visibility.Visible:Visibility.Collapsed;
        peekDetachButton.Visibility=canDetach?Visibility.Visible:Visibility.Hidden;
    }

    private void ApplyPeekGeometry()
    {
        if(assistantPeeking){LayoutAssistantPeek();return;}
        double height=peekDetailed?PeekExpandedHeight:100;
        var safe=PeekLayout.SafeBounds(PeekSurfaceWidth,height,surface.ContourRadius,surface.Notch,bottomInset:16);
        peekPanel.Width=peekViewport.Width=safe.Width;peekPanel.Height=safe.Height;
        peekPanel.Margin=new(0,safe.Top,0,0);
        double contentHeight=peekDetailed
            ?Math.Min(peekDurationEditor!=null || peekId=="tasks"?safe.Height:PeekContentHeight+PeekFooterHeight,safe.Height)-PeekFooterHeight
            :Math.Min(64,safe.Height);
        contentHeight=Math.Max(1,contentHeight);
        UpdatePeekFooter();
        peekViewport.Height=contentHeight;peekPanel.RowDefinitions[0].Height=new(contentHeight);
        UpdatePeekTabs(PeekPages());
        peekPanel.RowDefinitions[1].Height=new(peekDetailed?PeekFooterHeight:0);peekFooter.Visibility=peekDetailed?Visibility.Visible:Visibility.Collapsed;
        foreach(var pair in peekViews)
        {
            pair.Value.Width=safe.Width;
            pair.Value.Height=contentHeight;
            pair.Value.VerticalAlignment=VerticalAlignment.Top;
            if(peekClocks.TryGetValue(pair.Key,out var countdown))
                countdown.Status.Visibility=safe.Width>=320?Visibility.Visible:Visibility.Collapsed;
            if(pair.Value is Panel page)foreach(FrameworkElement child in page.Children)
            {
                if(pair.Key=="tasks" && child is Button)
                {
                    child.Visibility=peekDetailed?Visibility.Visible:Visibility.Collapsed;
                    continue;
                }
                bool primary=pair.Key=="media"?Grid.GetRow(child)==0:pair.Key is "timer" or "focus"?Grid.GetRow(child)<2:page.Children.IndexOf(child)<(pair.Key=="notes"?1:2);
                if(!primary){if(!peekDetailed)child.Visibility=Visibility.Hidden;else if(child.Visibility==Visibility.Hidden)child.Visibility=Visibility.Visible;}
            }
        }
        // Media metadata keeps a flexible title column even for very long tracks.
        peekTrackPrevious.Visibility=peekTrackNext.Visibility=Visibility.Visible;
        if(peekViews["media"] is Grid media && media.Children.OfType<Grid>().LastOrDefault() is Grid controls)
        {bool narrow=safe.Width<260;controls.ColumnDefinitions[0].Width=new(narrow?0:34);controls.ColumnDefinitions[1].Width=new(narrow?0:34);peekTrackPrevious.Visibility=peekTrackNext.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;}
    }
    internal void OpenPeekPage()
    {
        if(assistantPeeking){OpenAssistant();return;}
        if(!HoverPagePolicy.Available(app.Settings,peekId))return;
        if(HoverPagePolicy.IsTool(peekId)){ShowTools(peekId);return;}
        OpenHoverPanel(peekId);
    }
    private void AddPeekClock(string id)
    {
        bool timer=id=="timer";
        var page=new Grid();
        foreach(double height in new[]{17d,42d,10d,32d})page.RowDefinitions.Add(new(){Height=new GridLength(height)});
        var heading=UI.Caption(timer?"Timer":"Focus",11,GlassDockAppearance.Quiet);
        page.Children.Add(heading);
        var time=UI.Text("",32,GlassDockAppearance.Foreground);
        time.FontWeight=FontWeights.SemiBold;
        time.TextWrapping=TextWrapping.NoWrap;
        time.Margin=new(0,0,68,0);
        Typography.SetNumeralAlignment(time,FontNumeralAlignment.Tabular);
        Grid.SetRow(time,1);
        page.Children.Add(time);

        var toggle=GlassPeekButton(timer?"Start or pause preview timer":"Start or pause preview focus","",()=>TogglePeekClock(id));
        toggle.Width=toggle.Height=34;
        toggle.Tag=new CornerRadius(17);
        toggle.Background=Accessibility.HighContrast?SystemColors.HighlightBrush:GlassDockAppearance.Accent;
        toggle.BorderThickness=new(0);
        toggle.VerticalAlignment=VerticalAlignment.Center;
        var reset=PlainPeekButton(Localization.Text(timer?"Reset preview timer":"Reset preview focus"),()=>{PeekClock(id).Reset();app.SaveSession();});
        reset.Content=Icons.Create("reset",13,GlassDockAppearance.Quiet);
        reset.Width=23;
        reset.Height=28;
        reset.Padding=new(0);
        reset.ToolTip=Localization.Text(timer?"Reset timer":"Reset focus");
        var transport=GlassDockAppearance.ActionRow(reset,toggle);
        transport.HorizontalAlignment=HorizontalAlignment.Right;
        transport.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetRowSpan(transport,2);
        page.Children.Add(transport);

        var progress=new ProgressBar{Minimum=0,Maximum=1,Height=3,Background=GlassDockAppearance.Stroke,
            Foreground=GlassDockAppearance.Accent,BorderThickness=new(0)};
        Grid.SetRow(progress,2);
        page.Children.Add(progress);
        var status=UI.Text("",11,GlassDockAppearance.Quiet);
        status.TextWrapping=TextWrapping.NoWrap;
        status.TextTrimming=TextTrimming.CharacterEllipsis;
        var edit=GlassPeekButton(timer?"Edit timer":"Edit focus","Edit",()=>EditPeekClock(id));
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        void Step(string name,string text,int unit,int step)
        {
            var button=GlassPeekButton(name,text,()=>AdjustPeekTimerPart(unit,step));
            button.Margin=new(0,0,4,0);
            buttons.Children.Add(button);
        }
        if(timer)
        {
            Step("Add one minute to timer","+1 min",60,1);
            Step("Subtract one minute from timer","−1 min",60,-1);
            Step("Add one second to timer","+1 sec",1,1);
            Step("Subtract one second from timer","−1 sec",1,-1);
        }
        else
        {
            var more=GlassPeekButton("Add five minutes to preview focus","+5 min",ExtendPeekFocus);
            more.Margin=new(0,0,GlassDockAppearance.ActionGap,0);
            buttons.Children.Add(more);
        }
        buttons.Children.Add(edit);
        var actions=new DockPanel();
        DockPanel.SetDock(buttons,Dock.Right);
        actions.Children.Add(buttons);
        actions.Children.Add(status);
        Grid.SetRow(actions,3);
        page.Children.Add(actions);
        peekClocks[id]=(time,heading,status,toggle,edit,reset,progress);
        AddPeekView(id,page);
        progress.IsVisibleChanged+=(_,_)=>
        {
            if (progress.IsVisible && peeking && peekId==id)
            {
                var clock=PeekClock(id);
                UpdatePeekCountdownProgress(progress,clock,clock.Remaining(DateTimeOffset.UtcNow),PeekClockDuration(id));
            }
            else if (peekCountdownAnimation?.Bar==progress)
                StopPeekCountdownAnimation();
        };
    }

    private void UpdatePeekCountdownProgress(ProgressBar bar,Countdown clock,double remaining,int configured)
    {
        double duration=Math.Max(1,clock.DurationSeconds);
        if (bar.IsVisible && Motion.Enabled && remaining>0 && clock.Deadline is {} deadline)
        {
            var run=(bar,deadline,duration,Motion.FrameLimit);
            if (peekCountdownAnimation==run)return;
            StopPeekCountdownAnimation();

            // Use a single linear animation tied to the deadline, independent of the seconds display.
            remaining=clock.Remaining(DateTimeOffset.UtcNow);
            if (remaining>0)
            {
                bar.Value=0;
                var animation=new DoubleAnimation(Math.Clamp(remaining/duration,0,1),0,TimeSpan.FromSeconds(remaining))
                {
                    FillBehavior=FillBehavior.Stop
                };
                Timeline.SetDesiredFrameRate(animation,run.FrameLimit);
                bar.BeginAnimation(RangeBase.ValueProperty,animation);
                peekCountdownAnimation=run;
                return;
            }
        }

        if (peekCountdownAnimation?.Bar==bar)StopPeekCountdownAnimation();
        bar.Value=remaining>0?Math.Clamp(remaining/duration,0,1):clock.DurationSeconds==0 && configured>0?1:0;
    }

    private void StopPeekCountdownAnimation()
    {
        if (peekCountdownAnimation is not {} run)return;
        peekCountdownAnimation=null;
        run.Bar.BeginAnimation(RangeBase.ValueProperty,null);
    }

    private Button GlassPeekButton(string name,string label,Action action)
    {
        var button=UI.CaptionButton(label,action);
        UI.LocalizedName(button,name);
        button.Padding=new(5,2,5,2);
        button.Margin=new(0);
        button.FontSize=11;
        button.MinHeight=24;
        return button;
    }

    private void EditPeekClock(string id)
    {
        if(!peeking || peekId!=id || PeekClock(id).Remaining(DateTimeOffset.UtcNow)>0)return;
        ClearPeekClockEditor();
        bool focus=id=="focus";
        var form=new StackPanel();
        var fields=new WrapPanel{Margin=new(0,4,0,8)};
        DurationPicker Field(string name,int seconds)
        {
            var column=new StackPanel{Margin=new(0,0,14,0)};
            column.Children.Add(UI.Caption(name,11,UI.Muted));
            var picker=new DurationPicker(name switch{"Timer"=>"timer","Focus"=>"focus",_=>"break"},seconds,true){HorizontalAlignment=HorizontalAlignment.Left};
            column.Children.Add(picker);
            fields.Children.Add(column);
            return picker;
        }
        var duration=Field(focus?"Focus":"Timer",focus?app.Settings.FocusSeconds:app.Settings.TimerDurationSeconds);
        var rest=focus?Field("Break duration",app.Settings.BreakSeconds):null;
        var done=UI.CaptionButton("Save duration",()=>
        {
            duration.Commit();
            rest?.Commit();
            if(PeekClock(id).Remaining(DateTimeOffset.UtcNow)>0 || duration.Seconds<=0 || rest is {Seconds:<=0})return;
            if(focus){app.Settings.FocusSeconds=duration.Seconds;app.Settings.BreakSeconds=rest!.Seconds;}
            else app.Settings.TimerDurationSeconds=duration.Seconds;
            ClearPeekClockEditor();
            app.ChangeSettings();
            PromotePeek();
            UpdatePeekDeck();
        });
        GlassDockAppearance.Emphasize(done);
        done.FontWeight=FontWeights.SemiBold;
        void Validate()=>done.IsEnabled=duration.Seconds>0 && (rest==null || rest.Seconds>0);
        duration.Changed+=Validate;
        if(rest!=null)rest.Changed+=Validate;
        Validate();
        form.Children.Add(fields);
        form.Children.Add(UI.Row(done,UI.CaptionButton("Cancel",()=>{ClearPeekClockEditor();PromotePeek();})));
        peekDurationEditor=UI.Scroll(form);
        peekViewport.Visibility=Visibility.Hidden;
        peekPanel.Children.Add(peekDurationEditor);
        PromotePeek();
    }

    private void ClearPeekClockEditor()
    {
        if(peekDurationEditor==null)return;
        peekPanel.Children.Remove(peekDurationEditor);
        peekDurationEditor=null;
        peekViewport.Visibility=Visibility.Visible;
    }

    private Countdown PeekClock(string id)=>id=="timer"?app.Session.Timer:app.Session.Focus;
    private int PeekClockDuration(string id)=>id=="timer"?app.Settings.TimerDurationSeconds:app.Settings.FocusSeconds;
    private void TogglePeekClock(string id)
    {var clock=PeekClock(id);var now=DateTimeOffset.UtcNow;if(clock.Running)clock.Pause(now);else if(clock.Remaining(now)>0)clock.Resume(now);else{int seconds=id=="timer"?app.Settings.TimerDurationSeconds:app.Settings.FocusSeconds;if(seconds<=0 || id=="focus" && app.Settings.BreakSeconds<=0)return;clock.Start(seconds,now,id=="focus"?"work":"");}app.SaveSession();}
    private void AdjustPeekTimerPart(int unit,int step)
    {
        var timer=app.Session.Timer;
        var now=DateTimeOffset.UtcNow;
        if(timer.Running && timer.Remaining(now)==0)app.CheckCountdowns();
        double remaining=Math.Min(DurationParts.MaximumSeconds,timer.Remaining(now));
        if(remaining<=0)
        {
            app.Settings.TimerDurationSeconds=DurationParts.StepPart(app.Settings.TimerDurationSeconds,unit,step);
            if(timer.DurationSeconds>0){timer.Reset();app.SaveSession();}
            app.ChangeSettings();
            return;
        }

        int displayed=(int)Math.Ceiling(remaining);
        int next=DurationParts.StepPart(displayed,unit,step);
        double delta=next-displayed;
        double adjusted=next==0?0:remaining+delta;
        timer.DurationSeconds=Math.Clamp(timer.DurationSeconds+delta,Math.Max(1,adjusted),DurationParts.MaximumSeconds);
        timer.RemainingSeconds=adjusted;
        if(timer.Running)
        {
            timer.Deadline=now.AddSeconds(adjusted);
            if(adjusted==0){app.CheckCountdowns();return;}
        }
        app.SaveSession();
    }

    private void ExtendPeekFocus()
    {
        var clock=app.Session.Focus;
        var now=DateTimeOffset.UtcNow;
        double remaining=clock.Remaining(now);
        if(remaining<=0)clock.Start(300,now,"work");
        else
        {
            clock.DurationSeconds=Math.Min(DurationParts.MaximumSeconds,clock.DurationSeconds+300);
            clock.RemainingSeconds=Math.Min(DurationParts.MaximumSeconds,remaining+300);
            if(clock.Running)clock.Deadline=now.AddSeconds(clock.RemainingSeconds);
        }
        app.SaveSession();
    }
    private FrameworkElement BuildPeekSystem()
    {var page=PeekSummary("system");peekSystem.Margin=new(0,7,0,0);page.Children.Add(peekSystem);return page;}
    private FrameworkElement BuildPeekCalendar()
    {
        var page=new StackPanel();peekEventTime.Margin=new(0,3,0,9);page.Children.Add(peekEventTime);peekEventTitle.MaxHeight=43;peekEventTitle.TextWrapping=TextWrapping.NoWrap;peekEventTime.TextWrapping=TextWrapping.NoWrap;peekEventTime.TextTrimming=TextTrimming.CharacterEllipsis;peekShelfCount.TextWrapping=TextWrapping.NoWrap;peekShelfCount.TextTrimming=TextTrimming.CharacterEllipsis;peekEventTitle.TextTrimming=TextTrimming.CharacterEllipsis;page.Children.Add(peekEventTitle);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,10,0,0)};peekJoin=GlassPeekButton("Join preview meeting","Join meeting",()=>{if(peekEvent?.MeetingUrl.Length>0)try{ShortcutService.Open(new(){Target=peekEvent.MeetingUrl});}catch(Exception ex){app.Notify(Localization.Text("Could not open meeting"),ex.Message);}});peekJoin.Content=Localization.Text("Join meeting");peekJoin.FontSize=12;peekJoin.Margin=new(0,0,6,4);actions.Children.Add(peekJoin);var open=GlassPeekButton("Open local calendar","Calendar",()=>ShowTools("calendar"));open.FontSize=12;actions.Children.Add(open);page.Children.Add(actions);return page;
    }
    private FrameworkElement BuildPeekNote()
    {
        var page=new StackPanel();
        peekNoteTitle=UI.CaptionInput("Note title",app.Productivity.NoteTitle,"Leave blank to use the first phrase.");peekNoteTitle.MaxLength=ProductivityService.MaxNoteTitleLength;peekNoteTitle.Height=28;peekNoteTitle.Padding=new(9,3,9,3);peekNoteTitle.Margin=new(0,0,0,6);
        var titleHost=new Grid();titleHost.Children.Add(peekNoteTitle);var titleHint=UI.Caption("Note title (optional)",11,UI.Muted);titleHint.IsHitTestVisible=false;titleHint.VerticalAlignment=VerticalAlignment.Center;titleHint.Margin=new(10,0,10,0);titleHost.Children.Add(titleHint);page.Children.Add(titleHost);
        void TitleHint()=>titleHint.Visibility=peekNoteTitle.Text.Length==0?Visibility.Visible:Visibility.Collapsed;TitleHint();
        peekNoteTitle.TextChanged+=(_,_)=>{TitleHint();if(syncingNote)return;app.Productivity.NoteTitle=peekNoteTitle.Text;noteSave.Stop();noteSave.Start();if(peekNoteSave!=null)peekNoteSave.Content=Localization.Text("Save note");};peekNoteTitle.PreviewMouseLeftButtonDown+=(_,_)=>{Activate();PromotePeek();};
        peekNote=UI.Input(Localization.Text("Preview note draft"),app.Productivity.Note);peekNote.TextChanged+=(_,_)=>{if(syncingNote)return;app.Productivity.Note=peekNote.Text;noteSave.Stop();noteSave.Start();if(peekNoteSave!=null){peekNoteSave.Content=Localization.Text("Save note");peekNoteSave.IsEnabled=!string.IsNullOrWhiteSpace(peekNote.Text);}};peekNote.Height=53;peekNote.MaxLength=50000;peekNote.Padding=new(9,7,9,7);peekNote.Margin=new(0);peekNote.AcceptsReturn=true;peekNote.TextWrapping=TextWrapping.Wrap;peekNote.VerticalContentAlignment=VerticalAlignment.Top;peekNote.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;peekNote.PreviewMouseLeftButtonDown+=(_,_)=>{Activate();PromotePeek();};page.Children.Add(peekNote);
        var save=GlassPeekButton("Save preview note","Save note",SavePeekNote);save.FontSize=12;peekNoteSave=save;save.IsEnabled=!string.IsNullOrWhiteSpace(peekNote.Text);
        var open=GlassPeekButton("Open all notes","All notes",()=>{FlushNotes();app.ShowSettings("Notes");});open.FontSize=12;
        var actions=GlassDockAppearance.ActionRow(save,open);
        actions.HorizontalAlignment=HorizontalAlignment.Left;
        actions.Margin=new(0,9,0,0);
        page.Children.Add(actions);
        return page;
    }
    private void SavePeekNote()
    {
        if(string.IsNullOrWhiteSpace(peekNote.Text))return;
        noteSave.Stop();app.Productivity.Note=peekNote.Text;
        bool saved=app.Productivity.SaveDraftToLibrary();
        if(peekNoteSave!=null)
        {
            peekNoteSave.Content=Localization.Text(saved?"Saved":"Retry save");
            peekNoteSave.ToolTip=Localization.Text(saved?"Note saved to your library. The quick draft is unchanged.":"Could not save this note. Check the library capacity or try again.");
        }
        if(!saved)app.Notify(Localization.Text("Notes"),Localization.Text("Could not save this note. Check the library capacity or try again."));
    }

    private FrameworkElement BuildPeekShelf()
    {
        var page=new StackPanel();peekShelfCount.Margin=new(0,3,0,9);page.Children.Add(peekShelfCount);
        peekShelfFile=PlainPeekButton(Localization.Text("Open or drag preview shelf file"),()=>{if(peekFilePath!=null)try{ShortcutService.Open(new(){Target=peekFilePath});}catch(Exception ex){app.Notify(Localization.Text("Could not open file"),ex.Message);}});peekShelfName.TextTrimming=TextTrimming.CharacterEllipsis;peekShelfName.TextWrapping=TextWrapping.NoWrap;var fileRow=new DockPanel();fileRow.Children.Add(peekShelfArt);fileRow.Children.Add(peekShelfName);peekShelfFile.Content=fileRow;peekShelfFile.HorizontalContentAlignment=HorizontalAlignment.Left;peekShelfFile.Padding=new(0);peekShelfFile.Height=48;page.Children.Add(peekShelfFile);Point start=default;
        peekShelfFile.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(peekShelfFile);peekShelfFile.PreviewMouseMove+=(_,e)=>{var delta=e.GetPosition(peekShelfFile)-start;if(e.LeftButton==MouseButtonState.Pressed && peekFilePath!=null && (System.IO.File.Exists(peekFilePath) || System.IO.Directory.Exists(peekFilePath)) && (Math.Abs(delta.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y)>SystemParameters.MinimumVerticalDragDistance))DragDrop.DoDragDrop(peekShelfFile,new DataObject(DataFormats.FileDrop,new[]{peekFilePath}),DragDropEffects.Copy|DragDropEffects.Link);};
        var open=GlassPeekButton("Open full file shelf","Open shelf",()=>ShowTools("files"));open.FontSize=12;open.HorizontalAlignment=HorizontalAlignment.Left;open.Margin=new(0,8,0,0);page.Children.Add(open);return page;
    }
    private async System.Threading.Tasks.Task LoadPeekShelfArtwork(string path)
    {var target=new Image();await LoadShelfThumbnail(path,target);if(peekShelfArtPath==path)peekShelfArt.Source=target.Source;}
    private void UpdatePeekDeck()
    {
        UpdateAmbientMotion();
        ApplyPeekSeek();
        if(peekVolume!=null && !peekVolume.IsMouseCaptureWithin && !peekVolume.IsKeyboardFocusWithin){bool previous=updating;updating=true;try{peekVolume.Value=app.Audio.Volume*100;}finally{updating=previous;}}
        peekMute.IsEnabled=peekOutput.IsEnabled=peekVolume!.IsEnabled=app.Audio.Available;Motion.Icon(peekMute,app.Audio.Muted?"muted":"volume",14);peekTrackPrevious.IsEnabled=app.Media.CanPrevious;peekTrackNext.IsEnabled=app.Media.CanNext;peekTrackPrevious.Opacity=peekTrackPrevious.IsEnabled?1:.3;peekTrackNext.Opacity=peekTrackNext.IsEnabled?1:.3;
        UpdateExtraPeek();UpdateToolPeeks();var now=app.UpdateSampleTime;peekSystem.Text=Localization.Format("CPU  {0:0}%\nRAM  {1:0.0} / {2:0.0} GB",app.Usage.cpu,app.Usage.used,app.Usage.total);
        if(peekClocks.TryGetValue(peekId,out var fields))
        {
            bool timer=peekId=="timer";
            var clock=PeekClock(peekId);
            double remaining=clock.Remaining(now);
            int configured=PeekClockDuration(peekId);
            bool complete=remaining<=0 && clock.DurationSeconds>0;
            var heading=Localization.Text(timer?"Timer":clock.Phase=="break"?"Break":"Focus");
            fields.Time.Text=Countdown.FormatSeconds(remaining>0?remaining:timer && complete?0:configured);
            fields.Status.Text=Localization.Text(remaining>0?(clock.Running?"Running":"Paused"):complete?"Complete":"Ready");
            if(automaticPreview && previewKind==peekId && activityTitle.Length>0)
            {
                heading=activityTitle;
                if(remaining<=0)fields.Time.Text="00:00";
            }
            fields.Heading.Text=heading;
            fields.Edit.IsEnabled=remaining<=0;
            fields.Reset.Visibility=remaining>0 || complete?Visibility.Visible:Visibility.Collapsed;
            fields.Toggle.IsEnabled=remaining>0 || configured>0 && (timer || app.Settings.BreakSeconds>0);
            UpdatePeekCountdownProgress(fields.Progress,clock,remaining,configured);
            Motion.Icon(fields.Toggle,clock.Running?"pause":"play",14,Accessibility.HighContrast?SystemColors.HighlightTextBrush:GlassDockAppearance.AccentText);
        }
        else StopPeekCountdownAnimation();
        if(peekViews.ContainsKey("calendar") && peekId=="calendar"){peekEvent=app.Calendar.Events.Where(e=>e.End>now).OrderBy(e=>e.Start).FirstOrDefault();peekEventTitle.Text=peekEvent?.Title??Localization.Text("No upcoming events");peekEventTime.Text=peekEvent==null?DateTime.Now.ToString("ddd, d MMM",Localization.Culture):peekEvent.Start<=now?Localization.Text("Now · until ")+peekEvent.End.ToLocalTime().ToString("HH:mm"):peekEvent.Start.ToLocalTime().ToString("ddd, d MMM · HH:mm",Localization.Culture);peekJoin.Visibility=peekEvent?.MeetingUrl.Length>0?Visibility.Visible:Visibility.Collapsed;}
        if(peekViews.ContainsKey("files") && peekId=="files"){var file=app.Productivity.Shelf.LastOrDefault();peekFilePath=file?.Exists==true?file.Path:null;if(peekShelfArtPath!=peekFilePath){peekShelfArtPath=peekFilePath;peekShelfArt.Source=null;peekShelfArt.Visibility=peekFilePath==null?Visibility.Collapsed:Visibility.Visible;if(peekFilePath!=null)_=LoadPeekShelfArtwork(peekFilePath);}peekShelfName.Text=file?.Name??Localization.Text("Drop files onto this panel");peekShelfCount.Text=app.Productivity.Shelf.Count==0?Localization.Text("File shelf"):Localization.Format("{0} staged · drag a file out",app.Productivity.Shelf.Count);peekShelfFile.IsEnabled=peekFilePath!=null;}
    }
}
