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

namespace NabyScape;

internal static class PanelBackdropDiagnostics
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var s=app.Settings;
        s.ReduceMotion=true;s.HoverPeek=true;s.TwoStageHover=false;s.HoverFollowsWidgets=true;s.DetachablePreviews=true;
        s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=s.SmartActivities=s.CoverAmbient=s.ClosedAmbient=false;
        s.CompactLayout=true;s.PanelNavigationStyle="Dots";s.PanelWidth=730;s.PanelMaxHeight=570;s.HoverWidth=430;
        s.Background="#111620";s.Preset="iPhone";
        foreach(var w in s.Widgets)w.Enabled=true;
        foreach(var w in s.HoverViews)w.Enabled=true;
        var options=s.PanelBackdrop;options.Enabled=true;
        options.Gradient="Two soft lights";options.Opacity=.84;options.Strength=.8;options.Image="No image";
        app.ChangeSettings();
        island.DiagnosticPointerPresent=true;
        var surface=(IslandSurface)typeof(OverlayWindow).GetField("surface",Fields)!.GetValue(island)!;
        async Task Settle(){await Task.Delay(120);island.UpdateLayout();}
        static RenderTargetBitmap Capture(FrameworkElement element)
        {
            var result=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
            result.Render(element);return result;
        }
        static Color Sample(BitmapSource source,int x,int y)
        {
            byte[] pixel=new byte[4];source.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);
            return Color.FromArgb(pixel[3],pixel[2],pixel[1],pixel[0]);
        }
        void Save(string name,BitmapSource picture)
        {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(picture));
            using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
        bool Corners(BitmapSource image,Rect area)
        {
            var left=Sample(image,(int)(area.Left+area.Width*.15),(int)(area.Top+area.Height*.80));
            var right=Sample(image,(int)(area.Left+area.Width*.85),(int)(area.Top+area.Height*.80));
            return left.B>left.R+20 && right.R>right.B+20 && Math.Abs(left.A-options.Opacity*255)<3;
        }
        void SavePanel(string phase)
        {
            var window=Capture(island);
            int width=(int)Math.Ceiling(island.PaintedWidth),height=(int)Math.Ceiling(island.PaintedHeight);
            Save(phase,new CroppedBitmap(window,new Int32Rect((window.PixelWidth-width)/2,0,width,height)));
        }
        void InspectDock(string phase)
        {
            check(phase+" shares the selected blue and gold corner colors and background opacity",
                island.Opacity==1 && Corners(Capture(surface),surface.Contour.Bounds));
            SavePanel(phase);
        }

        island.Collapse();await Settle();InspectDock("closed");
        island.PreviewActivity();island.SelectPeek("notes");await Settle();InspectDock("mini");
        island.Reveal(false);island.SelectPage("calendar",false);await Settle();InspectDock("expanded");
        s.UseClosedBackgroundOpacity=true;s.ClosedBackgroundOpacity=.42;
        app.ChangeSettings();island.Collapse();await Settle();
        int MidAlpha()=>Sample(Capture(surface),(int)(surface.Contour.Bounds.Left+surface.Contour.Bounds.Width/2),10).A;
        check("Closed opacity affects the background while UI stays at full opacity",island.Opacity==1 && Math.Abs(MidAlpha()-.42*255)<3);
        island.PreviewActivity();island.SelectPeek("notes");await Settle();
        check("Hover restores the shared background opacity",Math.Abs(MidAlpha()-options.Opacity*255)<3);
        island.Reveal(false);island.SelectPage("calendar",false);await Settle();
        check("Expanded restores the shared background opacity",Math.Abs(MidAlpha()-options.Opacity*255)<3);
        Button CalendarButton(string name)=>Diagnostics.Descendants(island).OfType<Button>()
            .Single(button=>button.IsVisible && UI.Identity(button)==Localization.Text(name));
        var todayAction=CalendarButton("Today");
        var calendarFill=(SolidColorBrush)todayAction.Background;
        var calendarStroke=(SolidColorBrush)todayAction.BorderBrush;
        var addEvent=CalendarButton("Add local event");
        check("Add event matches Today's transparent fill, border and height",
            addEvent.Background==Brushes.Transparent && addEvent.BorderThickness==todayAction.BorderThickness &&
                addEvent.BorderBrush==todayAction.BorderBrush && Math.Abs(addEvent.ActualHeight-todayAction.ActualHeight)<.5);
        var actions=new[]{"Toggle calendar month","Previous calendar month","Next calendar month","Today","Add local event"}.Select(CalendarButton).ToArray();
        Rect ButtonBounds(Button button)=>button.TransformToAncestor(island).TransformBounds(new Rect(button.RenderSize));
        check("Calendar toolbar leaves six pixels between every adjoining button",
            actions.Zip(actions.Skip(1),(left,right)=>Math.Abs(ButtonBounds(right).Left-ButtonBounds(left).Right-6)<.6).All(gap=>gap));
        var header=Diagnostics.Descendants(island).OfType<Grid>().Single(g=>Equals(g.Tag,"panel-navigation"));
        var scroller=Diagnostics.Descendants(header).OfType<ScrollViewer>().Single();
        var row=(StackPanel)scroller.Content;
        var center=row.TransformToAncestor(header).Transform(new Point(row.ActualWidth/2,0));
        check("Expanded navigation dots are geometrically centered",Math.Abs(center.X-header.ActualWidth/2)<1 && scroller.ScrollableWidth<.75);
        Button FindDate(DateTime date)=>Diagnostics.Descendants(island).OfType<Button>().Single(b=>b.IsVisible &&
            UI.Identity(b)==date.ToString("dddd, d MMMM yyyy",Localization.Culture));
        var today=DateTime.Today;
        var other=MinimalWidgets.Week(today).First(day=>day!=today);
        FindDate(other).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
        var todayWeek=FindDate(today);
        check("Today's week cell keeps its circular outline when another date is selected",
            AutomationProperties.GetItemStatus(todayWeek)==Localization.Text("Today") &&
            Diagnostics.Descendants(todayWeek).OfType<Border>().Any(b=>b.CornerRadius.TopLeft==15 &&
                b.BorderThickness.Left>0 && b.BorderBrush==GlassDockAppearance.Accent));
        Diagnostics.Descendants(island).OfType<Button>().Single(b=>b.IsVisible && UI.Identity(b)==Localization.Text("Toggle calendar month"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
        var todayMonth=FindDate(today);
        check("Today's monthly cell has the accent outline without changing its label",
            AutomationProperties.GetItemStatus(todayMonth)==Localization.Text("Today") &&
            todayMonth.BorderThickness.Left>0 && todayMonth.BorderBrush==GlassDockAppearance.Accent &&
            todayMonth.Content is string);
        Save("calendar-today",Capture(island));
        foreach(string id in new[]{"awake","power"})
        {
            island.ShowTools(id,false);await Settle();
            var box=Diagnostics.Descendants(island).OfType<ComboBox>().Single(b=>b.IsVisible);
            box.IsDropDownOpen=true;await Settle();
            var popup=(Popup)box.Template.FindName("PART_Popup",box);
            check(id+" dropdown uses shared palette and round edges",popup.IsOpen && popup.Child is Border
                {Background:LinearGradientBrush,CornerRadius.TopLeft:>=12});
            box.IsDropDownOpen=false;
        }

        bool CalendarStroke(Brush brush)=>brush is SolidColorBrush solid && solid.Color==calendarStroke.Color;
        bool SharedField(Control field)=>field.IsVisible && field.Background is SolidColorBrush fill && fill.Color.A==calendarFill.Color.A &&
            field.BorderBrush==app.FindResource("GlassControlBorder") && field.BorderThickness==new Thickness(1);
        TextBox[] Editors()=>Diagnostics.Descendants(island).OfType<TextBox>().Where(field=>field.IsVisible).ToArray();
        island.Collapse();island.PreviewActivity();island.SelectPeek("notes");await Settle();
        var miniEditors=Editors();
        check("Mini Notes uses the shared translucent editor surface",miniEditors.Length==2 && miniEditors.All(SharedField));
        island.SelectPeek("awake");await Settle();
        var miniChoice=Diagnostics.Descendants(island).OfType<ComboBox>().Single(box=>box.IsVisible);
        bool HasSwitch()=>Diagnostics.Descendants(island).OfType<CheckBox>().Any(box=>box.IsVisible &&
            box.Template.FindName("Track",box) is Border track && CalendarStroke(track.BorderBrush));
        check("Mini Awake uses an outlined switch",HasSwitch());
        check("Mini drop-down uses the same translucent field and outline",SharedField(miniChoice) &&
            miniEditors.All(editor=>editor.Background==miniChoice.Background));
        miniChoice.IsDropDownOpen=true;await Settle();
        var miniPopup=(Popup)miniChoice.Template.FindName("PART_Popup",miniChoice);
        check("Mini drop-down opens on the shared menu material",miniPopup.IsOpen && miniPopup.Child is Border miniFrame &&
            miniFrame.Background==app.FindResource("PopupFill") && miniFrame.BorderBrush==app.FindResource("MenuBorder") &&
            miniChoice.BorderBrush==app.FindResource("GlassFieldFocus"));
        miniChoice.IsDropDownOpen=false;
        island.Reveal(false);island.SelectPage("notes",false);await Settle();
        var expandedEditors=Editors();
        check("Expanded Notes uses the same material and outline as the mini editor",expandedEditors.Length==2 &&
            expandedEditors.All(editor=>SharedField(editor) && editor.Background==miniEditors[0].Background));

        island.ShowTools("awake",false);await Settle();
        check("Expanded Awake uses the same outlined switch",HasSwitch());
        island.SelectPage("volume",false);await Settle();
        bool HasGlassSlider()=>Diagnostics.Descendants(island).OfType<Slider>().Any(slider=>slider.IsVisible &&
            slider.Template.FindName("Rail",slider) is Border rail && CalendarStroke(rail.BorderBrush));
        check("Expanded Volume has an outlined slider",HasGlassSlider());
        island.Collapse();island.PreviewActivity();island.SelectPeek("volume");await Settle();
        check("Mini Volume has the same outlined slider",HasGlassSlider());

        island.OpenFeaturePage("voice");await Settle();
        var voiceOptions=Diagnostics.Descendants(island).OfType<Button>().Single(b=>b.IsVisible && UI.Identity(b)=="Options");
        voiceOptions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
        var nabyMenu=voiceOptions.ContextMenu;
        check("Naby Options uses the same panel menu palette",nabyMenu.IsOpen && nabyMenu.Background is LinearGradientBrush &&
            nabyMenu.BorderBrush==app.FindResource("MenuBorder"));
        nabyMenu.IsOpen=false;

        island.Collapse();
        app.Previews.Open("notes");await Task.Delay(100);
        var detached=Application.Current.Windows.OfType<DetachedPreviewWindow>().Single();
        try
        {
            var frame=(Border)detached.Content;
            var material=new System.Windows.Shapes.Rectangle{Fill=frame.Background};
            material.Measure(new Size(frame.ActualWidth,frame.ActualHeight));
            material.Arrange(new Rect(frame.RenderSize));
            check("Detached windows share the same background",frame.Opacity==1 && Corners(Capture(material),new Rect(material.RenderSize)));
        }
        finally{detached.Close();}

        var fixture=new DrawingVisual();
        using(var context=fixture.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black,null,new Rect(0,0,60,80));
            context.DrawRectangle(Brushes.White,null,new Rect(60,0,60,80));
        }
        var image=new RenderTargetBitmap(120,80,96,96,PixelFormats.Pbgra32);
        image.Render(fixture);Save("input-fixture",image);
        PanelBackdropRenderer.ImportPicture(app.Store.DirectoryPath,Path.Combine(directory,"input-fixture.png"));
        options.Image="Custom picture";options.ImageOpacity=1;options.Gradient="No gradient";options.Opacity=1;options.Blur=0;
        var renderer=app.PanelBackground;
        BitmapSource RenderPicture()
        {
            var drawing=new DrawingVisual();
            using(var context=drawing.RenderOpen())renderer.Get(s,app.Store.DirectoryPath)!.Draw(context,new RectangleGeometry(new Rect(0,0,600,400)));
            var bitmap=new RenderTargetBitmap(600,400,96,96,PixelFormats.Pbgra32);
            bitmap.Render(drawing);return bitmap;
        }
        var sharp=RenderPicture();options.Blur=26;var soft=RenderPicture();
        int sharpLeft=Sample(sharp,296,200).R,sharpRight=Sample(sharp,304,200).R;
        int softLeft=Sample(soft,296,200).R,softRight=Sample(soft,304,200).R;
        check($"The imported picture is drawn and blur softens edges ({sharpLeft}/{sharpRight} → {softLeft}/{softRight})",
            sharpRight-sharpLeft>150 && softLeft>sharpLeft+30 && softRight<sharpRight-30 && softRight>softLeft);
        Save("blurred-background",soft);
        var layout=CustomizationProfile.Export(s);var clone=new Settings();
        CustomizationProfile.Import(layout,clone);
        check("The shared palette, closed opacity, and picture settings survive a layout round trip with no file path",
            clone.PanelBackdrop==s.PanelBackdrop && clone.DockBackgroundOpacity(true)==.42 && clone.DockBackgroundOpacity(false)==null && !layout.Contains(directory,StringComparison.Ordinal));
        options.Image="No image";options.Gradient="Two soft lights";app.ChangeSettings();
        var settingsWindow=new SettingsWindow(app){Width=1000,Height=785};
        settingsWindow.Show();Diagnostics.NavigateSettings(settingsWindow,"Appearance");
        try
        {
            await Task.Delay(150);
            var controls=Diagnostics.LogicalDescendants(settingsWindow).OfType<FrameworkElement>();
            check("Settings exposes independent background opacity, palette and picture controls",
                new[]{"Panel background opacity","Separate opacity for closed dock","Closed dock background opacity","Panel gradient type","First gradient position","Second gradient position","Panel background image"}
                    .All(name=>controls.Any(c=>UI.Identity(c)==name)));
        }
        finally{settingsWindow.Close();}
        s.Background="#212339";s.Preset="Mac";s.PanelWidth=526;s.PanelPadding=32;
        options=s.PanelBackdrop;options.Image="No image";options.Gradient="Two soft lights";options.Opacity=.89;
        options.FirstColor="#000000";options.SecondColor="#C73C7D";options.FirstPosition="Top";options.SecondPosition="Bottom right";options.Strength=.36;
        app.ChangeSettings();island.Reveal(false);island.SelectPage("notes",false);await Settle();
        SavePanel("outlined-notes");
        island.Collapse();island.PreviewActivity();island.SelectPeek("awake");await Settle();
        SavePanel("outlined-mini-switch");
        var duration=Diagnostics.Descendants(island).OfType<ComboBox>().Single(box=>box.IsVisible);
        duration.IsDropDownOpen=true;await Settle();
        var durationMenu=(Popup)duration.Template.FindName("PART_Popup",duration);
        Save("outlined-duration-menu",Capture((Border)durationMenu.Child));
        duration.IsDropDownOpen=false;
        island.SelectPeek("volume");await Settle();SavePanel("outlined-mini-slider");
        island.Reveal(false);island.SelectPage("calendar",false);await Settle();
        s.ReduceMotion=false;s.InteractionMotion="Expressive";Motion.Configure(s);
        var month=CalendarButton("Toggle calendar month");var before=ButtonBounds(month);
        month.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0){RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent});
        await Task.Delay(280);
        check("Hover preserves both edges of the calendar month button",before==ButtonBounds(month));
        s.ReduceMotion=true;s.ControlFill="#382D46";app.ChangeSettings();
        var ai=app.Assistant;ai.Options.Enabled=true;
        typeof(AssistantRuntime).GetField("state",Fields)!.SetValue(ai,AssistantState.Error);
        var missingModel=Localization.Text("Enter the model identifier supplied by your provider.");
        typeof(AssistantRuntime).GetProperty("Status",Fields)!.SetValue(ai,missingModel);
        island.OpenAssistant(false);await Settle();
        var view=island.IslandAssistantView!;
        var statusFrame=(Border)typeof(AssistantView).GetField("statusHost",Fields)!.GetValue(view)!;
        var status=Diagnostics.Descendants(statusFrame).OfType<TextBlock>().First();
        check("Inline assistant error uses the shared translucent frame and retains its error text",
            statusFrame.IsVisible && statusFrame.Style==view.FindResource("GlassFieldFrame") &&
            statusFrame.Background==app.FindResource("GlassFieldFill") && statusFrame.BorderThickness==new Thickness(1) &&
            status.Text==missingModel && status.Foreground==UI.Brush("#EAB3AD"));
        SavePanel("outlined-assistant-error");
        island.DiagnosticPointerPresent=false;
    }
}
