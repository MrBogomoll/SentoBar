using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

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
    }
}
