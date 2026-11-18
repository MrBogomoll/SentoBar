using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentoBar;

internal static class MiniLayoutDiagnostics
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;

    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,Fields)!.GetValue(island)!;
        var s=app.Settings;
        s.HoverWidth=300;s.HoverHeight=184;s.TwoStageHover=false;
        s.ReduceMotion=s.HoverPeek=s.HoverFollowsWidgets=s.CompactLayout=s.DetachablePreviews=true;
        s.SmartActivities=s.HideMaximized=s.HideFullscreen=s.AlwaysTucked=false;
        s.ClockSeconds=s.ShowDate=s.ClipboardHistory=s.LiveActivity=true;
        s.WeatherLatitude=s.WeatherLongitude=null;
        foreach(var widget in s.Widgets)widget.Enabled=true;
        foreach(var page in s.HoverViews)page.Enabled=true;
        app.ChangeSettings();
        string longCaption="Длинное название для проверки переноса и размещения текста в мини-панели";
        app.Media.DiagnosticPlayback(true);
        typeof(MediaService).GetProperty("Title")!.SetValue(app.Media,longCaption);
        app.PublishUpdate(RuntimeChange.Media);
        app.Productivity.Note=string.Join("\n",Enumerable.Range(1,9).Select(i=>$"{i}. {longCaption}"));
        for(int i=1;i<=8;i++)app.Productivity.AddTask($"{i}. {longCaption}");
        app.Productivity.IngestText(longCaption);
        var shelfFile=Path.Combine(directory,longCaption+".txt");
        File.WriteAllText(shelfFile,"Fixture");
        app.Productivity.AddFiles(new[]{shelfFile});
        var now=DateTimeOffset.Now;
        app.Calendar.Add(longCaption,now.AddMinutes(10),now.AddMinutes(55),"https://example.com/meeting");
        s.Shortcuts=Enumerable.Range(1,4).Select(i=>new Shortcut{Name=$"{i}. {longCaption}",Target="ms-settings:"}).ToList();
        s.TimerDurationSeconds=s.FocusSeconds=DurationParts.MaximumSeconds;
        app.Assistant.Options.IslandCharacter=true;
        island.DiagnosticPointerPresent=true;

        void Capture(string name)
        {
            var full=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),(int)Math.Ceiling(island.ActualHeight),96,96,PixelFormats.Pbgra32);
            full.Render(island);
            int width=(int)Math.Ceiling(island.PaintedWidth),height=(int)Math.Ceiling(island.PaintedHeight);
            var crop=new CroppedBitmap(full,new Int32Rect((full.PixelWidth-width)/2,0,width,height));
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(crop));
            using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }

        var results=new List<string>();
        void Validate(string scenario,FrameworkElement host)
        {
            var issues=new List<string>();
            foreach(var element in Diagnostics.Descendants(host).OfType<FrameworkElement>())
            {
                if(!element.IsVisible || element is not (ButtonBase or TextBlock or TextBoxBase or ComboBox or Slider or ProgressBar or ActivityVisual))continue;
                // Below-fold items inside a scrollable viewport may be offscreen until scrolled.
                bool scrolled=false;
                for(DependencyObject? parent=VisualTreeHelper.GetParent(element);parent!=null && parent!=host;parent=VisualTreeHelper.GetParent(parent))
                    if(parent is ScrollViewer scroll && (scroll.HorizontalScrollBarVisibility!=ScrollBarVisibility.Disabled && scroll.ScrollableWidth>.75 || scroll.ScrollableHeight>.75)){scrolled=true;break;}
                if(scrolled)continue;
                var bounds=element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));
                var clip=LayoutInformation.GetLayoutClip(element);
                var text=element switch{TextBlock t=>t.Text,HeaderedContentControl h=>h.Header?.ToString(),ContentControl c when c.Content is string caption=>caption,_=>UI.Identity(element)};
                if(bounds.Left<-.75 || bounds.Right>host.ActualWidth+.75 || bounds.Top<-.75 || bounds.Bottom>host.ActualHeight+.75 ||
                   bounds.Width<.5 && element is not TextBlock {Text.Length:0})
                    issues.Add($"{element.GetType().Name} {text} outside {bounds} versus {host.RenderSize}");
                if(clip!=null && (clip.Bounds.Left>.75 || clip.Bounds.Top>.75 || clip.Bounds.Right<element.ActualWidth-.75 || clip.Bounds.Bottom<element.ActualHeight-.75))
                    issues.Add($"{element.GetType().Name} {text} clipped {clip.Bounds} of {element.RenderSize}");
            }
            results.Add(scenario+": "+(issues.Count==0?"OK":string.Join(" | ",issues)));
            check(scenario+(issues.Count==0?"":" · "+string.Join(" | ",issues)),issues.Count==0);
        }
        try
        {
            foreach(string style in new[]{"Dots","Labels","Icons"})
            foreach(string preset in new[]{"iPhone","Mac"})
            {
                s.Preset=preset;s.PanelNavigationStyle=style;app.ChangeSettings();
                island.Collapse();island.PreviewActivity();await Task.Delay(80);
                check($"{preset} {style}: all 18 tabs are available",island.HoverDeckIds.Order().SequenceEqual(HoverPagePolicy.Ids.Order()));
                var viewport=Field<Grid>("peekViewport");
                var footer=Field<Grid>("peekFooter");
                var navigation=Field<ScrollViewer>("peekTabScroll");
                var tabs=Field<Dictionary<string,Button>>("peekDots");
                var strip=(StackPanel)navigation.Content;
                var detach=(Button)footer.Children.Cast<UIElement>().Single(e=>UI.Identity(e)=="Detach preview");
                double[]? dotPositions=null;
                foreach(string id in island.HoverDeckIds)
                {
                    island.SelectPeek(id);await Task.Delay(50);island.UpdateLayout();
                    string scenario=$"{preset}-{style}-{id}";
                    Validate(scenario+" content",viewport);
                    Validate(scenario+" footer",footer);
                    if(id=="media")
                    {
                        Rect Bounds(FrameworkElement control)=>control.TransformToAncestor(viewport).TransformBounds(new Rect(control.RenderSize));
                        var progress=Field<ProgressBar>("peekProgress");
                        var wave=Field<ActivityVisual>("peekWave");
                        var volume=Field<Slider>("peekVolume");
                        var seek=Field<PeekSeekStrip>("peekSeek");
                        var progressBounds=Bounds(progress);
                        var waveBounds=Bounds(wave);
                        var volumeBounds=Bounds(volume);
                        check(scenario+" progress, wave and volume occupy separate rows",
                            progressBounds.Bottom+4<waveBounds.Top && waveBounds.Bottom<volumeBounds.Top &&
                            volumeBounds.Bottom<=viewport.ActualHeight+.5);
                        var seekBounds=Bounds(seek);
                        check(scenario+" seek hit area covers only the timeline",
                            seek.IsHitTestVisible && seekBounds.Top<=progressBounds.Top && seekBounds.Bottom>=progressBounds.Bottom &&
                            Math.Abs(seekBounds.Width-progressBounds.Width)<.5 && seekBounds.Bottom<=waveBounds.Top &&
                            OverlayWindow.PeekGestureBlocked(seek));
                    }
                    if(id is "battery" or "power" or "shortcuts")
                    {
                        string actionId=id=="battery"?"Power settings":id=="power"?"Choose a plan":"All shortcuts";
                        var action=Diagnostics.Descendants(viewport).OfType<Button>().Single(b=>b.IsVisible && UI.Identity(b)==actionId);
                        var bounds=action.TransformToAncestor(viewport).TransformBounds(new Rect(action.RenderSize));
                        check(scenario+" aligns the action with the left content edge",Math.Abs(bounds.Left)<.75);
                    }
                    check(scenario+" has only the previous arrow in the left footer slot",
                        footer.Children.Cast<UIElement>().Where(child=>Grid.GetColumn(child)==0)
                            .SequenceEqual(new UIElement[]{Field<Button>("peekPrevious")}));
                    if(style=="Dots")
                    {
                        double center=strip.TranslatePoint(new Point(strip.ActualWidth/2,0),footer).X;
                        var positions=island.HoverDeckIds.Select(page=>tabs[page].TranslatePoint(new Point(tabs[page].ActualWidth/2,0),footer).X).ToArray();
                        dotPositions??=positions;
                        check(scenario+" all dots fit, remain centered and keep their positions",
                            navigation.ScrollableWidth<.5 && Math.Abs(center-footer.ActualWidth/2)<.75 &&
                            dotPositions.Zip(positions,(expected,actual)=>Math.Abs(expected-actual)<.4).All(equal=>equal));
                    }
                    else
                        check(scenario+" active tab fits in the tab viewport",tabs[id].IsHitTestVisible && tabs[id].Opacity==1 &&
                            (style!="Icons" || (tabs[id].Content as FrameworkElement)?.Tag is string));
                    if(id is "battery" or "timer")
                        check(scenario+" retains the pin slot",footer.ColumnDefinitions[Grid.GetColumn(detach)].ActualWidth>=detach.Width &&
                            detach.Visibility==(id=="timer"?Visibility.Visible:Visibility.Hidden));
                    if(style!="Labels" && preset=="iPhone")Capture(scenario);
                }
                island.SelectPeek("timer");island.UpdateLayout();
                var frame=(FrameworkElement)detach.Template.FindName("Frame",detach);
                var canvas=(Canvas)((Viewbox)detach.Content).Child;
                var pin=(System.Windows.Shapes.Path)canvas.Children[0];
                var pinBounds=pin.TransformToAncestor(detach).TransformBounds(pin.Data.Bounds);
                var circle=frame.TransformToAncestor(detach).TransformBounds(new Rect(frame.RenderSize));
                var offset=pinBounds.Location+new Vector(pinBounds.Width/2,pinBounds.Height/2)-
                    (circle.Location+new Vector(circle.Width/2,circle.Height/2));
                check($"{preset} {style}: pushpin is centered in its circle ({offset.X:F2}, {offset.Y:F2})",
                    Math.Abs(offset.X)<.18 && Math.Abs(offset.Y)<.18 && LayoutInformation.GetLayoutClip((Viewbox)detach.Content)==null);
            }

            var previousMotionSettings=(s.AnimationMs,s.WheelWrapTabs);
            s.ReduceMotion=false;s.AnimationMs=400;s.WheelWrapTabs=false;app.ChangeSettings();
            island.Collapse();island.PreviewActivity();await Task.Delay(80);
            var pages=island.HoverDeckIds;
            var moves=Field<Dictionary<string,TranslateTransform>>("peekMoves");
            var views=Field<Dictionary<string,FrameworkElement>>("peekViews");
            var carouselViewport=Field<Grid>("peekViewport");

            async Task CheckWrap(Button arrow,string from,string to,int direction)
            {
                island.SelectPeek(from);await Task.Delay(350);island.UpdateLayout();
                check("Mini arrow remains enabled at "+from,arrow.IsEnabled);
                arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check($"Mini arrow wraps {from} to {to}",island.PeekPage==to);
                if(Motion.Enabled)
                {
                    await Task.Delay(50);
                    check($"Mini wrap slides one page from {from} to {to}",
                        views[from].IsVisible && views[to].IsVisible && moves[from].X*direction<-.1 &&
                        moves[to].X*direction>.1 && Math.Abs((moves[to].X-moves[from].X)*direction-carouselViewport.ActualWidth)<.5);
                }
                await Task.Delay(350);
                check($"Mini wrap settles on {to}",views[to].IsVisible && !views[from].IsVisible &&
                    Math.Abs(moves[to].X)<.1 && island.PeekTabSafe);
            }

            await CheckWrap(Field<Button>("peekNext"),pages[^1],pages[0],1);
            await CheckWrap(Field<Button>("peekPrevious"),pages[0],pages[^1],-1);

            s.ReduceMotion=true;app.ChangeSettings();
            Keyboard.ClearFocus();
            foreach(var (pressed,from,to) in new[]{(Key.Left,pages[0],pages[^1]),(Key.Right,pages[^1],pages[0])})
            {
                island.SelectPeek(from);
                var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(island)!,Environment.TickCount,pressed)
                    {RoutedEvent=Keyboard.PreviewKeyDownEvent};
                carouselViewport.RaiseEvent(key);
                check($"Mini {pressed} key wraps with reduced motion",key.Handled && island.PeekPage==to && !island.PeekMoving);
            }
            island.SelectPeek(pages[^1]);
            check("Mini wheel stops at the edge when wrapping is off",
                island.RoutePeekWheel(-120,false,null,false) && island.PeekPage==pages[^1]);
            s.WheelWrapTabs=true;app.ChangeSettings();
            check("Mini wheel wraps when its setting is on",
                island.RoutePeekWheel(-120,false,null,false) && island.PeekPage==pages[0]);
            (s.AnimationMs,s.WheelWrapTabs)=previousMotionSettings;app.ChangeSettings();

            island.SelectPeek("media");
            var miniSeek=Field<PeekSeekStrip>("peekSeek");
            typeof(MediaService).GetProperty("CanSeek")!.SetValue(app.Media,false);
            app.PublishUpdate(RuntimeChange.Media);await Task.Delay(60);
            check("Read-only mini timeline shows progress without taking seek gestures",
                !miniSeek.IsHitTestVisible && Field<ProgressBar>("peekProgress").IsVisible);
            typeof(MediaService).GetProperty("CanSeek")!.SetValue(app.Media,true);
            app.PublishUpdate(RuntimeChange.Media);

            var previousShortcuts=s.Shortcuts;
            s.Shortcuts=previousShortcuts.Take(1).ToList();
            app.ChangeSettings();island.Collapse();island.PreviewActivity();island.SelectPeek("shortcuts");await Task.Delay(70);island.UpdateLayout();
            var tiles=Field<Grid>("peekShortcutTiles");
            var singleShortcut=tiles.Children.OfType<Button>().Single();
            var shortcutBounds=singleShortcut.TransformToAncestor(tiles).TransformBounds(new Rect(singleShortcut.RenderSize));
            check("A single shortcut starts at the left content edge",Math.Abs(shortcutBounds.Left)<.75);
            s.Shortcuts=previousShortcuts;app.ChangeSettings();island.Collapse();island.PreviewActivity();

            foreach(string id in new[]{"timer","focus"})
            {
                island.SelectPeek(id);await Task.Delay(50);
                var edit=Diagnostics.Descendants(Field<Grid>("peekViewport")).OfType<Button>()
                    .Single(button=>button.IsVisible && UI.Identity(button)==(id=="timer"?"Edit timer":"Edit focus"));
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(70);
                var editor=Field<ScrollViewer>("peekDurationEditor");
                Validate(id+" duration editor",editor);
                var save=Diagnostics.Descendants(editor).OfType<Button>().Single(button=>UI.Identity(button)=="Save duration");
                var cancel=Diagnostics.Descendants(editor).OfType<Button>().Single(button=>UI.Identity(button)=="Cancel");
                editor.ScrollToEnd();island.UpdateLayout();
                var bounds=save.TransformToAncestor(editor).TransformBounds(new Rect(save.RenderSize));
                check(id+" duration actions are reachable",save.IsEnabled && bounds.Top>=-.5 && bounds.Bottom<=editor.ActualHeight+.5);
                Capture(id+"-editor");
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            island.Collapse();island.Reveal(false);island.SelectPage("notes",false);
            await Task.Delay(80);island.UpdateLayout();
            var expandedTabs=Field<Dictionary<string,Button>>("compactTabs");
            check("Expanded icon navigation shows the active Notes tab",
                expandedTabs["notes"].IsHitTestVisible && expandedTabs["notes"].Content is FrameworkElement { Tag:"notes" } &&
                UI.Identity(expandedTabs["notes"])==Localization.Text("Notes"));
            Capture("expanded-icons");

            s.UiLanguage="ru";s.Preset="iPhone";s.PanelNavigationStyle="Dots";
            s.WeatherCity="Москва, Россия";s.WeatherLatitude=55.75;s.WeatherLongitude=37.62;
            var weather=new WeatherSnapshot
            {
                Key=WeatherService.Key(s),Updated=DateTimeOffset.UtcNow,Temperature=2,High=2,Low=0,Code=3,Day=true
            };
            typeof(WeatherService).GetProperty(nameof(WeatherService.Cached),Fields)!.SetValue(app.Weather,weather);
            foreach(double width in new[]{300d,500d})
            {
                s.HoverWidth=width;app.ChangeSettings();
                island.Collapse();island.PreviewActivity();island.SelectPeek("weather");await Task.Delay(80);island.UpdateLayout();
                var glance=Diagnostics.Descendants(Field<Grid>("peekViewport")).OfType<WeatherGlance>().Single(view=>view.IsVisible);
                Validate($"Weather mini {width}",glance);
                var labels=Diagnostics.Descendants(glance).OfType<TextBlock>().Where(label=>label.IsVisible).ToArray();
                check($"Weather mini shows the configured city and forecast at {width}",labels.Any(label=>label.Text==s.WeatherCity) &&
                    labels.Any(label=>label.Text=="2°") && labels.Any(label=>label.Text.Contains(WeatherService.Condition(weather.Code))));
                check($"Weather mini gives readings space inside the weather card at {width}",labels.All(label=>
                {
                    var bounds=label.TransformToAncestor(glance).TransformBounds(new Rect(label.RenderSize));
                    return bounds.Left>=10 && bounds.Right<=glance.ActualWidth-10 && bounds.Top>=6 && bounds.Bottom<=glance.ActualHeight-6;
                }));
                Capture($"weather-mini-ru-{width}");
            }
            s.HoverWidth=300;s.TwoStageHover=true;s.HoverDetailDelayMs=1200;app.ChangeSettings();
            island.Collapse();island.PreviewActivity();await Task.Delay(80);island.UpdateLayout();
            var compactGlance=Diagnostics.Descendants(Field<Grid>("peekViewport")).OfType<WeatherGlance>().Single(view=>view.IsVisible);
            var compactArt=Diagnostics.Descendants(compactGlance).OfType<WeatherArtwork>().Single(element=>element.IsVisible);
            var compactArtBounds=compactArt.TransformToAncestor(compactGlance).TransformBounds(new Rect(compactArt.RenderSize));
            check("First-stage weather mini fits the forecast icon inside the card",
                !island.PeekDetailed && compactArtBounds.Top>=0 && compactArtBounds.Bottom<=compactGlance.ActualHeight);
            Validate("First-stage weather mini",compactGlance);
            Capture("weather-mini-ru-compact");

            s.WeatherLatitude=s.WeatherLongitude=null;app.ChangeSettings();island.UpdateLayout();
            check("Weather mini explains how to set a city and clears the old forecast when the location is removed",
                !compactArt.IsVisible && Diagnostics.Descendants(compactGlance).OfType<TextBlock>().Any(label=>
                    label.IsVisible && label.Text==Localization.Text("Choose a city in Weather")));
            check("Weather mini uses only synthetic cached data",app.Weather.LastAttempt==default);
            island.Collapse();island.PreviewAssistant();await Task.Delay(80);island.UpdateLayout();
            Validate("Sento preview",Field<Grid>("assistantPeek"));Capture("sento");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(directory,"mini-layout.txt"),results);
            island.DiagnosticPointerPresent=false;
            island.Collapse();
        }
    }
}
