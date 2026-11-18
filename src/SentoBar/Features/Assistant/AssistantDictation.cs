using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SentoBar;

internal enum AssistantDictationPhase { Idle, Recording, Transcribing }

internal static class AssistantDictation
{
    internal const int MaximumSeconds=60;

    internal static async Task<string> Transcribe(Func<CancellationToken,Task<byte[]>> capture,
        Func<byte[],CancellationToken,Task<string>> transcribe,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] recording=await capture(token);
        token.ThrowIfCancellationRequested();
        if(!AssistantAudioCheck.HasSignal(recording))throw new IOException(AssistantAudioCheck.NoSignal);
        string text=await transcribe(recording,token);
        token.ThrowIfCancellationRequested();
        return text[..Math.Min(text.Length,8000)].Trim();
    }
}

internal sealed partial class AssistantRuntime
{
    private CancellationTokenSource? dictationCancellation;
    private TaskCompletionSource? dictationFinish;
    internal AssistantDictationPhase DictationPhase {get;private set;}
    internal string DictationStatus {get;private set;}="";
    internal float DictationLevel {get;private set;}
    internal bool DictationAvailable=>Options.Enabled && Options.LocalVoice.Enabled;
    private bool Dictating=>dictationCancellation!=null;

    internal void FinishDictation()=>dictationFinish?.TrySetResult();
    internal void CancelDictation()=>dictationCancellation?.Cancel();

    internal async Task<string?> StartDictation()
    {
        if(disposed || Dictating)return null;
        if(!DictationAvailable)
        {
            SetDictationStatus("Set up local speech recognition in Voice settings to use dictation.");
            return null;
        }
        if(Busy || SpeechCheckBusy || Speech.Busy || VoiceConnected || ImportingFiles)
        {
            SetDictationStatus("Finish the current audio or chat operation before dictating.");
            return null;
        }
        if(!HasDictationListeningModel())
        {
            SetDictationStatus("Install or select a listening model in Voice settings to use dictation.");
            return null;
        }

        using var operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dictationCancellation=operation;
        dictationFinish=finish;
        Speech.Testing=true;
        DictationPhase=AssistantDictationPhase.Recording;
        DictationLevel=0;
        try
        {
            SuspendWake(Localization.Text("Wake listening paused while you dictate."));
            SetDictationStatus("Opening the microphone…");
            var token=operation.Token;
            await WaitForMicrophoneRelease().WaitAsync(TimeSpan.FromSeconds(8),token);
            var transcript=await AssistantDictation.Transcribe(
                cancellation=>AssistantAudioCheck.Capture(Options.MicrophoneId,AssistantDictation.MaximumSeconds,finish.Task,
                    ()=>Dispatch(()=>
                    {
                        if(ReferenceEquals(dictationCancellation,operation) && !operation.IsCancellationRequested)
                        {
                            DictationStatus=Localization.Format("Dictating · click the microphone to finish ({0} seconds max).",AssistantDictation.MaximumSeconds);
                            Notify();
                        }
                    }),
                    percent=>Dispatch(()=>
                    {
                        if(!ReferenceEquals(dictationCancellation,operation) || operation.IsCancellationRequested || DictationPhase!=AssistantDictationPhase.Recording)return;
                        DictationLevel=percent/100f;
                        VisualChanged?.Invoke();
                    }),cancellation),
                (pcm,cancellation)=>TranscribeDictation(pcm,operation,cancellation),token);
            SetDictationStatus(transcript.Length==0?"No words recognized. Try again.":"");
            return transcript.Length==0?null:transcript;
        }
        catch(OperationCanceledException) when(operation.IsCancellationRequested){SetDictationStatus("Dictation cancelled.");}
        catch(OperationCanceledException){SetDictationStatus("Dictation timed out. Check the microphone or the local speech model.");}
        catch(TimeoutException){SetDictationStatus("Dictation timed out. Check the microphone or the local speech model.");}
        catch(HttpRequestException){SetDictationStatus("The speech service is not responding. Check that it is running.");}
        catch(IOException ex){SetDictationStatus(ex.Message);}
        catch(Exception){SetDictationStatus("Dictation failed. Check the microphone and Voice settings.");}
        finally
        {
            dictationFinish=null;
            dictationCancellation=null;
            DictationPhase=AssistantDictationPhase.Idle;
            DictationLevel=0;
            Speech.Testing=false;
            if(WakeArmed)wakeNotBefore=Environment.TickCount64+1200;
            Notify();
        }
        return null;
    }

    private async Task<string> TranscribeDictation(byte[] pcm,CancellationTokenSource operation,CancellationToken token)
    {
        DictationPhase=AssistantDictationPhase.Transcribing;
        DictationLevel=0;
        SetDictationStatus("Recognizing your speech…");
        await using var speech=new LocalAssistantVoice(Options.LocalVoice,
            (_,_,_,_)=>throw new InvalidOperationException("Dictation cannot send chat."),Speech);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        return await speech.TranscribeOnce(pcm,message=>Dispatch(()=>
        {
            if(!ReferenceEquals(dictationCancellation,operation) || operation.IsCancellationRequested)return;
            DictationStatus=message;
            Notify();
        }),deadline.Token);
    }

    private bool HasDictationListeningModel()
    {
        var options=Options.LocalVoice;
        try
        {
            if(options.Managed)return Speech.Installed(AssistantOwnSpeech.Asr(options),SpeechCatalog.TtsById("windows"));
            _=LocalAssistantVoice.Endpoint(options.AsrUrl);
            return true;
        }
        catch(Exception){return false;}
    }

    private void SetDictationStatus(string message)
    {
        DictationStatus=Localization.Text(message);
        Notify();
    }
}
