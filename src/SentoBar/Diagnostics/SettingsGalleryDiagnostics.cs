using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentoBar;

// Renders every Settings category of an isolated profile so layout reviews compare real pages.
internal static class SettingsGalleryDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();app.Settings.SettingsMaterial="Solid";app.Settings.ReduceMotion=true;app.ChangeSettings();
        var window=new SettingsWindow(app){Width=1100,Height=780};window.Show();await Task.Delay(300);
        var navigate=typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var categories=(System.Collections.IEnumerable)typeof(SettingsWindow).GetField("SettingsCategories",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        // Freeze monitor containment so tall full-page captures are not clipped to the screen.
        var fitting=typeof(SettingsWindow).GetField("fittingBounds",BindingFlags.Instance|BindingFlags.NonPublic)!;fitting.SetValue(window,true);window.MaxHeight=double.PositiveInfinity;
        try
        {
            bool hoverTipsSuppressed=true;
            foreach(var category in categories)
            {
                string id=(string)category.GetType().GetProperty("Id")!.GetValue(category)!;
                navigate.Invoke(window,new object[]{id});await Task.Delay(120);window.UpdateLayout();
                hoverTipsSuppressed&=Diagnostics.LogicalDescendants(window).OfType<FrameworkElement>()
                    .Where(element=>element.ToolTip!=null).All(element=>!ToolTipService.GetIsEnabled(element));
                Save(window,window.ActualWidth,window.ActualHeight,Path.Combine(directory,"settings-page-"+id+".png"));
                var page=(FrameworkElement)typeof(SettingsWindow).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                Save(page,page.ActualWidth,page.ActualHeight,Path.Combine(directory,"settings-form-"+id+".png"),true);
                var form=Diagnostics.Descendants(window).OfType<ScrollViewer>().Single(v=>UI.Identity(v)=="Settings form viewport");var origin=page.TransformToAncestor(form).Transform(new Point());
                check($"Settings gallery {id}: page {origin.X:0}+{page.ActualWidth:0}+{page.Margin.Right:0} within viewport {form.ViewportWidth:0} of {form.ActualWidth:0}",origin.X+page.ActualWidth<=form.ViewportWidth+.5);
            }
            check("Hover tooltips are disabled throughout Settings",hoverTipsSuppressed);
            // Narrow window: the sidebar becomes a category chooser above the page.
            foreach(var size in new[]{(640d,700d),(480d,420d)})
            {
                window.Width=size.Item1;window.Height=size.Item2;await Task.Delay(120);navigate.Invoke(window,new object[]{"Appearance"});await Task.Delay(80);window.UpdateLayout();
                Save(window,window.ActualWidth,window.ActualHeight,Path.Combine(directory,$"settings-narrow-{size.Item1:0}.png"));
                var narrowPage=(FrameworkElement)typeof(SettingsWindow).GetField("page",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                var chooser=Diagnostics.Descendants(window).OfType<ComboBox>().Single(c=>UI.Identity(c)=="Settings category");
                check($"Narrow {size.Item1:0}: chooser visible and page laid out (page {narrowPage.ActualWidth:0}, window {window.ActualWidth:0})",chooser.IsVisible && narrowPage.ActualWidth>0);
            }
            window.Width=1100;window.Height=780;await Task.Delay(80);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var settingsPage=(Panel)typeof(SettingsWindow).GetField("page",flags)!.GetValue(window)!;
            string[] Sections()=>settingsPage.Children.OfType<Border>().Select(b=>b.Tag as string).Where(t=>t!=null).Distinct().ToArray()!;
            Button Nav(string id)=>((System.Collections.Generic.Dictionary<string,Button>)typeof(SettingsWindow).GetField("navigationButtons",flags)!.GetValue(window)!)[id];
            navigate.Invoke(window,new object[]{"Appearance"});window.UpdateLayout();
            check("Appearance settings are visible without an extra switch",
                Sections().SequenceEqual(new[]{"Color & material","Shape","Surface","Controls & outlines","Typography"}));
            check("Essentials: dock color, tab controls and window theme stay reachable",
                Nav("Motion").IsVisible && Nav("ClosedDock").IsVisible && Nav("Controls").IsVisible && Nav("Appearance").IsVisible && Nav("WindowAppearance").IsVisible);
            check("The old visibility control is absent from the whole window",
                !Diagnostics.LogicalDescendants(window).OfType<Control>().Any(c=>UI.Identity(c) is "Show all settings" or "Show essentials only"));
            var settings=app.Settings;
            settings.Preset="iPhone";
            settings.DockEdge="Floating";
            settings.Top=24;
            island.Show();
            island.Collapse();
            app.ChangeSettings();
            var surface=(IslandSurface)typeof(OverlayWindow).GetField("surface",flags)!.GetValue(island)!;
            Diagnostics.LogicalDescendants(settingsPage).OfType<Button>().Single(b=>UI.Identity(b)=="Use notch shape")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(380);
            var persisted=app.Store.Load<Settings>("settings");
            var painted=surface.Contour.Bounds;
            check("Choosing the notch from a floating dock attaches it to the top and paints real shoulders",
                persisted.Preset=="Mac" && persisted.DockEdge=="Top" && persisted.Top==0 && surface.Notch &&
                surface.Contour.FillContains(new Point(painted.Left+2,.1)) &&
                !surface.Contour.FillContains(new Point(painted.Left+2,painted.Height/2)));
            island.Hide();
            var livePreview=Diagnostics.LogicalDescendants(window).OfType<Expander>().Single(e=>UI.Identity(e)=="Settings live preview");
            check("The live preview has a rounded, accented header",livePreview.Header is Border {Background:not null});
            navigate.Invoke(window,new object[]{"Home"});window.UpdateLayout();
            var dockContentTile=Diagnostics.LogicalDescendants(settingsPage).OfType<Button>().Single(b=>UI.Identity(b)=="Dock content");
            dockContentTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            check("The Home tile opens dock content directly",Sections().SequenceEqual(new[]{"When idle","Closed media display"}));
            navigate.Invoke(window,new object[]{"WindowAppearance"});window.UpdateLayout();
            check("The settings-window page has theme controls and no dock preview",
                Sections().SequenceEqual(new[]{"Window theme","Window appearance"}) &&
                !Diagnostics.LogicalDescendants(window).OfType<Expander>().Single(e=>UI.Identity(e)=="Settings live preview").IsVisible);
            navigate.Invoke(window,new object[]{"Controls"});window.UpdateLayout();
            var tabStyle=Diagnostics.LogicalDescendants(settingsPage).OfType<ComboBox>().Single(c=>UI.Identity(c)=="Panel navigation style");
            var detach=Diagnostics.LogicalDescendants(settingsPage).OfType<CheckBox>().Single(c=>UI.Identity(c)=="Show Detach button");
            check("Essentials: the first section offers labels, dots and icons",Sections().First()=="Panel navigation" &&
                tabStyle.IsVisible && tabStyle.Items.Cast<string>().SequenceEqual(new[]{"Labels","Dots","Icons"}));
            check("Detaching is explained inline without a tooltip",
                detach.IsVisible && detach.ToolTip==null &&
                Diagnostics.LogicalDescendants(settingsPage).OfType<TextBlock>().Any(t=>t.IsVisible &&
                    t.Text==Localization.Text("Click the pushpin at the bottom right to open this widget in its own window. Drag the pushpin to position the window as it opens.")));
            navigate.Invoke(window,new object[]{"Clipboard"});window.UpdateLayout();
            check("Essentials: clipboard capture stays reachable from widgets",Sections().Contains("Clipboard history"));
            var open=typeof(SettingsWindow).GetMethod("OpenSettingsSearchResult",flags)!;
            open.Invoke(window,new object[]{new SettingsSearchItem("Quiet hours","Notifications","Notification presentation","","quiet")});window.UpdateLayout();
            check("Search still opens a section directly",Sections().Contains("Notification presentation"));

            navigate.Invoke(window,new object[]{"Application"});window.UpdateLayout();
            var details=Diagnostics.LogicalDescendants(settingsPage).OfType<Expander>().Single(e=>UI.Identity(e)=="Private diagnostics");
            var report=Diagnostics.LogicalDescendants(details).OfType<TextBox>().Single();
            check("Private diagnostics starts collapsed with no generated report",!details.IsExpanded && !report.IsVisible && report.Text.Length==0);
            open.Invoke(window,new object[]{new SettingsSearchItem("Private diagnostics","Application","Private diagnostics","","diagnostics")});
            await Task.Delay(80);window.UpdateLayout();
            details=Diagnostics.LogicalDescendants(settingsPage).OfType<Expander>().Single(e=>UI.Identity(e)=="Private diagnostics");
            report=Diagnostics.LogicalDescendants(details).OfType<TextBox>().Single();
            check("Finding diagnostics opens and generates the existing report",details.IsExpanded && report.IsVisible && report.Text.Contains("ReportSchemaVersion"));

            navigate.Invoke(window,new object[]{"ClosedDock"});window.UpdateLayout();
            var idle=Diagnostics.LogicalDescendants(settingsPage).OfType<ComboBox>().Single(c=>UI.Identity(c)=="Idle dock content");
            var mediaIcon=Diagnostics.LogicalDescendants(settingsPage).OfType<ComboBox>().Single(c=>UI.Identity(c)=="Closed media icon");
            check("The media icon selector and System category have accessible descriptions without hover tooltips",
                mediaIcon.ToolTip==null && AutomationProperties.GetHelpText(mediaIcon).Length>0 &&
                Nav("System").ToolTip==null && AutomationProperties.GetHelpText(Nav("System")).Length>0);
            check("Dock content shows separate idle and music choices",Sections().SequenceEqual(new[]{"When idle","Closed media display"}) &&
                idle.Items.Cast<string>().SequenceEqual(new[]{"Clock and date","Ask Sento"}));
            island.Show();island.Collapse();app.Media.DiagnosticPlayback(false);
            idle.SelectedIndex=1;
            await Task.Delay(420);
            var prompt=(Button)typeof(OverlayWindow).GetField("idlePrompt",flags)!.GetValue(island)!;
            check("Choosing Ask Sento saves and displays the idle action on the actual dock",
                app.Store.Load<Settings>("settings").ClosedIdleContent=="Assistant" && prompt.IsVisible);
            livePreview.IsExpanded=true;window.UpdateLayout();
            var previewLabel=(TextBlock)typeof(SettingsWindow).GetField("previewSummaryLabel",flags)!.GetValue(window)!;
            check("The closed preview follows the saved idle choice",previewLabel.Text==Localization.Text("Ask Sento") && previewLabel.IsVisible);
            prompt.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("The idle dock action opens the assistant",island.AssistantOpen);
            island.Collapse();
            idle.SelectedIndex=0;await Task.Delay(420);
            check("Choosing clocks restores the normal idle dock",app.Settings.ClosedIdleContent=="Clock" && !prompt.IsVisible);
            island.Hide();
        }
        finally{fitting.SetValue(window,false);window.Close();}
    }
    private static void Save(Visual visual,double width,double height,string path,bool brush=false)
    {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(Math.Min(height,8000)),96,96,PixelFormats.Pbgra32);
        if(brush)
        {
            var drawing=new DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(UI.Brush("#111214"),null,new Rect(0,0,width,height));context.DrawRectangle(new VisualBrush(visual){Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top},null,new Rect(0,0,width,height));}
            bitmap.Render(drawing);
        }
        else bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
