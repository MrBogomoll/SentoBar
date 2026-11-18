using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SentoBar;

internal sealed class SystemPeekView : UserControl
{
    private readonly TextBlock compact=UI.Text("",13);
    private readonly SystemMonitorDashboard dashboard=new(compact:true);
    private readonly ScrollViewer detail;

    internal SystemPeekView(string title)
    {
        var grid=new Grid();
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        grid.RowDefinitions.Add(new());
        var heading=UI.Heading(title,13);
        heading.Margin=new(0,0,0,7);
        grid.Children.Add(heading);

        compact.TextWrapping=TextWrapping.NoWrap;
        compact.TextTrimming=TextTrimming.CharacterEllipsis;
        compact.VerticalAlignment=VerticalAlignment.Top;
        Grid.SetRow(compact,1);
        grid.Children.Add(compact);

        detail=UI.Scroll(dashboard);
        UI.LocalizedName(detail,"System details · scroll or use Page Down");
        Grid.SetRow(detail,1);
        grid.Children.Add(detail);
        Content=grid;
    }

    internal void SetCompactLayout(bool value)
    {
        compact.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        detail.Visibility=value?Visibility.Collapsed:Visibility.Visible;
    }

    internal void Refresh(Settings settings,(double cpu,double used,double total) usage,
        MonitorSnapshot? current,MonitorSnapshot? previous,IEnumerable<MonitorHistorySample> history)
    {
        if(detail.Visibility==Visibility.Visible)dashboard.Refresh(settings,current,previous,history);
        var display=settings.SystemDisplay;
        string cpu=double.IsFinite(usage.cpu)?$"CPU  {usage.cpu:0}%":Localization.Text("CPU unavailable");
        string memory=usage.total>0?Localization.Format("RAM  {0:0.0} / {1:0.0} GB",usage.used,usage.total):Localization.Text("RAM unavailable");
        string summary=string.Join("  ·  ",new[]{display.Cpu?cpu:null,display.Memory?memory:null}.OfType<string>());
        if(summary.Length==0)
        {
            var group=SystemDisplayRows.Extra(display,current,previous).FirstOrDefault();
            var row=group.Rows?.FirstOrDefault();
            summary=row==null?Localization.Text("No system readings selected"):(row.Name.Length>0?row.Name:group.Heading)+"  ·  "+row.Value;
        }
        compact.Text=summary;
    }
}
