using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

internal static class GlassDockAppearance
{
    internal static readonly SolidColorBrush Foreground=UI.Brush("#F4F7FC");
    internal static readonly SolidColorBrush Quiet=UI.Brush("#BEC8D7");
    internal static readonly SolidColorBrush Stroke=UI.Brush("#3E4B5F");
    internal static readonly SolidColorBrush Control=UI.Brush("#222A37");
    internal static readonly SolidColorBrush Selected=UI.Brush("#303B4D");
    internal static readonly SolidColorBrush Accent=UI.Brush("#83B5E9");
    internal static readonly SolidColorBrush AccentText=UI.Brush("#111827");
    internal static readonly Pen Edge=CreateEdge();

    internal const double ActionGap=6;

    internal static StackPanel ActionRow(params Button[] actions)
    {
        for(int index=0;index<actions.Length;index++)
        {
            var margin=actions[index].Margin;
            actions[index].Margin=new Thickness(0,margin.Top,index+1<actions.Length?ActionGap:0,margin.Bottom);
        }
        return UI.Row(actions);
    }

    internal static void ApplyControls(FrameworkElement host)
    {
        if(Accessibility.HighContrast)return;
        host.Resources[typeof(TextBox)]=host.FindResource("GlassPanelInput");
        host.Resources[typeof(PasswordBox)]=host.FindResource("GlassPanelPassword");
        host.Resources[typeof(ComboBox)]=host.FindResource("GlassPanelChoice");
        host.Resources[typeof(Button)]=host.FindResource("GlassPanelButton");
    }

    internal static void UpdateControlColors(Settings settings)
    {
        string fill=settings.ControlFill.Equals("#16171B",StringComparison.OrdinalIgnoreCase)?"#263244":settings.ControlFill;
        string border=settings.ControlBorder.Equals("#303238",StringComparison.OrdinalIgnoreCase)?"#536781":settings.ControlBorder;
        UI.ResourceBrush("GlassControlFill",Accessibility.HighContrast?SystemColors.WindowBrush:UI.Brush(fill));
        UI.ResourceBrush("GlassControlBorder",Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush(border));
        UI.ResourceBrush("GlassControlText",Accessibility.HighContrast?SystemColors.WindowTextBrush:fill=="#263244"?Foreground:UI.ContrastText(fill));

        var panel=UI.Brush(settings.Background).Color;
        var tint=UI.Brush(fill).Color;
        if(settings.ControlFill.Equals("#16171B",StringComparison.OrdinalIgnoreCase))tint=Blend(tint,panel,.4);
        var ink=(SolidColorBrush)UI.ContrastText(Blend(panel,tint,.83).ToString());
        var outline=UI.Brush(border).Color;
        var surface=new SolidColorBrush(Color.FromArgb(212,tint.R,tint.G,tint.B));
        surface.Freeze();
        UI.ResourceBrush("GlassFieldFill",Accessibility.HighContrast?SystemColors.WindowBrush:surface);
        UI.ResourceBrush("GlassFieldText",Accessibility.HighContrast?SystemColors.WindowTextBrush:ink);
        UI.ResourceBrush("GlassFieldHover",Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush(Blend(outline,ink.Color,.3).ToString()));
        UI.ResourceBrush("GlassFieldFocus",Accessibility.HighContrast?SystemColors.HighlightBrush:UI.Brush(Blend(outline,ink.Color,.72).ToString()));
    }

    internal static double Radius(double radius)=>radius>0?radius+10:0;

    internal static LinearGradientBrush Material(Color background,Color light)
    {
        bool standard=background==Colors.Black;
        var top=standard?Color.FromRgb(0x15,0x1a,0x23):background;
        var baseColor=standard?Color.FromRgb(0x17,0x1c,0x25):background;
        Color bottom=light.A==0?baseColor:Blend(baseColor,light,HasColor(light)?.13:.045);
        var brush=new LinearGradientBrush(new GradientStopCollection{new(top,0),new(top,.28),new(bottom,1)},new Point(.5,0),new Point(.5,1));
        brush.Freeze();
        return brush;
    }

    internal static double GlowOpacity(Color light)=>HasColor(light)?.26:.11;

    private static bool HasColor(Color light)
        =>Math.Max(light.R,Math.Max(light.G,light.B))-Math.Min(light.R,Math.Min(light.G,light.B))>=20;

    private static Color Blend(Color baseColor,Color light,double weight)=>Color.FromRgb(
        (byte)(baseColor.R*(1-weight)+light.R*weight),
        (byte)(baseColor.G*(1-weight)+light.G*weight),
        (byte)(baseColor.B*(1-weight)+light.B*weight));

    private static Pen CreateEdge()
    {
        var pen=new Pen(Stroke,1);
        pen.Freeze();
        return pen;
    }
}
