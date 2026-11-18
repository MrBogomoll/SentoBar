using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private FrameworkElement PanelBackdropEditor()
    {
        var options=app.Settings.PanelBackdrop;
        var editor=new StackPanel();
        var details=new StackPanel{Visibility=options.Enabled?Visibility.Visible:Visibility.Collapsed};
        editor.Children.Add(Check("Custom panel background",options.Enabled,value=>
        {
            options.Enabled=value;
            details.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        }));
        editor.Children.Add(details);
        details.Children.Add(UI.Caption("Shares the background across the closed dock, mini panel, expanded panel and detached windows. The base color comes from Island color above.",11,UI.Muted));
        AddSlider(details,"Panel background opacity",options.Opacity*100,0,100,value=>options.Opacity=value/100,"%");

        var closedOpacity=new StackPanel{Visibility=app.Settings.UseClosedBackgroundOpacity?Visibility.Visible:Visibility.Collapsed};
        editor.Children.Add(Check("Separate opacity for closed dock",app.Settings.UseClosedBackgroundOpacity,value=>
        {
            app.Settings.UseClosedBackgroundOpacity=value;
            closedOpacity.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        }));
        AddSlider(closedOpacity,"Closed dock background opacity",app.Settings.ClosedBackgroundOpacity*100,0,100,
            value=>app.Settings.ClosedBackgroundOpacity=value/100,"%");
        editor.Children.Add(closedOpacity);

        var first=new StackPanel();
        var second=new StackPanel();
        details.Children.Add(PremiumChoice("Panel gradient type",options.Gradient,PanelBackdropPreferences.Gradients,value=>
        {
            options.Gradient=value;
            ShowGradientFields();
        }));
        AddSlider(first,"Panel gradient intensity",options.Strength*100,0,100,value=>options.Strength=value/100,"%");
        ColorControl(first,"First gradient color",options.FirstColor,value=>options.FirstColor=value,new[]{"#345F9E","#6773B8","#40968E","#AD8930"});
        first.Children.Add(PremiumChoice("First gradient position",options.FirstPosition,PanelBackdropPreferences.Positions,value=>options.FirstPosition=value));
        ColorControl(second,"Second gradient color",options.SecondColor,value=>options.SecondColor=value,new[]{"#AD8930","#B45C85","#5665AA","#3D8F9F"});
        second.Children.Add(PremiumChoice("Second gradient position",options.SecondPosition,PanelBackdropPreferences.Positions,value=>options.SecondPosition=value));
        details.Children.Add(first);details.Children.Add(second);
        void ShowGradientFields()
        {
            first.Visibility=options.Gradient=="No gradient"?Visibility.Collapsed:Visibility.Visible;
            second.Visibility=options.Gradient is "Linear gradient" or "Two soft lights"?Visibility.Visible:Visibility.Collapsed;
        }
        ShowGradientFields();

        var imageControls=new StackPanel();
        var pictureControls=new StackPanel();
        var pictureStatus=UI.Caption(PanelBackdropRenderer.HasPicture(app.Store.DirectoryPath)?"Background picture saved":"No background picture selected",11,UI.Muted);
        pictureControls.Children.Add(UI.CaptionButton("Choose background picture…",()=>
        {
            var picker=new OpenFileDialog
            {
                Title=Localization.Text("Choose background picture…"),
                Filter=Localization.Text("Pictures (*.png, *.jpg, *.bmp)|*.png;*.jpg;*.jpeg;*.bmp"),
                CheckFileExists=true
            };
            if(picker.ShowDialog(this)!=true)return;
            try
            {
                PanelBackdropRenderer.ImportPicture(app.Store.DirectoryPath,picker.FileName);
                pictureStatus.Text=Localization.Text("Background picture saved");
                Save();
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
            {
                pictureStatus.Text=Localization.Text("Could not load the background picture.");
            }
        }));
        pictureControls.Children.Add(pictureStatus);
        details.Children.Add(PremiumChoice("Panel background image",options.Image,PanelBackdropPreferences.Images,value=>
        {
            options.Image=value;
            ShowImageFields();
        }));
        details.Children.Add(pictureControls);
        AddSlider(imageControls,"Background image opacity",options.ImageOpacity*100,0,100,value=>options.ImageOpacity=value/100,"%");
        AddSlider(imageControls,"Background image blur",options.Blur,0,32,value=>options.Blur=value,"px",1);
        details.Children.Add(imageControls);
        void ShowImageFields()
        {
            pictureControls.Visibility=options.Image=="Custom picture"?Visibility.Visible:Visibility.Collapsed;
            imageControls.Visibility=options.Image=="No image"?Visibility.Collapsed:Visibility.Visible;
        }
        ShowImageFields();
        return editor;
    }
}
