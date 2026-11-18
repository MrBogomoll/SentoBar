using System.Windows;
using System.Windows.Controls;

namespace SentoBar;

internal sealed class SystemSummaryExtras : StackPanel
{
    internal void Refresh(SystemDisplayPreferences settings,MonitorSnapshot? current,MonitorSnapshot? previous)
    {
        int index=0;
        void SetLine(string text,bool heading)
        {
            if(index==Children.Count)Children.Add(UI.Text("",11));
            var line=(TextBlock)Children[index++];
            line.Text=text;
            line.FontWeight=heading?FontWeights.SemiBold:FontWeights.Normal;
            line.Margin=heading?new(0,8,0,3):new(0,1,0,3);
        }
        foreach(var group in SystemDisplayRows.Extra(settings,current,previous))
        {
            SetLine(group.Heading,true);
            foreach(var row in group.Rows)SetLine(row.Name.Length>0?row.Name+"  ·  "+row.Value:row.Value,false);
        }
        if(index==0 && !settings.Cpu && !settings.Memory)SetLine(Localization.Text("No system readings selected"),false);
        if(Children.Count>index)Children.RemoveRange(index,Children.Count-index);
    }
}
