using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SentoBar;

internal sealed class MediaSeekSlider : Slider
{
    private bool updatingPosition;
    private long revision, dragRevision;

    internal event Action<double,long>? SeekRequested;

    internal MediaSeekSlider()
    {
        SetResourceReference(StyleProperty,typeof(Slider));
        Minimum=0;
        Maximum=1;
        SmallChange=.01;
        LargeChange=.05;
    }

    internal void ShowPosition(double fraction,long trackRevision)
    {
        revision=trackRevision;
        if(IsMouseCaptured)
        {
            if(dragRevision==revision && IsEnabled && IsHitTestVisible)return;
            ReleaseMouseCapture();
        }
        SetDisplayedValue(fraction);
    }

    private void SetDisplayedValue(double value)
    {
        updatingPosition=true;
        try{SetCurrentValue(ValueProperty,value);}
        finally{updatingPosition=false;}
    }

    protected override void OnValueChanged(double oldValue,double newValue)
    {
        base.OnValueChanged(oldValue,newValue);
        if(!updatingPosition && !IsMouseCaptured && IsEnabled && IsHitTestVisible)
            SeekRequested?.Invoke(newValue,revision);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(!IsEnabled || PointerValue(e) is not double value)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }
        Focus();
        dragRevision=revision;
        if(CaptureMouse())SetDisplayedValue(value);
        else SetCurrentValue(ValueProperty,value);
        e.Handled=true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if(IsMouseCaptured && PointerValue(e) is double value)SetDisplayedValue(value);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if(!IsMouseCaptured)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            return;
        }
        double value=PointerValue(e)??Value;
        SetDisplayedValue(value);
        ReleaseMouseCapture();
        e.Handled=true;
        if(IsEnabled && IsHitTestVisible && dragRevision==revision)SeekRequested?.Invoke(value,dragRevision);
    }

    private double? PointerValue(MouseEventArgs e)
    {
        if(GetTemplateChild("PART_Track") is not Track track)return null;
        double thumbWidth=track.Thumb?.ActualWidth??0;
        double travel=track.ActualWidth-thumbWidth;
        if(travel<=0)return null;
        double fraction=Math.Clamp((e.GetPosition(track).X-thumbWidth/2)/travel,0,1);
        return track.IsDirectionReversed?1-fraction:fraction;
    }
}
