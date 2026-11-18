using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NabyScape;

internal sealed class LyricsView : Grid
{
    internal const double StackedHeight=158;
    private const double ActiveLineSize=20;
    private const double AdjacentLineSize=ActiveLineSize*.9;
    private const double LineGap=6;
    private readonly App app;
    private readonly TextBlock earlier=UI.Text("",AdjacentLineSize),later=UI.Text("",AdjacentLineSize);
    private readonly TextBlock previous=UI.Text("",AdjacentLineSize),next=UI.Text("",AdjacentLineSize);
    private readonly TextBlock current=UI.Text("",ActiveLineSize),highlight=UI.Text("",ActiveLineSize),credit=UI.Text("",9,UI.Muted);
    private readonly Grid line=new(){ClipToBounds=true};
    private readonly ScrollViewer reader=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private readonly RectangleGeometry sweep=new();
    private readonly DispatcherTimer tick=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly Button retry;
    private int index=int.MinValue;
    private LyricResult? last;
    private bool attached;
    private bool contextLayoutPending;
    private ScrollViewer? viewport;
    private Window? host;
    private string style="";
    private (LyricResult Result,double At,double Duration)? fixture;
    internal void SetFixture(LyricResult result,double seconds,double duration){fixture=(result,seconds,duration);tick.Stop();Paint();}
    internal LyricsView(App app)
    {
        this.app=app;Height=StackedHeight;Margin=new(0,6,0,12);ClipToBounds=true;
        RowDefinitions.Add(new());RowDefinitions.Add(new(){Height=GridLength.Auto});
        foreach(var text in new[]{earlier,previous,next,later})
        {
            text.FontWeight=FontWeights.SemiBold;
            text.TextTrimming=TextTrimming.CharacterEllipsis;
            text.TextWrapping=TextWrapping.NoWrap;
            text.Opacity=Accessibility.HighContrast?1:.78;
            text.Visibility=Visibility.Collapsed;
        }
        earlier.Margin=previous.Margin=new(0,0,0,LineGap);
        next.Margin=later.Margin=new(0,LineGap,0,0);
        foreach(var text in new[]{current,highlight}){text.FontWeight=FontWeights.SemiBold;text.VerticalAlignment=VerticalAlignment.Center;text.TextWrapping=TextWrapping.Wrap;}
        highlight.IsHitTestVisible=false;highlight.Clip=sweep;line.Children.Add(current);line.Children.Add(highlight);
        var verse=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        verse.Children.Add(earlier);verse.Children.Add(previous);verse.Children.Add(line);verse.Children.Add(next);verse.Children.Add(later);
        var reading=new Grid();reading.Children.Add(verse);
        reader.Content=reading;Children.Add(reader);
        reader.SizeChanged+=(_,_)=>QueueContextLayout();
        foreach(var element in new FrameworkElement[]{previous,line,next})element.SizeChanged+=(_,_)=>QueueContextLayout();
        var footer=new StackPanel{Margin=new(0,8,0,0)};
        retry=UI.Button(Localization.Text("Retry lyrics"),()=>{app.Lyrics.Retry();Refresh();});
        var sources=new LyricsSourcesControl(app,()=>{app.Lyrics.Retry();Refresh();},()=>{credit.Text=LyricsFiles.Import(app,Window.GetWindow(this));});
        credit.FontSize=11;credit.TextWrapping=TextWrapping.Wrap;credit.Margin=new(0,7,0,0);footer.Children.Add(sources);footer.Children.Add(credit);SetRow(footer,1);Children.Add(footer);
        Loaded+=(_,_)=>Attach();Unloaded+=(_,_)=>Detach();IsVisibleChanged+=(_,_)=>{if(IsVisible)Attach();else Detach();};
        tick.Tick+=(_,_)=>Paint();
    }
    private void Attach()
    {
        if(attached || !IsLoaded || !IsVisible)return;attached=true;app.Updated+=Updated;app.Lyrics.Changed+=Paint;app.Lyrics.Acquire(this);
        for(var parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is ScrollViewer viewer){viewport=viewer;viewport.ScrollChanged+=ViewportChanged;break;}
        host=Window.GetWindow(this);if(host!=null)host.StateChanged+=HostChanged;Refresh();
    }
    private void Detach(){if(!attached)return;attached=false;app.Updated-=Updated;app.Lyrics.Changed-=Paint;if(viewport!=null)viewport.ScrollChanged-=ViewportChanged;if(host!=null)host.StateChanged-=HostChanged;viewport=null;host=null;tick.Stop();if(fixture==null)app.Lyrics.Release(this);}
    private void ViewportChanged(object sender,ScrollChangedEventArgs e)=>Refresh();
    private void HostChanged(object? sender,EventArgs e)=>Refresh();
    private void Updated(){if((app.CurrentUpdate&(RuntimeChange.Media|RuntimeChange.Settings))!=0)Refresh();}
    private void QueueContextLayout()
    {
        if(contextLayoutPending)return;
        contextLayoutPending=true;
        _=Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            contextLayoutPending=false;
            if(!attached)return;

            double remaining=reader.ActualHeight-line.ActualHeight;
            if(previous.IsVisible)remaining-=previous.ActualHeight+LineGap;
            if(next.IsVisible)remaining-=next.ActualHeight+LineGap;
            var hasEarlier=earlier.Text.Length>0;
            var hasLater=later.Text.Length>0;
            double reserve=(hasEarlier?Math.Max(previous.ActualHeight,earlier.FontSize*1.25)+LineGap:0)
                          +(hasLater?Math.Max(next.ActualHeight,later.FontSize*1.25)+LineGap:0);
            bool fits=remaining>=reserve;
            earlier.Visibility=fits && hasEarlier?Visibility.Visible:Visibility.Collapsed;
            later.Visibility=fits && hasLater?Visibility.Visible:Visibility.Collapsed;
        }));
    }
    private void Refresh()
    {
        if(!attached)return;if(!ElementVisibility.InViewport(this)){tick.Stop();if(fixture==null)app.Lyrics.Release(this);return;}
        if(fixture!=null){Paint();return;}
        app.Lyrics.SetTextMode(app.Settings.LyricsTextMode);app.Lyrics.Acquire(this);_=app.Lyrics.Load(app.Media.Title,app.Media.Artist,app.Media.Timeline().duration,app.Settings.LyricsEnabled,LyricTiming.CanUse(app.Media),app.Media.Selected?.SourceAppUserModelId);
        Paint();if(app.Media.Playing && app.Settings.LyricsEnabled && LyricTiming.CanUse(app.Media))tick.Start();else tick.Stop();
    }
    private void Paint()
    {
        if(!attached || !ElementVisibility.InViewport(this)){tick.Stop();return;}var result=fixture?.Result??app.Lyrics.Result;var lines=result.Lines;var position=app.Media.Timeline();double at=(fixture?.At??position.elapsed)+app.Settings.LyricsOffset;
        int now=LyricTiming.Index(lines,at);bool change=now!=index || !ReferenceEquals(last,result) || style!=app.Settings.LyricsAnimation;index=now;last=result;style=app.Settings.LyricsAnimation;
        credit.Text=result.Status;credit.Visibility=lines.Length==0 && result.Plain.Length==0?Visibility.Visible:Visibility.Collapsed;retry.Visibility=lines.Length==0 && !result.Status.StartsWith(Localization.Text("Finding"))?Visibility.Visible:Visibility.Collapsed;
        if(change)
        {
            previous.Text=now>0?lines[now-1].Text:""; current.Text=highlight.Text=result.Plain.Length>0?result.Plain:now>=0?lines[now].Text:lines.Length>0?"♪":Localization.Text("No lyrics yet");next.Text=now+1<lines.Length?lines[now+1].Text:"";
            earlier.Text=now>1?lines[now-2].Text:"";
            later.Text=now+2<lines.Length?lines[now+2].Text:"";
            previous.Visibility=previous.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
            next.Visibility=next.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
            QueueContextLayout();
            current.Opacity=style=="Karaoke" && !Accessibility.HighContrast?.9:1;
            highlight.Visibility=style=="Karaoke" && now>=0?Visibility.Visible:Visibility.Collapsed;
            if(Motion.Enabled && !app.Settings.ReduceMotion)
            {
                if(style=="Slide"){var move=new TranslateTransform(0,5);line.RenderTransform=move;Motion.To(move,TranslateTransform.YProperty,0,260);Motion.Fade(line,1,200);}
                else if(style=="Focus"){var scale=new ScaleTransform(.97,.97);line.RenderTransformOrigin=new(0,.5);line.RenderTransform=scale;Motion.To(scale,ScaleTransform.ScaleXProperty,1,300);Motion.To(scale,ScaleTransform.ScaleYProperty,1,300);line.Opacity=.5;Motion.Fade(line,1,260);}
                else{line.RenderTransform=Transform.Identity;line.Opacity=1;}
            }
            else{line.RenderTransform=Transform.Identity;line.Opacity=1;}
        }
        if(style=="Karaoke" && now>=0)
        {
            double end=now+1<lines.Length?lines[now+1].At:fixture?.Duration??position.duration;double progress=Math.Clamp((at-lines[now].At)/Math.Max(.1,end-lines[now].At),0,1);
            // LRC supplies line timing. This is a line sweep, not fabricated word timestamps.
            var target=new Rect(0,0,Math.Max(0,line.ActualWidth*(app.Settings.ReduceMotion || !Motion.Enabled?1:progress)),Math.Max(0,line.ActualHeight));var before=sweep.Rect;
            sweep.BeginAnimation(RectangleGeometry.RectProperty,null);sweep.Rect=target;
            if(!change && Motion.Enabled && !app.Settings.ReduceMotion && target.Width>=before.Width && target.Width-before.Width<line.ActualWidth*.2)
            {
                var animation=new RectAnimation(before,target,TimeSpan.FromMilliseconds(100)){FillBehavior=FillBehavior.Stop};Timeline.SetDesiredFrameRate(animation,Motion.FrameLimit);sweep.BeginAnimation(RectangleGeometry.RectProperty,animation);
            }
        }
    }
}

