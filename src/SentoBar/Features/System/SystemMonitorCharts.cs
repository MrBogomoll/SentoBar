using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SentoBar;

internal readonly record struct MonitorHistorySample(long Timestamp,MonitorSnapshot Current,MonitorSnapshot? Previous)
{
    internal const int DurationSeconds=60;

    internal double? Reading(MonitorMetric metric,string? deviceId)
    {
        bool Matches(string id)=>StringComparer.OrdinalIgnoreCase.Equals(id,deviceId);
        switch(metric)
        {
            case MonitorMetric.Cpu:
                return SystemMonitor.Cpu(Current,Previous);
            case MonitorMetric.Memory:
                return Current.Total is ulong total && Current.Available is ulong free?Occupied(free,total):null;
            case MonitorMetric.Gpu:
                return Current.Hardware?.Gpus.FirstOrDefault(gpu=>Matches(gpu.DeviceId))?.Utilization;
            case MonitorMetric.Storage:
                var drive=Current.Drives.FirstOrDefault(disk=>Matches(disk.DeviceId));
                return drive==null?null:SystemMonitor.DiskActivity(drive,Previous);
            case MonitorMetric.Network:
                var network=Current.Networks.FirstOrDefault(adapter=>Matches(adapter.Id));
                if(network==null)return null;
                var (received,sent)=SystemDisplayRows.Rates(Current,Previous,network);
                return received+sent;
            default:
                return null;
        }
    }

    private static double? Occupied(double free,double total)=>total>0 && free>=0 && free<=total
        ?100*(total-free)/total:null;
}

internal sealed class MonitorHistoryChart : Grid
{
    private readonly TextBlock maximumLabel=UI.Text("",10);
    private readonly TextBlock minimumLabel=UI.Text("",10);
    private readonly Plot plot=new();

    internal MonitorHistoryChart(bool compact=false)
    {
        MinHeight=(compact?30:50)*Accessibility.TextScale;
        Margin=new(0,compact?0:2,0,compact?2:8);
        ColumnDefinitions.Add(new());
        ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        double inset=7*Accessibility.TextScale;
        plot.Margin=new(0,inset,8,inset);
        Children.Add(plot);

        var labels=new Grid{MinWidth=30};
        maximumLabel.VerticalAlignment=VerticalAlignment.Top;
        minimumLabel.VerticalAlignment=VerticalAlignment.Bottom;
        foreach(var label in new[]{maximumLabel,minimumLabel})
        {
            label.HorizontalAlignment=HorizontalAlignment.Right;
            label.TextWrapping=TextWrapping.NoWrap;
            labels.Children.Add(label);
        }
        SetColumn(labels,1);
        Children.Add(labels);
    }

    internal void SetHistory(IEnumerable<MonitorHistorySample> history,MonitorMetric metric,string? deviceId,Brush line)
    {
        var points=history.Select(sample=>(sample.Timestamp,Value:sample.Reading(metric,deviceId))).ToArray();
        bool network=metric==MonitorMetric.Network;
        double ceiling=network?Math.Pow(2,Math.Ceiling(Math.Log2(Math.Max(1024,points.Select(point=>point.Value??0).DefaultIfEmpty().Max())))):100;
        maximumLabel.Text=network?SystemMonitor.RateText(ceiling):"100%";
        minimumLabel.Text=network?SystemMonitor.RateText(0):"0%";
        plot.Update(points,ceiling,line);
    }

    private sealed class Plot : FrameworkElement
    {
        private static readonly Brush Guide=UI.Brush("#4CFFFFFF");
        private (long Timestamp,double? Value)[] points=Array.Empty<(long,double?)>();
        private double maximum=100;
        private Brush line=Brushes.White;
        internal void Update((long Timestamp,double? Value)[] values,double ceiling,Brush brush)
        {
            points=values;
            maximum=ceiling;
            line=brush;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if(ActualWidth<=2 || ActualHeight<=2)return;
            double right=ActualWidth-1,bottom=ActualHeight-1;
            var grid=new Pen(Accessibility.HighContrast?SystemColors.WindowTextBrush:Guide,1);
            dc.DrawLine(grid,new(1,1),new(right,1));
            dc.DrawLine(grid,new(1,bottom),new(right,bottom));
            if(points.Length==0)return;

            var path=new StreamGeometry();
            long? previous=null;
            long now=points[^1].Timestamp;
            using(var draw=path.Open())
            {
                foreach(var (timestamp,value) in points)
                {
                    if(value is not double number || !double.IsFinite(number)){previous=null;continue;}
                    double age=Stopwatch.GetElapsedTime(timestamp,now).TotalSeconds;
                    var point=new Point(right-(right-1)*Math.Clamp(age/MonitorHistorySample.DurationSeconds,0,1),
                        bottom-(bottom-1)*Math.Clamp(number/maximum,0,1));
                    if(previous==null || Stopwatch.GetElapsedTime(previous.Value,timestamp).TotalSeconds>6)
                    {
                        draw.BeginFigure(point,false,false);
                        dc.DrawEllipse(line,null,point,1,1);
                    }
                    else draw.LineTo(point,true,false);
                    previous=timestamp;
                }
            }
            path.Freeze();
            dc.DrawGeometry(null,new Pen(line,1.6),path);
        }
    }
}
