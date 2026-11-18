using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

internal static class LauncherPresentation
{
    internal static void ApplyHeadingTypography(DependencyObject node,bool bold)
    {
        if(node is TextBlock text && UI.IsHeading(text))text.FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal;
        foreach(var child in LogicalTreeHelper.GetChildren(node))if(child is DependencyObject element)ApplyHeadingTypography(element,bold);
    }
    internal static void ApplyMediaTypography(TextBlock current,TextBlock? outgoing,bool bold)
    {current.FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal;if(outgoing!=null)outgoing.FontWeight=current.FontWeight;}
    internal static bool ShowIcon(string mode)=>mode!="Text";
    internal static bool ShowLabel(string mode)=>mode!="Icons";
    internal static string CategoryIcon(string page)=>page switch
    {
        "media"=>"nav-media","timer"=>"nav-focus","focus"=>"focus","stopwatch"=>"timer",
        "voice"=>"mic","volume" or "mixer"=>"volume","clock"=>"clock","battery"=>"battery",
        "system" or "status"=>"nav-status","tools"=>"nav-tools","files"=>"folder","notes"=>"notes",
        "tasks"=>"nav-tasks","clips"=>"clipboard","calendar"=>"calendar","weather"=>"weather",
        "awake"=>"coffee","power"=>"nav-power",_=>"nav-launch"
    };
}
public sealed partial class OverlayWindow
{
    private Button LaunchButton(Shortcut shortcut)
    {
        var button=UI.Button(shortcut.Name,()=>{try{ShortcutService.Open(shortcut);Collapse();}catch(Exception ex){app.Notify(Localization.Text("Could not open shortcut"),ex.Message);}});
        button.ToolTip=shortcut.Name+"\n"+shortcut.Target;button.MinHeight=44;button.Padding=new(9);button.Margin=new(0,0,10,10);button.Background=Brushes.Transparent;button.BorderThickness=new(0);
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        ShortcutIcon? icon=null;
        if(LauncherPresentation.ShowIcon(app.Settings.LaunchStyle)){icon=new ShortcutIcon(shortcut){Margin=new(0,0,LauncherPresentation.ShowLabel(app.Settings.LaunchStyle)?7:0,0)};row.Children.Add(icon);}
        var label=UI.Text(shortcut.Name,11,UI.ContrastText(app.Settings.ControlFill));label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxWidth=115;row.Children.Add(label);
        if(icon!=null && !LauncherPresentation.ShowLabel(app.Settings.LaunchStyle))icon.AvailabilityChanged+=available=>label.Visibility=available?Visibility.Collapsed:Visibility.Visible;
        button.Content=row;return button;
    }
    private (bool Media,bool Summary,bool Headings,bool Motion)? titleAppearance;
    private bool? bodyHeadingWeight;
    private ScrollViewer? compactNavScroll;
    private void StyleCategory(Button button,string page,bool selected=false)
    {
        string mode=app.Settings.PanelNavigationStyle;
        double width=compactNavScroll?.ActualWidth??0;
        if(compactNavScroll?.Content is StackPanel strip)
            strip.HorizontalAlignment=mode=="Labels"?HorizontalAlignment.Left:HorizontalAlignment.Center;
        double slot=width>0?Math.Clamp(width/Math.Max(1,compactTabs.Count),2,18):18;
        StyleNavigationTab(button,PageName(page),page,selected,mode,slot,28);
    }

