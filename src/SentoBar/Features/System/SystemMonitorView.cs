using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SentoBar;

public sealed partial class OverlayWindow
{
    private SystemSummaryExtras? classicSystemRows;
    private bool ClassicSystemCardVisible=>expanded && IsVisible && !commandOpen && !app.Settings.CompactLayout
        && classicSystemRows?.IsVisible==true && classicSystemRows.IsDescendantOf(body);

    private Border BuildClassicSystemCard()
    {
        cpuBar.Foreground=ramBar.Foreground=Brushes.White;
        cpuBar.Background=ramBar.Background=UI.Brush("#3A3A3C");
        var settings=SystemDisplaySettingsWindow.SettingsButton(()=>ShowSystemDisplaySettings(this),true);
        settings.HorizontalAlignment=HorizontalAlignment.Right;
        classicSystemRows=new();
        RefreshClassicSystemCard();
        return UI.Card(Localization.Text("System"),cpu,cpuBar,ram,ramBar,classicSystemRows,settings);
    }

    private void RefreshClassicSystemCard()
    {
        if(classicSystemRows==null)return;
        var display=app.Settings.SystemDisplay;
        cpu.Visibility=cpuBar.Visibility=display.Cpu?Visibility.Visible:Visibility.Collapsed;
        ram.Visibility=ramBar.Visibility=display.Memory?Visibility.Visible:Visibility.Collapsed;
        classicSystemRows.Refresh(display,monitorLatest,monitorPrevious);
    }

    private readonly List<UIElement> monitorLayout=new();
    private readonly List<Grid> monitorValueRows=new();
    private int monitorRowIndex;
    private readonly SystemMonitorDashboard monitorDashboard=new();
    private void BeginMonitorRender(){monitorLayout.Clear();monitorRowIndex=0;}
    private void AddMonitorElement(UIElement element)=>monitorLayout.Add(element);
    private Grid GetMonitorRow()
    {
        if(monitorRowIndex<monitorValueRows.Count)return monitorValueRows[monitorRowIndex++];
        var row=new Grid{Margin=new(0,3,0,3)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var name=UI.Text("",11,UI.Muted);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new(0,0,8,0);row.Children.Add(name);
        var text=UI.Text("");text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(text,1);row.Children.Add(text);monitorValueRows.Add(row);monitorRowIndex++;return row;
    }
    private void CommitMonitorRender()
    {
        for(int i=0;i<monitorLayout.Count;i++)
        {
            var next=monitorLayout[i];
            if(i<monitorRows.Children.Count)
            {
                var old=monitorRows.Children[i];if(ReferenceEquals(old,next))continue;
                if(old is TextBlock previous && next is TextBlock text)
                {Motion.Text(previous,text.Text);previous.FontSize=text.FontSize;previous.FontWeight=text.FontWeight;previous.Foreground=text.Foreground;previous.Margin=text.Margin;previous.TextWrapping=text.TextWrapping;previous.TextTrimming=text.TextTrimming;previous.ToolTip=text.ToolTip;previous.Tag=text.Tag;continue;}
            }
            if(next is FrameworkElement {Parent:Panel owner})owner.Children.Remove(next);
            if(i<monitorRows.Children.Count)monitorRows.Children.RemoveAt(i);
            monitorRows.Children.Insert(i,next);
        }
        while(monitorRows.Children.Count>monitorLayout.Count)monitorRows.Children.RemoveAt(monitorRows.Children.Count-1);
        // A process/device list may shrink. Do not retain detached historical rows indefinitely.
        if(monitorValueRows.Count>monitorRowIndex)monitorValueRows.RemoveRange(monitorRowIndex,monitorValueRows.Count-monitorRowIndex);
        monitorLayout.Clear();
    }
}
