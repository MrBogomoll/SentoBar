using System;
using System.IO;
using System.Linq;

namespace NabyScape;

// Timeline availability, sparse-position interpolation, Auto source following, browser exclusion
// (ported from PR #7) and output-capture diagnostics. No media session or audio device is opened.
internal static class MediaPlaybackTests
{
    private static void Assert(bool value,string reason="Media playback assertion failed."){if(!value)throw new Exception(reason);}
    internal static void Run(Action<string,Action> test)
    {
        test("Timeline availability hides unknown durations and separates read-only progress from seeking",()=>
        {
            foreach(double duration in new[]{0,-5,double.NaN,double.PositiveInfinity})Assert(MediaTimelinePolicy.State(duration,true)==MediaTimelineState.None && MediaTimelinePolicy.State(duration,false)==MediaTimelineState.None);
            Assert(MediaTimelinePolicy.State(180,false)==MediaTimelineState.ReadOnly && MediaTimelinePolicy.State(180,true)==MediaTimelineState.Seekable);
            using var media=new MediaService(ex=>throw ex){DiagnosticFixture=true};media.DiagnosticPlayback(true);Assert(media.TimelineState==MediaTimelineState.Seekable);
            media.SelectNone();Assert(media.TimelineState==MediaTimelineState.None && media.Timeline()==(0d,0d));
        });
        test("Timeline interpolates sparse player updates and survives a missing update stamp",()=>
        {
            var t0=new DateTimeOffset(2026,11,14,12,0,0,TimeSpan.Zero);var start=TimeSpan.Zero;var end=TimeSpan.FromSeconds(200);var clock=new MediaTimelineClock();
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(10),t0,true,t0.AddSeconds(5))==(15d,200d),"Fresh stamp did not advance.");
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(10),t0,true,t0.AddSeconds(30)).elapsed==40,"Sparse snapshot did not keep advancing.");
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(10),t0,false,t0.AddSeconds(31)).elapsed==40,"Pause without a new snapshot jumped back.");
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(42),t0.AddSeconds(31),false,t0.AddSeconds(32)).elapsed==42,"A published pause position was ignored.");
            var missing=new MediaTimelineClock();
            Assert(missing.Sample(start,end,TimeSpan.FromSeconds(20),default,true,t0).elapsed==20 && missing.Sample(start,end,TimeSpan.FromSeconds(20),default,true,t0.AddSeconds(3)).elapsed==23,"Unset stamp did not interpolate locally.");
            Assert(new MediaTimelineClock().Sample(start,end,TimeSpan.FromSeconds(20),t0.AddHours(1),true,t0).elapsed==20,"Future stamp moved the position.");
            Assert(clock.Sample(start,end,TimeSpan.FromSeconds(190),t0,true,t0.AddSeconds(60)).elapsed==200,"Position exceeded the duration.");
            Assert(new MediaTimelineClock().Sample(TimeSpan.FromSeconds(100),TimeSpan.FromSeconds(160),TimeSpan.FromSeconds(130),t0,false,t0)==(30d,60d),"Start offset was not applied.");
            Assert(new MediaTimelineClock().Sample(TimeSpan.Zero,TimeSpan.Zero,TimeSpan.Zero,t0,true,t0.AddSeconds(9))==(0d,0d),"A player without a timeline reported progress.");
        });
        test("Output capture failures are described, retried with backoff and formats validated",()=>
        {
            Exception Hr(uint code)=>new System.Runtime.InteropServices.COMException("fixture",unchecked((int)code));
            Assert(AudioCaptureDiagnostics.Describe(Hr(0x8889000A)).StartsWith("another app is using the output in exclusive mode") && AudioCaptureDiagnostics.Describe(Hr(0x8889000A)).EndsWith("(0x8889000A)"));
            Assert(AudioCaptureDiagnostics.Describe(Hr(0x88890004)).Contains("changed or was removed") && AudioCaptureDiagnostics.Describe(Hr(0x88890010)).Contains("audio service"));
            Assert(AudioCaptureDiagnostics.Describe(new NotSupportedException()).Contains("sample format") && AudioCaptureDiagnostics.Describe(new TimeoutException()).Contains("no samples") && AudioCaptureDiagnostics.Describe(new InvalidOperationException()).StartsWith("output capture could not start"));
            Assert(AudioCaptureDiagnostics.RetryDelay(1)==5000 && AudioCaptureDiagnostics.RetryDelay(2)==10000 && AudioCaptureDiagnostics.RetryDelay(5)==60000 && AudioCaptureDiagnostics.RetryDelay(50)==60000);
            Assert(AudioCaptureDiagnostics.SupportedFormat(32,2,true) && AudioCaptureDiagnostics.SupportedFormat(16,2,false) && AudioCaptureDiagnostics.SupportedFormat(24,6,false) && AudioCaptureDiagnostics.SupportedFormat(32,8,false));
            Assert(!AudioCaptureDiagnostics.SupportedFormat(64,2,true) && !AudioCaptureDiagnostics.SupportedFormat(16,2,true) && !AudioCaptureDiagnostics.SupportedFormat(8,2,false) && !AudioCaptureDiagnostics.SupportedFormat(16,0,false));
        });
        test("Packaged players match their audio session by package family",()=>
        {
            Assert(AudioService.MediaPackageMatches("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic","microsoft.zunemusic_8wekyb3d8bbwe"));
            Assert(!AudioService.MediaPackageMatches("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic","SpotifyAB.SpotifyMusic_zpdnekdrzrea0") && !AudioService.MediaPackageMatches("Spotify.exe","Spotify.exe") && !AudioService.MediaPackageMatches("A_1!App",null) && !AudioService.MediaPackageMatches("!App","x"));
            Assert(PackageIdentity.FamilyName(Environment.ProcessId)==null,"An unpackaged test process reported a package family.");
            using var audio=new AudioService(_=>{});Assert(!audio.MediaMeterFallback && audio.SpectrumStatus==null);
        });
    }
}
