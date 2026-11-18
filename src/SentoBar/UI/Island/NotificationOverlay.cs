using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SentoBar;
public sealed partial class OverlayWindow
{
    private Border? noticeCard;
    private bool noticeShowing,historyNoticeShowing;
    private Border? noticeEdge;
    private TextBlock? noticeQueue;
    private Button? noticeNext;
    private void RefreshNoticeEdge()
    {
        if(noticeEdge==null){noticeEdge=new(){Width=18,Height=2,CornerRadius=new(1),Background=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};visuals.Children.Add(noticeEdge);}
        // A short accent line under the closed dock means unread alerts are waiting in the center.
        noticeEdge.Width=20;noticeEdge.Height=3;noticeEdge.CornerRadius=new(1.5);noticeEdge.Background=UI.Brush(app.Settings.Accent);
        noticeEdge.Margin=new(0,app.Settings.Height-4,0,0);
        bool waiting=app.Notifications.Current!=null || app.Notifications.Unread>0;
        noticeEdge.Visibility=waiting && !expanded && !peeking?Visibility.Visible:Visibility.Collapsed;
    }
    internal bool ManuallyHidden=>userHidden;
    internal bool NotificationBusy=>expanded || peeking || FeedbackVisible || fileDragActive || PeekInteractionActive || PointerInside();
    internal void RefreshNotice()
    {
        if(!IsLoaded)return;
        RefreshNoticeEdge();
        if(noticeQueue!=null)noticeQueue.Text=app.Notifications.Count>1?app.Notifications.Count+Localization.Text(" queued"):"";
        if(noticeNext!=null)noticeNext.IsEnabled=app.Notifications.Count>1;
        UpdateBellBadges();
        if(centerShowing){RefreshNotificationCenter();app.PublishUpdate(RuntimeChange.Clock);return;}
        if(app.Notifications.Current==null && noticeShowing && !historyNoticeShowing)Collapse();
        QueueNoticeBanner();
        // Never replace controls under a pointer, keyboard editor, or active drag.
        app.PublishUpdate(RuntimeChange.Clock);
    }
    private void HideNotice(){noticeShowing=false;historyNoticeShowing=false;centerShowing=false;bannerShowing=false;bannerTimer.Stop();if(noticeCard!=null)noticeCard.Visibility=Visibility.Collapsed;}
    private static string? NoticeDestinationCaption(string kind)=>kind switch
    {
        "timer"=>"Open timer",
        "focus"=>"Open focus",
        "updates"=>"Open updates",
        _=>null
    };
    private Button NoticeActionButton(InternalNotice notice,FrameworkElement content,bool fromHistory=false)
    {
        string caption=NoticeDestinationCaption(notice.Kind) ?? (notice.Kind=="downloads"?"Download details":"View notification");
        var button=UI.CaptionButton(caption,()=>ActivateNotice(notice,fromHistory));
        button.Content=content;button.Background=Brushes.Transparent;button.Margin=new(0);button.Padding=new(0);button.MinHeight=0;
        button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Tag=new CornerRadius(10);
        AutomationProperties.SetName(button,Localization.Text(caption)+" — "+notice.Title);
        button.ToolTip=Localization.Text(caption);
        return button;
    }
    private void ActivateNotice(InternalNotice notice,bool fromHistory)
    {
        if(NoticeDestinationCaption(notice.Kind)!=null){OpenNoticeDestination(notice,fromHistory);return;}
        Collapse();
        if(fromHistory)ShowNoticeDetails(notice,true,true);
        else OpenNotice(true);
    }
    private void OpenNoticeDestination(InternalNotice notice,bool fromHistory)
    {
        Collapse();
        if(!fromHistory)app.Notifications.Dismiss();
        if(notice.Kind=="updates")app.ShowSettings("Application");
        else OpenHoverPanel(notice.Kind,openClassicWidget:true);
    }
    internal void OpenNotice(bool requested=false)
    {
        // An open notification center already lists new alerts; never replace it with a popup.
        if((centerShowing || bannerShowing) && !requested)return;
        if(centerShowing)Collapse();
        var notice=app.Notifications.Current;
        if(requested && expanded)Collapse();
        if(notice==null || userHidden || expanded || fileDragActive || PeekInteractionActive || FeedbackVisible)return;
        if(peeking && !noticeShowing)return;
        ShowNoticeDetails(notice,false,requested);
    }
    private void ShowNoticeDetails(InternalNotice notice,bool fromHistory,bool focus)
    {
        if(noticeCard!=null)visuals.Children.Remove(noticeCard);
        if(!fromHistory)app.Notifications.MarkDisplayed();
        StopHoverTimers();SetTucked(false);peeking=true;noticeShowing=true;historyNoticeShowing=fromHistory;RefreshNoticeEdge();peekPanel.Visibility=Visibility.Collapsed;SyncAssistantVisibility();
        var content=new StackPanel();
        var header=new DockPanel{Margin=new(0,0,0,12)};
        noticeQueue=null;noticeNext=null;
        if(!fromHistory)
        {
            noticeQueue=UI.Text(app.Notifications.Count>1?app.Notifications.Count+Localization.Text(" queued"):"",10,UI.Muted);
            DockPanel.SetDock(noticeQueue,Dock.Right);header.Children.Add(noticeQueue);
        }
        var kind=UI.Text(notice.Kind=="external"?(notice.Source??Localization.Text("App")).ToUpperInvariant():notice.Kind switch{"timer"=>Localization.Text("TIMER"),"focus"=>Localization.Text("FOCUS"),"downloads"=>Localization.Text("DOWNLOADS"),"updates"=>Localization.Text("UPDATE"),_=>"SENTOBAR"},10,UI.Muted);
        var icon=NoticeAvatar.Create(notice,30);icon.Margin=new(0,0,10,0);icon.VerticalAlignment=VerticalAlignment.Center;
        header.Children.Add(icon);header.Children.Add(kind);content.Children.Add(header);
        var noticeTitle=UI.Heading(notice.Title,18);noticeTitle.FontWeight=FontWeights.SemiBold;noticeTitle.Margin=new(0,0,0,6);content.Children.Add(noticeTitle);
        // Message text from other apps can be private; the setting keeps it off the screen edge.
        bool hideText=!fromHistory && notice.Kind=="external" && !app.Settings.ExternalNotificationText;
        var detail=UI.Text(hideText?Localization.Text("Open the notification center to read it."):notice.Detail,12,UI.Muted);detail.LineHeight=18;content.Children.Add(detail);
        var accent=new LinearGradientBrush(UI.Brush("#C4C9D2").Color,UI.Brush("#30343C").Color,0);accent.Freeze();content.Children.Add(new Border{Height=2,CornerRadius=new(1),Background=accent,Margin=new(0,15,0,4)});
        string[] paths=(notice.Files??Array.Empty<string>()).Where(File.Exists).Take(40).ToArray();
        if(paths.Length>0)
        {
            var row=new DockPanel{Margin=new(0,10,0,8)};var image=new Image{Width=32,Height=32,Margin=new(0,0,10,0)};row.Children.Add(image);_=LoadShelfThumbnail(paths[0],image);
            var drag=UI.Button(Path.GetFileName(paths[0])+(paths.Length>1?$" +{paths.Length-1}":""),()=>{});drag.Focusable=false;drag.Content=new TextBlock{Text=Path.GetFileName(paths[0])+(paths.Length>1?$" +{paths.Length-1}":""),TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};drag.ToolTip=Localization.Text("Drag original files · use Show in folder or Add to shelf with keyboard");row.Children.Add(drag);content.Children.Add(row);
            Point? down=null;drag.PreviewMouseLeftButtonDown+=(_,e)=>down=e.GetPosition(drag);
            drag.PreviewMouseMove+=(_,e)=>
            {
                if(down==null || e.LeftButton!=MouseButtonState.Pressed)return;var delta=e.GetPosition(drag)-down.Value;
                if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                down=null;e.Handled=true;fileDragActive=true;peekSuppressClick=true;
                try{DragDrop.DoDragDrop(drag,new DataObject(DataFormats.FileDrop,paths.Where(File.Exists).ToArray()),DragDropEffects.Copy|DragDropEffects.Link);}
                finally{fileDragActive=false;down=null;}
            };
            drag.PreviewMouseLeftButtonUp+=(_,e)=>{down=null;e.Handled=true;};
            var fileActions=new WrapPanel();fileActions.Children.Add(UI.CaptionButton("Show in folder",()=>
            {
                try{if(!File.Exists(paths[0]))return;var info=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add("/select,");info.ArgumentList.Add(paths[0]);System.Diagnostics.Process.Start(info);}
                catch(Exception ex){app.Store.Log(ex);app.Notify(Localization.Text("Could not show file"),ex.Message);}
            }));
            fileActions.Children.Add(UI.CaptionButton("Add to shelf",()=>{try{app.Productivity.AddFiles(paths.Where(File.Exists));}catch(Exception ex){app.Store.Log(ex);app.Notify(Localization.Text("Could not stage files"),ex.Message);}}));
            content.Children.Add(fileActions);
        }
        else if(notice.Kind=="downloads")content.Children.Add(UI.Text(Localization.Text("These files are no longer available."),11,UI.Muted));
        if(NoticeDestinationCaption(notice.Kind) is {} destination)content.Children.Add(UI.CaptionButton(destination,()=>OpenNoticeDestination(notice,fromHistory)));
        if(notice.Kind=="external" && !string.IsNullOrWhiteSpace(notice.AppId))content.Children.Add(UI.CaptionButton("Open app",()=>
        {
            Collapse();if(!fromHistory)app.Notifications.Dismiss();
            ExternalAppLauncher.Open(notice.AppId);
        }));
        var actions=new WrapPanel{Margin=new(0,10,0,0)};
        if(fromHistory)
        {
            actions.Children.Add(UI.CaptionButton("Back to notifications",()=>{Collapse();OpenNotificationCenter();}));
            actions.Children.Add(UI.CaptionButton("Close",Collapse));
        }
        else
        {
            actions.Children.Add(UI.CaptionButton("Dismiss",()=>{Collapse();app.Notifications.Dismiss();}));
            noticeNext=UI.CaptionButton("Next",()=>{Collapse();app.Notifications.Dismiss();OpenNotice(true);});noticeNext.IsEnabled=app.Notifications.Count>1;
            actions.Children.Add(noticeNext);
            actions.Children.Add(UI.CaptionButton("Clear all",()=>{Collapse();app.Notifications.Clear();}));
            actions.Children.Add(UI.CaptionButton("View all",()=>{Collapse();app.Notifications.Clear();OpenNotificationCenter();}));
        }
        foreach(Button action in actions.Children){action.FontSize=11;action.MinHeight=30;action.Padding=new(9,4,9,4);action.Margin=new(0,0,6,0);}content.Children.Add(actions);
        var screen=Native.ChooseScreen(app.Settings);double scale=Native.Scale(screen);
        double width=Math.Max(260,PeekSurfaceWidth);
        var viewport=new ScrollViewer{Content=content,Focusable=true,IsTabStop=false,MaxHeight=Math.Max(100,Math.Min(HostHeight-40,screen.WorkingArea.Height/scale-48)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        KeyboardNavigation.SetTabNavigation(viewport,KeyboardNavigationMode.Cycle);
        noticeCard=new(){Child=viewport,Width=width-(surface.Notch?64:32),Margin=new(0,16,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
        visuals.Children.Add(noticeCard);compact.IsHitTestVisible=false;compact.Opacity=0;Show();Dimensions();
        viewport.Measure(new Size(noticeCard.Width,double.PositiveInfinity));
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,width,app.Settings.AnimationMs);
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,viewport.DesiredSize.Height+32,app.Settings.AnimationMs);
        ControlFeedback.Notice(noticeCard,app.Settings.NotificationAnimation);
        if(focus && !DiagnosticRun){Activate();viewport.Focus();}
    }
    internal static FrameworkElement NoticeGlyph(InternalNotice notice,double size)=>NoticeAvatar.Create(notice,size);
}
