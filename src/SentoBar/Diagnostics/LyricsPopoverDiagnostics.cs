using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace SentoBar;
internal static class LyricsPopoverDiagnostics
{
    internal static async Task RunLanguageLive(App app,string directory)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var match=await app.Lyrics.FindAutomatic("Nanairo Symphony - TV Size","COALAMODE.",92,System.Threading.CancellationToken.None);
        File.WriteAllText(Path.Combine(directory,"language-live.json"),System.Text.Json.JsonSerializer.Serialize(new{Matched=match!=null,Source=match?.Source,Language=match==null?"und":LyricsLanguage.Detect(match.Value.Choice.Lrc),ElapsedMs=watch.ElapsedMilliseconds}));
    }
    internal static async Task RunAutomaticLive(App app,string directory)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var match=await app.Lyrics.FindAutomatic("Rick Astley - Never Gonna Give You Up [Official Video]","Rick Astley - Topic",214,System.Threading.CancellationToken.None);
        File.WriteAllText(Path.Combine(directory,"automatic-live.json"),System.Text.Json.JsonSerializer.Serialize(new{Matched=match!=null,Source=match?.Source,ElapsedMs=watch.ElapsedMilliseconds,Lines=match==null?0:LyricTiming.Parse(match.Value.Choice.Lrc).Length}));
    }
    internal static async Task RunLive(App app,string directory)
    {
        var results=new System.Collections.Generic.List<object>();
        foreach(string source in LyricsProviders.Names.Where(s=>!LyricsProviders.NeedsKey(s) && !LyricsProviders.NeedsVideo(s)))
        {
            using var deadline=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(22));
            try
            {
                var choices=await app.Lyrics.FetchProvider(source,"Never Gonna Give You Up","Rick Astley",214,deadline.Token);
                results.Add(new{Source=source,Status="Responded",Candidates=choices.Length,Error=""});
            }
            catch(Exception ex){results.Add(new{Source=source,Status="Unavailable",Candidates=0,Error=ex.GetType().Name+": "+ex.Message});}
            File.WriteAllText(Path.Combine(directory,"live-provider-results.json"),System.Text.Json.JsonSerializer.Serialize(results,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
    }
    internal static async Task Run(App app,string directory,Action<string,bool> check)
    {
        var view=new LyricsView(app){Background=UI.Brush("#151618")};view.SetFixture(new LyricResult(Array.Empty<LyricLine>(),"No synced lyrics for this track."),0,180);
        var host=new Window{Width=300,Height=250,Content=view,Background=UI.Brush("#151618"),ShowActivated=false};host.Show();await Task.Delay(100);
        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();if(element.ActualWidth<1 || element.ActualHeight<1)return;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
        try
        {
            var trigger=Diagnostics.Descendants(view).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==Localization.Text("Choose lyrics provider"));
            check("Lyrics footer has one compact disclosure instead of three competing actions",Diagnostics.Descendants(view).OfType<Button>().Count(b=>b.IsVisible)==1 && trigger.ActualHeight>=28);
            var credit=(TextBlock)typeof(LyricsView).GetField("credit",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
            check("Lyrics status stays readable and inside the panel",credit.FontSize>=11 && credit.TransformToAncestor(view).Transform(new Point()).Y+credit.ActualHeight<=view.ActualHeight+1);
            Capture(view,"lyrics-empty");trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
            var control=Diagnostics.Descendants(view).OfType<LyricsSourcesControl>().Single();var popup=(Popup)typeof(LyricsSourcesControl).GetField("popup",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(control)!;
            var border=(Border)popup.Child;var viewport=(ScrollViewer)border.Child;
            check("Lyrics popover reuses the dock's gradient material and glass field styles",border.Background is LinearGradientBrush &&
                ReferenceEquals(border.TryFindResource(typeof(TextBox)),app.FindResource("GlassPanelInput")));
            var names=Diagnostics.Descendants(border).OfType<Button>().Select(b=>AutomationProperties.GetName(b)).ToArray();
            check("Lyrics popover contains all fourteen requested provider choices",LyricsProviders.Names.Select(Localization.Text).All(names.Contains));
            check("Automatic lyrics is available alongside all manual sources",names.Contains(Localization.Text("Automatic")));
            check("Lyrics writing system is available directly in the compact menu",names.Contains(Localization.Text("Lyric text")));
            check("Lyrics popover preserves Retry and Local LRC",names.Contains(Localization.Text("Retry lyrics")) && names.Contains(Localization.Text("Local LRC…")));
            check("Lyrics popover escapes the clipped panel and remains bounded",popup.IsOpen && border.ActualWidth<=280 && border.ActualHeight<=350 && viewport.ScrollableHeight>0);
            check("Lyrics popover holds the dock interaction open",LyricsSourcesControl.MenuOpen);
            Capture(border,"lyrics-provider-top");viewport.ScrollToEnd();await Task.Delay(80);Capture(border,"lyrics-provider-bottom");
            check("Provider list scrolls to its final actions",viewport.VerticalOffset>0);
            Diagnostics.Descendants(border).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==Localization.Text("Lyric text")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);viewport.ScrollToTop();await Task.Delay(30);
            var modes=Diagnostics.Descendants(border).OfType<Button>().Select(b=>AutomationProperties.GetName(b)).ToArray();
            check("Original and Japanese Romaji have explicit selectable labels",modes.Contains(Localization.Text("Original")) && modes.Contains(Localization.Text("Japanese Romaji")));Capture(border,"lyrics-text-modes");
            popup.IsOpen=false;await Task.Delay(50);check("Closing lyrics menu releases the dock interaction guard",!LyricsSourcesControl.MenuOpen);
            view.SetFixture(new LyricResult(LyricTiming.Parse("[00:00]Previous line\n[00:06]Current line\n[00:12]Next line"),"Synchronized lyrics · LRCLIB · Automatic"),8,180);
            await Task.Delay(310);view.UpdateLayout();
            check("Playing lyrics hide provider status from the reading area",credit.Visibility==Visibility.Collapsed &&
                Diagnostics.Descendants(view).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text=="Current line"));
            TextBlock Block(string text)=>Diagnostics.Descendants(view).OfType<TextBlock>().First(t=>t.IsVisible && t.Text==text);
            Rect Bounds(string text)
            {
                var block=Block(text);
                return block.TransformToAncestor(view).TransformBounds(new Rect(block.RenderSize));
            }
            var previous=Bounds("Previous line");var current=Bounds("Current line");var next=Bounds("Next line");
            double previousGap=current.Top-previous.Bottom,nextGap=next.Top-current.Bottom;
            check($"Synced lyrics keep adjacent lines close to the larger active line ({previousGap:F1}, {nextGap:F1})",
                previousGap is >=4 and <=8 && nextGap is >=4 and <=8 &&
                current.Height>previous.Height && current.Height>next.Height);
            Capture(view,"lyrics-reading-area");
            var active=Block("Current line");
            check("Both neighboring lines have the same weight and 90 percent of the active line size",
                new[]{Block("Previous line"),Block("Next line")}.All(text=>
                    Math.Abs(text.FontSize/active.FontSize-.9)<.01 && text.FontWeight==active.FontWeight &&
                    text.Opacity<active.Opacity && Equals(text.Foreground,active.Foreground)));

            host.Width=220;view.Height=LyricsView.StackedHeight;
            const string earlier="The evening settles over town";
            const string before="The streets keep changing after the lights come on";
            const string now="We follow the light to the station";
            const string after="The river carries every reflection past the window until everything fades away";
            const string later="The last train turns the corner";
            LyricResult Context(string activeText)=>new(new[]{
                new LyricLine(0,earlier),new LyricLine(6,before),new LyricLine(12,activeText),
                new LyricLine(18,after),new LyricLine(24,later)},"");
            var narrow=Context(now);
            view.SetFixture(narrow,14,180);
            await Task.Delay(310);view.UpdateLayout();
            var reader=Diagnostics.Descendants(view).OfType<ScrollContentPresenter>().First(p=>p.IsVisible);
            var currentBounds=Bounds(now);
            var viewportBounds=reader.TransformToAncestor(view).TransformBounds(new Rect(reader.RenderSize));
            bool TwoRows(string text)
            {
                var block=Block(text);
                return block.TextWrapping==TextWrapping.Wrap && block.TextTrimming==TextTrimming.CharacterEllipsis &&
                    block.ActualHeight>block.FontSize*1.8 && block.ActualHeight<block.FontSize*3;
            }
            check("Narrow lyrics fit two rows per adjacent lyric and keep the active lyric and menu visible",
                TwoRows(before) && TwoRows(after) && Bounds(before).Top>=viewportBounds.Top &&
                Bounds(after).Bottom<=viewportBounds.Bottom+.5 &&
                trigger.TransformToAncestor(view).Transform(new Point()).Y>=currentBounds.Bottom);
            bool Visible(string text)=>Diagnostics.Descendants(view).OfType<TextBlock>().Any(t=>t.IsVisible && t.Text==text);
            check("A narrow lyric pane keeps just the closest lines",!Visible(earlier) && !Visible(later));
            Capture(view,"lyrics-reading-narrow");

            host.Width=300;host.Height=320;view.Height=240;
            const string wrapped="We follow the light\ndown the quiet road\nand back to the station";
            var expanded=Context(wrapped);
            view.SetFixture(expanded,14,180);
            await Task.Delay(310);view.UpdateLayout();
            var expandedViewport=reader.TransformToAncestor(view).TransformBounds(new Rect(reader.RenderSize));
            check("The closest lyrics each wrap to two rows around the active three-row lyric",
                !Visible(earlier) && !Visible(later) && TwoRows(before) && TwoRows(after) &&
                Bounds(before).Top>=expandedViewport.Top && Bounds(before).Bottom<=Bounds(wrapped).Top &&
                Bounds(wrapped).Bottom<=Bounds(after).Top && Bounds(after).Bottom<=expandedViewport.Bottom+.5 &&
                trigger.TransformToAncestor(view).Transform(new Point()).Y>Bounds(after).Bottom);
            Capture(view,"lyrics-reading-two-rows");

            view.SetFixture(expanded,26,180);await Task.Delay(310);
            check("After advancing to the last line, only existing context is shown",
                Visible(after) && Visible(later) && !Visible(wrapped) && !Visible(earlier) && !Visible(before));
        }
        finally{host.Close();}
    }
}