    internal static void StyleNavigationTab(Button button,string name,string page,bool selected,string mode,double dotWidth,double height)
    {
        bool dots=mode=="Dots",icons=mode=="Icons";
        var normalText=Accessibility.HighContrast?SystemColors.WindowTextBrush:GlassDockAppearance.Quiet;
        var activeText=Accessibility.HighContrast?SystemColors.HighlightTextBrush:GlassDockAppearance.Foreground;
        button.BorderThickness=new(0);
        button.Background=!dots && selected?(Accessibility.HighContrast?SystemColors.HighlightBrush:GlassDockAppearance.Selected):Brushes.Transparent;
        button.Foreground=selected && !dots?activeText:normalText;
        button.Height=button.MinHeight=height;
        button.Width=dots?dotWidth:icons?height:double.NaN;
        if(dots || icons)button.MaxWidth=double.PositiveInfinity;
        button.Padding=dots || icons?new Thickness(0):new Thickness(height==22?7:10,2,height==22?7:10,2);
        button.Margin=new(0,0,mode=="Labels"?4:0,0);
        button.HorizontalContentAlignment=HorizontalAlignment.Center;
        button.VerticalAlignment=VerticalAlignment.Center;
        button.FontSize=11;
        button.Tag=new CornerRadius(icons?7:height/2);
        if(dots)
        {
            double size=Math.Min(dotWidth*.62,4);
            var dot=button.Content as System.Windows.Shapes.Rectangle??new System.Windows.Shapes.Rectangle();
            dot.Width=selected?Math.Min(dotWidth*.92,11):size;
            dot.Height=size;dot.RadiusX=dot.RadiusY=size/2;
            dot.Fill=selected && !Accessibility.HighContrast?Brushes.White:normalText;
            dot.Opacity=selected || Accessibility.HighContrast?1:.55;
            button.Content=dot;
            button.Opacity=1;button.IsHitTestVisible=true;
        }
        else if(icons)
        {
            string icon=LauncherPresentation.CategoryIcon(page);
            if(button.Content is not FrameworkElement glyph || !Equals(glyph.Tag,icon))
            {
                glyph=Icons.Create(icon,height==22?13:15);
                glyph.Tag=icon;
                Icons.FollowForeground(glyph,button);
                button.Content=glyph;
            }
        }
        else
        {
            var label=button.Content as TextBlock??new TextBlock
            {
                TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,
                VerticalAlignment=VerticalAlignment.Center
            };
            label.Text=name;
            button.Content=label;
        }
        button.ToolTip=null;
        UI.Named(button,name);
        System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?Localization.Text("Selected"):"");
    }
    private static void KeepTabsWithinViewport(ScrollViewer viewport,StackPanel strip)
    {
        void Refresh()
        {
            double width=viewport.ViewportWidth;
            if(width<=0 || !double.IsFinite(width))return;
            double start=viewport.HorizontalOffset;
            foreach(var child in strip.Children)
            {
                if(child is not Button tab)continue;
                tab.MaxWidth=tab.Content is TextBlock?width:double.PositiveInfinity;
                double left=tab.TranslatePoint(new Point(),strip).X;
                bool entirelyVisible=tab.Content is System.Windows.Shapes.Rectangle || left>=start-.5 && left+tab.ActualWidth<=start+width+.5;
                tab.Opacity=entirelyVisible?1:0;
                tab.IsHitTestVisible=entirelyVisible;
            }
        }
        viewport.Loaded+=(_,_)=>Refresh();
        viewport.ScrollChanged+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,viewport))Refresh();};
    }

    private void ApplyTitleTypography(bool mediaSummary)
    {
        var key=(app.Settings.BoldMediaTitle,mediaSummary,app.Settings.BoldHeadings,Motion.Enabled);if(titleAppearance==key)return;titleAppearance=key;
        if(!Motion.Enabled)foreach(var label in marqueeLabels.Values)label.Reset();
        ApplyMediaTypography(title,app.Settings.BoldMediaTitle);
        ApplyMediaTypography(peekTitle,app.Settings.BoldMediaTitle);
        ApplyMediaTypography(artist,false);
        ApplyMediaTypography(peekSubtitle,false);
        summary.FontWeight=mediaSummary && app.Settings.BoldMediaTitle?FontWeights.SemiBold:FontWeights.Normal;
        if(bodyHeadingWeight!=app.Settings.BoldHeadings)ApplyHeadingTypography(body);
    }
    private void ApplyMediaTypography(TextBlock current,bool bold)
    {previousText.TryGetValue(current,out var outgoing);LauncherPresentation.ApplyMediaTypography(current,outgoing,bold);}
    private void ApplyHeadingTypography(DependencyObject node){LauncherPresentation.ApplyHeadingTypography(node,app.Settings.BoldHeadings);if(ReferenceEquals(node,body))bodyHeadingWeight=app.Settings.BoldHeadings;}
}
