using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SentoBar;

internal sealed class SystemMonitorDashboard(bool compact=false) : StackPanel
{
    internal void Refresh(Settings settings,MonitorSnapshot? current,MonitorSnapshot? previous,IEnumerable<MonitorHistorySample> history)
    {
        var display=settings.SystemDisplay;
        var groups=SystemDisplayRows.Extra(display,current,previous,overview:true).ToArray();
        var ink=Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush(settings.Accent);
        string sampling=Localization.Text("Sampling…");
        int index=0;

        void Add(string name,string value="",MonitorMetric? metric=null,string? id=null,bool heading=false)
        {
            if(index==Children.Count)Children.Add(new Entry(compact));
            var entry=(Entry)Children[index++];
            entry.Update(name,value,heading);
            entry.SetChart(metric is {} key && display.ShowChart(key,id)?metric:null,id,history,ink);
        }

        if(!compact && (display.Cpu && display.ShowChart(MonitorMetric.Cpu) ||
            display.Memory && display.ShowChart(MonitorMetric.Memory) ||
            groups.Any(group=>group.Rows.Any(row=>row.DeviceId is {} id && display.ShowChart(group.Metric,id)))))
            Add(Localization.Text("Last 60 seconds · sampled every 2 seconds"));
        if(display.Cpu)
        {
            double? load=current==null?null:SystemMonitor.Cpu(current,previous);
            Add("CPU",load is double cpu?$"{cpu:0.#}%":sampling,MonitorMetric.Cpu);
        }
        if(display.Memory)
        {
            string used=current is {Total:ulong total,Available:ulong available} && available<=total
                ?SystemDisplayRows.UsedCapacity(total-available,total)
                :current==null?sampling:Localization.Text("Unavailable");
            Add(Localization.Text("Memory"),used,MonitorMetric.Memory);
        }
        foreach(var group in groups)
        {
            Add(group.Heading,heading:true);
            foreach(var row in group.Rows)
                Add(row.Name.Length>0?row.Name:row.Value,row.Name.Length>0?row.Value:"",
                    row.DeviceId==null?null:group.Metric,row.DeviceId);
        }
        if(index==0)Add(Localization.Text("No system readings selected"));
        if(Children.Count>index)Children.RemoveRange(index,Children.Count-index);
    }

    private sealed class Entry : StackPanel
    {
        private readonly TextBlock name=UI.Text("",11,UI.Muted);
        private readonly TextBlock value=UI.Text("",11);
        private readonly bool compact;
        private MonitorHistoryChart? chart;

        internal Entry(bool compact)
        {
            this.compact=compact;
            var row=new Grid();
            row.ColumnDefinitions.Add(new());
            row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            name.TextWrapping=value.TextWrapping=TextWrapping.NoWrap;
            name.TextTrimming=value.TextTrimming=TextTrimming.CharacterEllipsis;
            name.Margin=new(0,0,4,0);
            Grid.SetColumn(value,1);
            row.Children.Add(name);
            row.Children.Add(value);
            SizeChanged+=(_,e)=>value.MaxWidth=e.NewSize.Width*.62;
            Children.Add(row);
        }

        internal void Update(string label,string reading,bool heading)
        {
            name.Text=label;
            name.ToolTip=label;
            name.FontWeight=heading?FontWeights.SemiBold:FontWeights.Normal;
            value.Text=reading;
            value.ToolTip=reading;
            Margin=heading?new(0,compact?4:12,0,compact?1:6):new(0,1,0,2);
        }

        internal void SetChart(MonitorMetric? metric,string? id,IEnumerable<MonitorHistorySample> history,Brush ink)
        {
            if(metric==null)
            {
                if(chart!=null)chart.Visibility=Visibility.Collapsed;
                return;
            }
            if(chart==null)
            {
                chart=new(compact);
                Children.Add(chart);
            }
            chart.Visibility=Visibility.Visible;
            chart.SetHistory(history,metric.Value,id,ink);
        }
    }
}
