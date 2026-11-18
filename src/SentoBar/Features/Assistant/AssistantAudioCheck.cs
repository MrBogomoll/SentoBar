using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio;
using NAudio.Wave;

namespace SentoBar;

internal static class AssistantAudioCheck
{
    private const int RecordingSeconds=4;
    private static readonly TimeSpan StopTimeout=TimeSpan.FromSeconds(2);
    internal static readonly WaveFormat MicrophoneFormat=new(16000,16,1);

    internal static Task<string> Replay(string microphoneId,Action<string> status,Action<int> level,CancellationToken token)
        =>Replay(cancellation=>Capture(microphoneId,status,level,cancellation),Play,status,token);

    internal static async Task<string> Replay(Func<CancellationToken,Task<byte[]>> capture,
        Func<WaveStream,CancellationToken,Task> play,Action<string> status,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] pcm=await capture(token);
        token.ThrowIfCancellationRequested();
        if(!HasSignal(pcm))return NoSignal;

        // Capture returns after the input device is disposed. Play the same samples from memory.
        using var bytes=new MemoryStream(pcm,writable:false);
        using var audio=new RawSourceWaveStream(bytes,MicrophoneFormat);
        status(Localization.Text("Playing your recording through the Windows default output. Microphone is off."));
        try{await play(audio,token);}
        catch(IOException ex)
        {
            throw new IOException(Localization.Text("The microphone recorded audio, but playback failed. Check your Windows output device and volume."),ex);
        }
        token.ThrowIfCancellationRequested();
        return Localization.Text("Recording played. If you heard your phrase, your microphone and Windows sound output work.");
    }

    internal static bool HasSignal(byte[] pcm)=>AssistantVoiceAudio.Peak(pcm,pcm.Length)>=.003f;
    internal static string NoSignal=>Localization.Text("No microphone signal detected. Check the selected input, mute switch and Windows input volume.");

    internal static Task<byte[]> Capture(string microphoneId,Action<string> status,Action<int> level,CancellationToken token)
        =>Capture(microphoneId,RecordingSeconds,null,()=>status(Localization.Format("Recording for {0} seconds. Say a short phrase.",RecordingSeconds)),level,token);

    internal static Task<byte[]> Capture(string microphoneId,int seconds,Task? finish,Action started,Action<int> level,CancellationToken token)
        =>Task.Run(async()=>
        {
            token.ThrowIfCancellationRequested();
            if(finish?.IsCompleted==true)return [];
            if(seconds is <1 or >60)throw new ArgumentOutOfRangeException(nameof(seconds));
            try
            {
                byte[] captured=new byte[seconds*MicrophoneFormat.AverageBytesPerSecond];
                int bytesCaptured=0;
                using var input=new WaveInEvent{DeviceNumber=AssistantMicrophones.Resolve(microphoneId),WaveFormat=MicrophoneFormat,BufferMilliseconds=50};
                var firstBuffer=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopped=new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                input.DataAvailable+=(_,e)=>
                {
                    firstBuffer.TrySetResult();
                    if(token.IsCancellationRequested)return;
                    int count=Math.Min(e.BytesRecorded,captured.Length-bytesCaptured)&~1;
                    Buffer.BlockCopy(e.Buffer,0,captured,bytesCaptured,count);
                    bytesCaptured+=count;
                    level((int)Math.Round(AssistantVoiceAudio.Peak(e.Buffer,count)*100));
                };
                input.RecordingStopped+=(_,e)=>stopped.TrySetResult(e.Exception);
                using var recordingWindow=CancellationTokenSource.CreateLinkedTokenSource(token);
                token.ThrowIfCancellationRequested();
                input.StartRecording();
                try
                {
                    started();
                    var limit=Task.Delay(TimeSpan.FromSeconds(seconds),recordingWindow.Token);
                    if(finish==null)await Task.WhenAny(limit,stopped.Task);
                    else await Task.WhenAny(limit,stopped.Task,finish);
                }
                finally
                {
                    recordingWindow.Cancel();
                    // Wait for WaveInEvent's worker before handling an immediate stop.
                    if(!firstBuffer.Task.IsCompleted && !stopped.Task.IsCompleted)
                        await Task.WhenAny(firstBuffer.Task,stopped.Task,Task.Delay(500));
                    if(!stopped.Task.IsCompleted)input.StopRecording();
                }
                Exception? error=await stopped.Task.WaitAsync(StopTimeout);
                token.ThrowIfCancellationRequested();
                if(error!=null)throw new IOException(Localization.Text("The microphone stopped recording. Reconnect it and try again."),error);
                return captured.AsSpan(0,bytesCaptured).ToArray();
            }
            catch(MmException ex)
            {
                throw new IOException(Localization.Text("Could not use the microphone. Check the selected device and microphone access in Windows."),ex);
            }
            catch(TimeoutException ex)
            {
                throw new IOException(Localization.Text("The microphone stopped responding. Check the device and try again."),ex);
            }
        },token);

    internal static Task Play(WaveStream stream,CancellationToken token)
        =>Task.Run(async()=>
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var output=new WaveOutEvent{DeviceNumber=-1};
                var stopped=new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                output.PlaybackStopped+=(_,e)=>stopped.TrySetResult(e.Exception);
                output.Init(stream);
                token.ThrowIfCancellationRequested();
                output.Play();
                try
                {
                    Exception? error=await stopped.Task.WaitAsync(stream.TotalTime+TimeSpan.FromSeconds(3),token);
                    if(error!=null)throw new IOException(PlaybackError,error);
                }
                finally
                {
                    if(!stopped.Task.IsCompleted)
                    {
                        output.Stop();
                        try{await stopped.Task.WaitAsync(StopTimeout);}
                        catch(TimeoutException){} // Disposal still closes the output when the driver misses the stop callback.
                    }
                }
            }
            catch(MmException ex){throw new IOException(PlaybackError,ex);}
            catch(TimeoutException ex){throw new IOException(PlaybackError,ex);}
        },token);

    private static string PlaybackError=>Localization.Text("Could not play audio through the Windows default output. Check your speakers or headphones.");
}
