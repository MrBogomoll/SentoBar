using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentoBar;

internal static class DetachedAppearanceDiagnostics
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);

    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var settings=app.Settings;
        settings.DetachablePreviews=true;
        settings.ReduceMotion=true;
        settings.Opacity=.9;
        settings.Background="#000000";
        foreach(var tab in settings.Widgets)tab.Enabled=true;
        foreach(var tab in settings.HoverViews)tab.Enabled=true;
        app.ChangeSettings();
        app.Media.DiagnosticPlayback(true);
        island.Collapse();
        var area=Native.ChooseScreen(settings).WorkingArea;
        var anchor=new Point(area.Left+area.Width*.35,area.Top+area.Height*.28);
        foreach(string kind in new[]{"media","timer","focus","stopwatch","notes","files","calendar","system"})
        {
            app.Previews.OpenAt(kind,anchor,false);
            var window=Application.Current.Windows.OfType<DetachedPreviewWindow>().Single(w=>w.IsVisible);
            try
            {
                await Task.Delay(110);
                window.UpdateLayout();
                var source=(HwndSource)PresentationSource.FromVisual(window)!;
                check(kind+" has a layered client without an opaque window backing",window.AllowsTransparency &&
                    source.UsesPerPixelOpacity && source.CompositionTarget.BackgroundColor.A==0);
                foreach(bool minimum in new[]{false,true})
                {
                    if(minimum){window.Width=window.MinWidth;window.Height=window.MinHeight;await Task.Delay(100);window.UpdateLayout();}
                    int width=(int)Math.Ceiling(window.ActualWidth),height=(int)Math.Ceiling(window.ActualHeight);
                    var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    byte[] pixel=new byte[4];
                    byte Alpha(int x,int y){bitmap.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel[3];}
                    check($"{kind} {(minimum?"min":"default")} paints the surface with four transparent corners",
                        Alpha(1,1)==0 && Alpha(width-2,1)==0 && Alpha(1,height-2)==0 && Alpha(width-2,height-2)==0 &&
                        Alpha(width/2,10)>100);
                    if(kind=="media" || !minimum)
                    {
                        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file=File.Create(Path.Combine(directory,"detached-"+kind+(minimum?"-min":"")+".png"));encoder.Save(file);
                    }
                }
                var hit=window.PointToScreen(new Point(window.ActualWidth/2,2));
                int nativeX=(int)Math.Round(hit.X),nativeY=(int)Math.Round(hit.Y);
                var coordinates=new IntPtr((nativeX&0xFFFF)|(nativeY<<16));
                check(kind+" retains native resizing on a painted edge",window.ResizeMode==ResizeMode.CanResize &&
                    SendMessage(source.Handle,0x0084,IntPtr.Zero,coordinates).ToInt32()==12);
                if(kind=="media")
                {
                    var label=Diagnostics.Descendants(window).OfType<TextBlock>().Single(element=>element.Text==Localization.Text("Volume"));
                    var natural=new TextBlock{Text=label.Text,FontFamily=label.FontFamily,FontSize=label.FontSize};
                    natural.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                    check("Detached music volume label stays on one line at minimum width",label.ActualHeight<=natural.DesiredSize.Height+.75 &&
                        label.ActualWidth>=natural.DesiredSize.Width-.75);
                    var next=Diagnostics.Descendants(window).OfType<Button>().Single(button=>UI.Identity(button)==Localization.Text("Next"));
                    var bounds=next.TransformToAncestor(window).TransformBounds(new Rect(next.RenderSize));
                    check("Detached music transport stays within the narrow window",bounds.Left>=0 && bounds.Right<=window.ActualWidth+.75);
                }
            }
            finally{window.Close();}
        }
        await CheckIslandPins(app,island,check);
    }

    private static async Task CheckIslandPins(App app,OverlayWindow island,Action<string,bool> check)
    {
        var s=app.Settings;
        s.CompactLayout=s.HoverPeek=s.HoverFollowsWidgets=true;
        s.AlwaysTucked=false;
        s.PanelWidth=300;s.PanelPadding=40;s.PanelScale=1.25;
        s.PanelNavigationStyle="Dots";
        app.ChangeSettings();
        Button[] Pins()=>Diagnostics.Descendants(island).OfType<Button>()
            .Where(b=>b.IsVisible && UI.Identity(b)=="Detach preview").ToArray();
        async Task Settle(){await Task.Delay(80);island.UpdateLayout();}
        async Task OpenPin(string kind,string context)
        {
            var pin=Pins().SingleOrDefault();
            var invoke=pin==null?null:new ButtonAutomationPeer(pin).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            check(context+" has a keyboard-accessible pin",pin?.Focusable==true && pin.IsTabStop && invoke!=null);
            invoke?.Invoke();
            await Settle();
            check(context+" opens its own floating window and closes the island",
                app.Previews.AnyVisible(kind) && !island.IsExpanded && !island.IsPeeking);
            foreach(var window in Application.Current.Windows.OfType<DetachedPreviewWindow>().ToArray())window.Close();
        }

        string[] kinds={"media","timer","focus","stopwatch","system","calendar","notes","files"};
        foreach(string kind in kinds)
        {
            island.OpenFeaturePage(kind);
            await Settle();
            await OpenPin(kind,"Expanded "+kind);
        }
        island.OpenFeaturePage("tasks");
        await Settle();
        check("Expanded Tasks has no detachment action",Pins().Length==0);

        foreach(string style in new[]{"Dots","Labels","Icons"})
        {
            s.PanelNavigationStyle=style;app.ChangeSettings();island.OpenFeaturePage("system");
            await Settle();
            var header=Diagnostics.Descendants(island).OfType<Grid>().Single(g=>Equals(g.Tag,"panel-navigation"));
            var tabs=Diagnostics.Descendants(header).OfType<ScrollViewer>().Single();
            var pin=Pins().SingleOrDefault();
            Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(header).TransformBounds(new Rect(element.RenderSize));
            bool reachable=pin!=null && island.InputHitTest(pin.TranslatePoint(new Point(pin.ActualWidth/2,pin.ActualHeight/2),island))
                is DependencyObject hit && (ReferenceEquals(pin,hit) || pin.IsAncestorOf(hit));
            check("Narrow "+style+" header keeps the System pin and tabs reachable",reachable &&
                Bounds(pin!).Left>=0 && Bounds(pin!).Right<=Bounds(tabs).Left+.5 && tabs.ViewportWidth>=28);
        }

        s.DetachablePreviews=false;app.ChangeSettings();await Settle();
        check("Disabling detachment hides the expanded pin without leaving System",
            Pins().Length==0 && island.IsExpanded && island.ActivePage=="status");
        s.DetachablePreviews=true;
        s.HoverFollowsWidgets=false;
        s.Widgets.Single(w=>w.Id=="system").Enabled=false;
        app.ChangeSettings();island.Collapse();island.PreviewActivity();island.SelectPeek("system");
        await Settle();
        await OpenPin("system","Mini System with its expanded widget disabled");
        island.Collapse();island.PreviewActivity();island.SelectPeek("system");island.OpenPeekPage();
        await Settle();
        await OpenPin("system","Expanded System opened from a separately enabled mini widget");

        s.CompactLayout=false;
        s.Widgets.Single(w=>w.Id=="system").Enabled=true;
        app.ChangeSettings();island.Collapse();island.Reveal(false);await Settle();
        check("Classic cards give each supported widget a detachment action",Pins().Length==kinds.Length);
        island.OpenFeaturePage("notes");await Settle();
        await OpenPin("notes","Classic Notes opened through its full-panel route");
    }
}
