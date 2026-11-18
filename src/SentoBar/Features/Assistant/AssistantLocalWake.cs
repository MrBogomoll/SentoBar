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
    private static string NormalizeVoicePhrase(string text)
    {
        string words=Regex.Replace(text.ToLowerInvariant().Replace('ё','е'),@"[^\p{L}]+"," ").Trim();
        return Regex.Replace(words,@"\b(?:sento|сента|сэнта|сэнто)\b","сенто");
    }

    internal static bool MatchesWakePhrase(string text,string phrase)
        =>NormalizeVoicePhrase(text)==NormalizeVoicePhrase(phrase);

    internal async Task ListenForWake(string phrase,string language,Action<bool> ready,Action<string> status,CancellationToken token)
    {
        SpeechAsrModel? wakeModel=null;
        if(MatchesWakePhrase(phrase,"Сенто"))
            wakeModel=await assets!.PrepareSentoWakeModel(status,token).ConfigureAwait(false);

        bool lightweight=wakeModel!=null;
        await PrepareCheck(true,_=>{},token,wakeOnly:true,wakeModel:wakeModel).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        using var mic=new WaveInEvent{DeviceNumber=AssistantMicrophones.Resolve(microphoneId),WaveFormat=AssistantAudioCheck.MicrophoneFormat,BufferMilliseconds=50};
        var phrases=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest});
        using var samples=new MemoryStream();
        int voiced=0,quiet=0;
        bool tooLong=false;
        int bytesPerSecond=mic.WaveFormat.AverageBytesPerSecond;
        int maxClipBytes=bytesPerSecond*(lightweight?3:6);
        byte[] leadingAudio=new byte[bytesPerSecond/5];
        int leadingCount=0;
        double threshold=Math.Clamp(options.Sensitivity,.003,.15);
        mic.DataAvailable+=(_,e)=>
        {
            if(token.IsCancellationRequested)return;
            bool speech=AssistantVoiceAudio.Peak(e.Buffer,e.BytesRecorded)>=threshold;
            if(voiced==0 && !speech)
            {
                int count=Math.Min(e.BytesRecorded,leadingAudio.Length);
                int keep=Math.Min(leadingCount,leadingAudio.Length-count);
                Buffer.BlockCopy(leadingAudio,leadingCount-keep,leadingAudio,0,keep);
                Buffer.BlockCopy(e.Buffer,e.BytesRecorded-count,leadingAudio,keep,count);
                leadingCount=keep+count;
                return;
            }
            if(voiced==0 && leadingCount>0)
            {
                samples.Write(leadingAudio,0,leadingCount);
                leadingCount=0;
            }
            if(speech){voiced++;quiet=0;}
            else quiet+=e.BytesRecorded;
            if(samples.Length+e.BytesRecorded>maxClipBytes)tooLong=true;
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
            ready(lightweight);
            await foreach(byte[] pcm in phrases.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                // Whisper uses one audio context frame per 20 ms. Keep padding for short clips.
                double seconds=(double)pcm.Length/bytesPerSecond;
                int context=lightweight?224:Math.Max(256,(int)Math.Ceiling((seconds+1)*50));
                if(MatchesWakePhrase(await TranscribePcm(pcm,token,language,context).ConfigureAwait(false),phrase))return;
                // Back off on unrelated audio while the one-slot channel keeps the newest candidate.
                await Task.Delay(TimeSpan.FromSeconds(2),token).ConfigureAwait(false);
            }
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