internal static class LyricsFiles
{
    internal static string Import(App app,Window? owner)
    {
        if(!LyricTiming.CanUse(app.Media))return Localization.Text("Select a music session with track details and a playback timeline first.");
        var dialog=new Microsoft.Win32.OpenFileDialog{Title=Localization.Text("Timed lyrics for the current track"),Filter=Localization.Text("Timed lyrics (*.lrc)|*.lrc")};if(dialog.ShowDialog(owner)!=true)return Localization.Text("No file chosen.");
        try
        {
            if(new System.IO.FileInfo(dialog.FileName).Length>512*1024)return Localization.Text("Choose an LRC file under 512 KB.");
            app.Lyrics.Import(app.Media.Title,app.Media.Artist,app.Media.Timeline().duration,System.IO.File.ReadAllText(dialog.FileName));app.Settings.LyricsEnabled=true;app.ChangeSettings();return Localization.Text("Local timed lyrics loaded for this session.");
        }
        catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexMatchTimeoutException){return Localization.Text("Could not load this timed lyric file.");}
    }
}

public sealed partial class OverlayWindow
{
    private LyricsView? mediaLyrics;
    private Button? lyricsButton;
    private void ToggleLyrics(){app.Settings.LyricsEnabled=!app.Settings.LyricsEnabled;app.ChangeSettings();}
    private void SyncLyrics()
    {
        bool compatible=LyricTiming.CanUse(app.Media);
        bool changed=false;
        if(mediaLyrics!=null){var visible=app.Settings.LyricsEnabled && compatible?Visibility.Visible:Visibility.Collapsed;changed=mediaLyrics.Visibility!=visible;mediaLyrics.Visibility=visible;}
        if(lyricsButton!=null)
        {
            lyricsButton.Visibility=compatible?Visibility.Visible:Visibility.Collapsed;
            bool enabled=app.Settings.LyricsEnabled;
            string state=Localization.Text(enabled?"On":"Off");
            if(System.Windows.Automation.AutomationProperties.GetItemStatus(lyricsButton)!=state)
            {
                if(enabled)GlassDockAppearance.Emphasize(lyricsButton);
                else
                {
                    lyricsButton.Background=Brushes.Transparent;
                    lyricsButton.ClearValue(Control.ForegroundProperty);
                    lyricsButton.ClearValue(Control.BorderBrushProperty);
                }
                lyricsButton.ToolTip=Localization.Text(enabled?"Hide lyrics":"Show synced lyrics · looks up track details with LRCLIB");
                System.Windows.Automation.AutomationProperties.SetItemStatus(lyricsButton,state);
            }
        }
        if(changed && mediaLyricsLayout?.IsLoaded==true)ResizeToolPanel();
    }
}
