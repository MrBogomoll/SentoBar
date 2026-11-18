using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SentoBar;

public sealed partial class OverlayWindow
{
    private readonly List<(Button Button,string? Kind)> expandedDetachButtons=new();

    private Button CreateExpandedDetachButton(string? kind=null)
    {
        var button=Icons.Button("pushpin","Detach preview",()=>DetachExpandedWidget(kind??WidgetForPage(activePage)),size:24);
        if(button.Content is FrameworkElement glyph)glyph.Width=glyph.Height=16;
        button.Padding=new(2);
        button.HorizontalContentAlignment=HorizontalAlignment.Center;
        button.VerticalAlignment=VerticalAlignment.Center;
        button.Visibility=Visibility.Collapsed;
        expandedDetachButtons.Add((button,kind));
        return button;
    }

    private DockPanel WithExpandedDetachButton(FrameworkElement heading,string kind)
    {
        var row=new DockPanel();
        var button=CreateExpandedDetachButton(kind);
        button.Margin=new(4,0,0,0);
        DockPanel.SetDock(button,Dock.Right);
        row.Children.Add(button);row.Children.Add(heading);
        return row;
    }

    private void RefreshExpandedDetachButtons()
    {
        foreach(var (button,kind) in expandedDetachButtons)
            button.Visibility=app.Previews?.CanOpen(kind??WidgetForPage(activePage))==true?Visibility.Visible:Visibility.Collapsed;
    }

    private void DetachExpandedWidget(string kind)
    {
        if(!expanded || app.Previews?.CanOpen(kind)!=true)return;
        Collapse();
        app.Previews.Open(kind);
    }
}
