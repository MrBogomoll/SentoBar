using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.Wave;

namespace SentoBar;

internal sealed partial class LocalAssistantVoice
{
    internal static bool MatchesWakePhrase(string text,string phrase)
    {
        static string Words(string value)
        {
            string words=Regex.Replace(value.ToLowerInvariant().Replace('ё','е'),@"[^\p{L}]+"," ").Trim();
            return Regex.Replace(words,@"\b(?:sento|сента|сэнта|сэнто)\b","сенто");
        }
        return Words(text)==Words(phrase);
    }

    internal async Task ListenForWake(string phrase,string language,Action ready,CancellationToken token)
    {
        options.ManagedGpu=false;
        await PrepareCheck(true,_=>{},token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        using var mic=new WaveInEvent{DeviceNumber=AssistantMicrophones.Resolve(microphoneId),WaveFormat=AssistantAudioCheck.MicrophoneFormat,BufferMilliseconds=50};
        var phrases=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest});
        using var samples=new MemoryStream();
        int voiced=0,quiet=0;
        bool tooLong=false;
        double threshold=Math.Clamp(options.Sensitivity,.003,.15);
        mic.DataAvailable+=(_,e)=>
        {
            if(token.IsCancellationRequested)return;
            bool speech=AssistantVoiceAudio.Peak(e.Buffer,e.BytesRecorded)>=threshold;
            if(speech){voiced++;quiet=0;}
            else if(voiced>0)quiet+=e.BytesRecorded;
            if(voiced==0)return;
            if(samples.Length+e.BytesRecorded>32000*6)tooLong=true;
            if(!tooLong)samples.Write(e.Buffer,0,e.BytesRecorded);
            if(quiet<16000)return;
            if(!tooLong && voiced>=3)phrases.Writer.TryWrite(samples.ToArray());
            samples.SetLength(0);voiced=quiet=0;tooLong=false;
        };
        var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mic.RecordingStopped+=(_,e)=>{stopped.TrySetResult();phrases.Writer.TryComplete(e.Exception);};
        mic.StartRecording();
        try
        {
            ready();
            await foreach(byte[] pcm in phrases.Reader.ReadAllAsync(token).ConfigureAwait(false))
                if(MatchesWakePhrase(await TranscribePcm(pcm,token,language).ConfigureAwait(false),phrase))return;
            token.ThrowIfCancellationRequested();
            throw new IOException(Localization.Text("The microphone stopped recording. Reconnect it and try again."));
        }
        finally
        {
            mic.StopRecording();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
    }
}
