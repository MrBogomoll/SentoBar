using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SentoBar;

// A new alert grows out of the closed dock as a compact banner (app, title, one line of text),
// then folds back on its own. Hover or keyboard focus keeps it available.
public sealed partial class OverlayWindow
{
    private readonly DispatcherTimer bannerTimer=new(){Interval=TimeSpan.FromSeconds(6)};
    private bool bannerShowing,bannerTimerReady;
    private Border? bannerProgress;
    internal bool NoticeBannerOpen=>bannerShowing && noticeShowing;
    internal static double BannerSeconds(Settings s)=>Math.Clamp(s.NotificationSeconds,4,12);
    private bool BannerBlocked=>userHidden || expanded || peeking || fileDragActive || dockEditing || PeekInteractionActive || FeedbackVisible || assistantOpen || commandOpen;
    private void QueueNoticeBanner()
    {
        if(DiagnosticRun && !DiagnosticBanners)return;
        if(app.Notifications.Current==null || app.Notifications.CurrentDisplayed || BannerBlocked)return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background,ShowNoticeBanner);
    }
    internal bool DiagnosticBanners { get; set; }
    internal void ShowNoticeBanner()
    {
        var notice=app.Notifications.Current;
        if(notice==null || app.Notifications.CurrentDisplayed || BannerBlocked)return;
        if(!bannerTimerReady){bannerTimerReady=true;bannerTimer.Tick+=(_,_)=>BannerTimeout();}
        if(noticeCard!=null)visuals.Children.Remove(noticeCard);
        app.Notifications.MarkDisplayed();
        StopHoverTimers();SetTucked(false);peeking=true;noticeShowing=true;bannerShowing=true;peekPanel.Visibility=Visibility.Collapsed;SyncAssistantVisibility();RefreshNoticeEdge();
        bool hideText=notice.Kind=="external" && !app.Settings.ExternalNotificationText;
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var glyph=NoticeAvatar.Create(notice,40);glyph.Margin=new(0,0,12,0);glyph.VerticalAlignment=VerticalAlignment.Center;
        grid.Children.Add(glyph);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,1);
        var source=UI.Text(NotificationCenterText.Kind(notice)+"  ·  "+Localization.Text("now"),11,UI.Muted);source.TextWrapping=TextWrapping.NoWrap;source.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(source);
        var title=UI.Text(notice.Title,14);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new(0,2,0,0);text.Children.Add(title);
        string line=hideText?Localization.Text("New notification"):notice.Detail;
        if(line.Length>0){var body=UI.Text(line,12,UI.Muted);body.TextWrapping=TextWrapping.NoWrap;body.TextTrimming=TextTrimming.CharacterEllipsis;body.Margin=new(0,1,0,0);text.Children.Add(body);}
        grid.Children.Add(text);
        var arrow=Icons.Create("chevron-right",12,UI.Muted);arrow.Margin=new(6,0,2,0);arrow.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(arrow,2);grid.Children.Add(arrow);
        var bannerRow=new Grid();bannerRow.ColumnDefinitions.Add(new());bannerRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var more=Icons.Button("bell","View all notifications",()=>{CloseBanner(false);OpenNotificationCenter();},false,26);more.Background=Brushes.Transparent;more.Margin=new(6,0,0,0);more.Opacity=.75;more.VerticalAlignment=VerticalAlignment.Center;
        bannerRow.Children.Add(NoticeActionButton(notice,grid));Grid.SetColumn(more,1);bannerRow.Children.Add(more);
        bannerProgress=new Border{Height=2,CornerRadius=new(1),Background=UI.Brush(app.Settings.Accent),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(52,10,0,0),Opacity=.85,RenderTransformOrigin=new(0,.5),RenderTransform=new ScaleTransform(1,1)};
        var content=new StackPanel();content.Children.Add(bannerRow);content.Children.Add(bannerProgress);
        var hit=new Border{Child=content,Background=Brushes.Transparent,Padding=new(2,0,2,0)};
        hit.MouseEnter+=(_,_)=>PauseBanner(true);hit.MouseLeave+=(_,_)=>PauseBanner(hit.IsKeyboardFocusWithin);
        hit.IsKeyboardFocusWithinChanged+=(_,_)=>PauseBanner(hit.IsMouseOver || hit.IsKeyboardFocusWithin);
        // Banners use the hover width from Position & size.
        double width=Math.Max(260,PeekSurfaceWidth);
        double inset=surface.Notch?30:18;
        noticeCard=new(){Child=hit,Width=width-inset*2,Margin=new(0,surface.Notch?10:12,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
        visuals.Children.Add(noticeCard);compact.IsHitTestVisible=false;compact.Opacity=0;Show();Dimensions();
        hit.Measure(new Size(noticeCard.Width,double.PositiveInfinity));
        bannerProgress.Width=Math.Max(0,noticeCard.Width-52);
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,width,app.Settings.AnimationMs);
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,hit.DesiredSize.Height+(surface.Notch?24:26),app.Settings.AnimationMs);
        ControlFeedback.Notice(noticeCard,app.Settings.NotificationAnimation);
        bannerTimer.Interval=TimeSpan.FromSeconds(BannerSeconds(app.Settings));StartBannerCountdown();
    }
    private void StartBannerCountdown()
    {
        bannerTimer.Stop();bannerTimer.Start();
        if(bannerProgress?.RenderTransform is ScaleTransform scale)
        {
            if(Motion.Enabled)scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,0,bannerTimer.Interval));
            else{scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.ScaleX=1;}
        }
    }
    private void PauseBanner(bool hovering)
    {
        if(!NoticeBannerOpen)return;
        if(hovering){bannerTimer.Stop();if(bannerProgress?.RenderTransform is ScaleTransform scale){scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.ScaleX=1;}}
        else StartBannerCountdown();
    }
    private void BannerTimeout()
    {
        bannerTimer.Stop();if(!NoticeBannerOpen)return;
        if(noticeCard?.IsKeyboardFocusWithin==true){PauseBanner(true);return;}
        if(PointerInside()){StartBannerCountdown();return;}
        CloseBanner(true);
    }
    // The alert stays in the notification center; the queue moves on to the next banner.
    private void CloseBanner(bool showNext)
    {
        bannerTimer.Stop();
        if(NoticeBannerOpen){Collapse();app.Notifications.Dismiss();}
        if(showNext)Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{if(!noticeShowing)QueueNoticeBanner();}));
    }
}
