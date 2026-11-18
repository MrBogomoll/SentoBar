using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class HoverResponsivenessDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var settings=app.Settings;settings.SmartActivities=false;settings.HideFullscreen=settings.HideMaximized=false;
        settings.HoverPeek=settings.CompactLayout=settings.PanelSwipe=settings.WheelWrapTabs=true;
        settings.ClipboardHistory=true;settings.ReduceMotion=false;settings.AnimationMs=420;settings.TwoStageHover=false;
        foreach(var widget in settings.Widgets)widget.Enabled=true;
        foreach(var page in settings.HoverViews)page.Enabled=true;
        app.ChangeSettings();app.Media.DiagnosticPlayback(true);app.Audio.DiagnosticPeak=()=>.3f;
        for(int i=0;i<30;i++)app.Productivity.IngestText("Synthetic clipboard row "+i);
        island.DiagnosticPointerPresent=true;await Task.Delay(500);
        var samples=new List<object>();var intervals=new List<double>();TimeSpan? previous=null;
        EventHandler frame=(_,args)=>{var time=((RenderingEventArgs)args).RenderingTime;if(previous is {} last && time>last)intervals.Add((time-last).TotalMilliseconds);previous=time;};
        CompositionTarget.Rendering+=frame;
        try
        {
            for(int attempt=0;attempt<8;attempt++)
            {
                island.Collapse();await Task.Delay(650);intervals.Clear();previous=null;
                var firstFrame=new TaskCompletionSource<double>();var watch=Stopwatch.StartNew();
                EventHandler? first=null;first=(_,_)=>{CompositionTarget.Rendering-=first;firstFrame.TrySetResult(watch.Elapsed.TotalMilliseconds);};
                CompositionTarget.Rendering+=first;
                double openMs;double firstMs;
                try
                {
                    island.PreviewActivity();openMs=watch.Elapsed.TotalMilliseconds;
                    firstMs=await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    // Spring timing depends on distance and preserves velocity on
                    // reversal. Observe completion rather than assuming a fixed
                    // sleep means that its final subpixel tail has settled.
                    while(island.IsAnimating && watch.ElapsedMilliseconds<2000)await Task.Delay(16);
                }
                finally{CompositionTarget.Rendering-=first;}
                var sorted=intervals.OrderBy(x=>x).ToArray();
                samples.Add(new{attempt,cold=attempt==0,openMs,firstRenderCallbackMs=firstMs,settledMs=watch.Elapsed.TotalMilliseconds,frameP95Ms=sorted.Length==0?0:sorted[(int)((sorted.Length-1)*.95)],maxFrameMs=sorted.DefaultIfEmpty().Max()});
                check("Hover: repeated opening reaches the preview without expanding the main panel "+attempt,island.IsPeeking && !island.IsExpanded && island.PeekOpacity>.99 && !island.IsAnimating);
            }
            var views=(Dictionary<string,FrameworkElement>)typeof(OverlayWindow).GetField("peekViews",flags)!.GetValue(island)!;
            var viewport=(Grid)typeof(OverlayWindow).GetField("peekViewport",flags)!.GetValue(island)!;
            check("Hover: music preview does not construct hidden widget editors",views.Count==1 && views.ContainsKey("media") && typeof(OverlayWindow).GetField("peekNote",flags)!.GetValue(island)==null);
            foreach(string id in island.HoverDeckIds)
            {
                island.SelectPeek(id);await Task.Delay(350);island.UpdateLayout();
                check("Hover: lazy page remains visible, fits and is keyboard safe · "+id,island.PeekPage==id && views.TryGetValue(id,out var view) && view.IsVisible && view.ActualWidth>0 && Math.Abs(view.ActualWidth-viewport.ActualWidth)<.1 && Math.Abs(view.Height-viewport.Height)<.1 && island.PeekTabSafe);
            }
            island.SelectPeek("media");await Task.Delay(350);island.UpdateLayout();
            var image=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),(int)Math.Ceiling(island.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(island);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(directory,"hover-music.png")))encoder.Save(file);
            // Exercise real painted-edge placement while springs run. Display/DPI
            // changes still refresh placement context through ReflowDisplay.
            foreach(string edge in new[]{"Top","Bottom","Left","Right"})
            {
                settings.DockEdge=edge;app.ChangeSettings();island.Collapse();island.PreviewActivity();await Task.Delay(700);
                check("Hover: contour placement finishes on "+edge,!island.IsAnimating && double.IsFinite(island.Left) && double.IsFinite(island.Top) && Math.Abs(island.PaintedHeight-settings.HoverHeight)<.1);
            }
            var wheelSamples=new List<object>();
            async Task MeasureWheel(string mode,string[] ids,Func<string> current,Func<bool> route)
            {
                const int steps=96;
                var times=new List<double>(steps);
                int start=Array.IndexOf(ids,current());
                bool advanced=start>=0;
                for(int step=1;step<=steps;step++)
                {
                    var watch=Stopwatch.StartNew();
                    bool handled=route();
                    island.UpdateLayout();
                    times.Add(watch.Elapsed.TotalMilliseconds);
                    advanced&=handled && current()==ids[(start+step)%ids.Length];
                    await Task.Delay(16);
                }
                times.Sort();
                check($"Wheel: {mode} applies all {steps} fast inputs in order",advanced);
                wheelSamples.Add(new{mode,tabs=ids.Length,steps,p95InputAndLayoutMs=times[(int)((times.Count-1)*.95)],maxInputAndLayoutMs=times[^1]});
            }
            island.SelectPeek(island.HoverDeckIds[0]);await Task.Delay(350);island.UpdateLayout();
            var miniPages=views.ToArray();
            await MeasureWheel("mini",island.HoverDeckIds,()=>island.PeekPage,()=>island.RoutePeekWheel(-120,false,null,false));
            check("Wheel: switching the mini panel reuses cached tabs",miniPages.Length==views.Count &&
                miniPages.All(pair=>views.TryGetValue(pair.Key,out var view) && ReferenceEquals(view,pair.Value)));

            island.Reveal(false);await Task.Delay(500);
            var tabs=(Dictionary<string,Button>)typeof(OverlayWindow).GetField("compactTabs",flags)!.GetValue(island)!;
            var pages=(Dictionary<string,FrameworkElement>)typeof(OverlayWindow).GetField("compactPages",flags)!.GetValue(island)!;
            string[] expandedIds=tabs.Keys.ToArray();
            foreach(string id in expandedIds){island.SelectPage(id,false);island.UpdateLayout();}
            var expandedPages=expandedIds.Select(id=>(id,view:pages[id])).ToArray();
            await MeasureWheel("expanded",expandedIds,()=>island.ActivePage,()=>island.RoutePanelWheel(-120,false,null,false));
            check("Wheel: switching the expanded panel reuses its page containers",
                expandedPages.All(page=>ReferenceEquals(pages[page.id],page.view)));
            File.WriteAllText(Path.Combine(directory,"hover-timing.json"),JsonSerializer.Serialize(new{note="Isolated WPF invocation. Times exclude configured pointer dwell. Wheel timings include input routing and synchronous WPF layout, not GPU presentation. No live microphones or clipboard capture.",configuredHoverDelayMs=settings.HoverDelayMs,samples,wheelSamples},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{CompositionTarget.Rendering-=frame;island.DiagnosticPointerPresent=false;island.Collapse();}
    }
}
