using System;

namespace SentoBar;

// What the dock may do with a session's timeline. Some players (for example Telegram Desktop)
// publish play/pause through Windows but no duration, so a 0:00 / 0:00 slider would be dead.
internal enum MediaTimelineState { None, ReadOnly, Seekable }

internal static class MediaTimelinePolicy
{
    internal static MediaTimelineState State(double duration,bool canSeek)=>double.IsFinite(duration) && duration>0?canSeek?MediaTimelineState.Seekable:MediaTimelineState.ReadOnly:MediaTimelineState.None;

    internal static string? Help(MediaTimelineState state,bool rejected)=>state switch
    {
        MediaTimelineState.ReadOnly=>Localization.Text("This player doesn't allow seeking"),
        MediaTimelineState.Seekable when rejected=>Localization.Text("Could not seek in the current track"),
        _=>null
    };

    internal static TimeSpan? SeekPosition(TimeSpan start,TimeSpan end,TimeSpan min,TimeSpan max,double fraction)
    {
        if(end<=start || !double.IsFinite(fraction))return null;
        double amount=Math.Clamp(fraction,0,1);
        long point=start.Ticks+(long)((end-start).Ticks*amount);
        // A zero range can mean the provider omitted seek bounds entirely.
        long lower=Math.Max(start.Ticks,min.Ticks),upper=Math.Min(end.Ticks,max.Ticks);
        return TimeSpan.FromTicks(upper>lower?Math.Clamp(point,lower,upper):point);
    }
}

// Players publish timeline snapshots at their own cadence, sometimes only on track or state
// changes. While playing, the position advances from the snapshot's LastUpdatedTime, or from
// when this snapshot was first seen when the player leaves that stamp unset or skewed. A player
// that does not publish its pause position keeps the last interpolated value instead of jumping back.
internal sealed class MediaTimelineClock
{
    private double raw=double.NaN,held=double.NaN;
    private DateTimeOffset stamp,observedAt;
    private DateTimeOffset? seekConfirmedAt;
    private bool seekWasPlaying;

    internal void Reset(){raw=held=double.NaN;stamp=observedAt=default;seekConfirmedAt=null;}

    internal void AcceptSeek(TimeSpan start,TimeSpan position,DateTimeOffset lastUpdated,TimeSpan target,bool playing,DateTimeOffset now)
    {
        raw=(position-start).TotalSeconds;
        stamp=lastUpdated;
        held=(target-start).TotalSeconds;
        seekConfirmedAt=observedAt=now;
        seekWasPlaying=playing;
    }
    internal (double elapsed,double duration) Sample(TimeSpan start,TimeSpan end,TimeSpan position,DateTimeOffset lastUpdated,bool playing,DateTimeOffset now)
    {
        double duration=(end-start).TotalSeconds;
        if(!double.IsFinite(duration) || duration<=0){Reset();return (0,0);}
        double snapshot=(position-start).TotalSeconds;if(!double.IsFinite(snapshot))snapshot=0;
        bool snapshotChanged=snapshot!=raw || lastUpdated!=stamp;
        if(seekConfirmedAt is DateTimeOffset confirmed)
        {
            double anchored=held+(playing && seekWasPlaying?Math.Max(0,(now-observedAt).TotalSeconds):0);
            // Windows can return the pre-seek snapshot after accepting the command.
            // Keep its confirmed target until the publisher reports a fresh position.
            if(!snapshotChanged || (now-confirmed).TotalSeconds<1 && Math.Abs(snapshot-anchored)>2)
            {
                observedAt=now;
                seekWasPlaying=playing;
                held=Math.Clamp(anchored,0,duration);
                return (held,duration);
            }
            seekConfirmedAt=null;
        }
        if(snapshotChanged){raw=snapshot;stamp=lastUpdated;observedAt=now;held=double.NaN;}
        double elapsed=raw;
        if(playing)
        {
            double age=(now-lastUpdated).TotalSeconds;
            if(!(age>=0 && age<86400))age=Math.Max(0,(now-observedAt).TotalSeconds);
            elapsed=held=raw+age;
        }
        else if(double.IsFinite(held))elapsed=held;
        return (Math.Clamp(elapsed,0,duration),duration);
    }
}
