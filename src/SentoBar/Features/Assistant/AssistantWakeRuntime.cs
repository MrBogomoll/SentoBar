using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SentoBar;

internal sealed partial class AssistantRuntime
{
    private readonly AssistantWakeWord wake=new();
    private bool backgroundReady,wakeWanted,wakeStarting,wakeFailed,wakeTriggering;
    private int wakeVersion;
    private long wakeNotBefore;
    private Task wakeClosing=Task.CompletedTask,voiceClosing=Task.CompletedTask;
    private AssistantWidgetBridge? widgetBridge;
    private CancellationTokenSource? localWakeStop;
    private Task localWakeTask=Task.CompletedTask;
    private bool localWakeListening;
    internal bool WakeListening=>wake.Listening || localWakeListening;
    internal bool WakeArmed=>wakeWanted && !wakeFailed;
    internal string WakeStatus {get;private set;}=Localization.Text("Wake word is off. Microphone stays off until you start voice.");
    internal void StartBackgroundFeatures()
    {
        backgroundReady=true;widgetBridge??=new(BridgeRequest);wakeWanted=Options.Enabled && Options.WakeWord.Enabled;Notify();
    }
    private async Task<JsonElement> BridgeRequest(JsonElement request,CancellationToken token)
    {
        if(!app.Dispatcher.CheckAccess())return await app.Dispatcher.InvokeAsync(()=>BridgeRequest(request,token),System.Windows.Threading.DispatcherPriority.Normal,token).Task.Unwrap();
        token.ThrowIfCancellationRequested();string operation=request.TryGetProperty("operation",out var op)?op.GetString()??"":"";
        if(operation=="tools")return JsonSerializer.SerializeToElement(new{tools=WidgetTools().Select(t=>new{name=t.Name,description=t.Description,inputSchema=t.Parameters,annotations=new{readOnlyHint=t.Name==ListPagesTool,destructiveHint=false,openWorldHint=false}})});
        if(operation!="call" || !request.TryGetProperty("name",out var name) || name.ValueKind!=JsonValueKind.String || !request.TryGetProperty("arguments",out var args))return JsonSerializer.SerializeToElement(new{ok=false,error="Unsupported widget bridge request."});
        string result=await ExecuteWidgetTool(new(Guid.NewGuid().ToString("N"),name.GetString()!,args.GetRawText()),token);using var json=JsonDocument.Parse(result);return json.RootElement.Clone();
    }
    internal void SaveWakeWord(AssistantWakeOptions options)
    {
        options.Phrase=AssistantWakeWord.Phrase(options.Phrase);options.Confidence=Math.Clamp(options.Confidence,.55,.95);
        var previous=Options.WakeWord;Options.WakeWord=options;
        try{app.Store.Save("assistant",Options);}catch{Options.WakeWord=previous;throw;}
        PauseWake();wakeWanted=options.Enabled;wakeFailed=false;WakeStatus=options.Enabled?Localization.Text("Preparing wake listening…"):Localization.Text("Wake word is off. Microphone stays off until you start voice.");Notify();
    }
    internal void ToggleWakeListening()
    {
        if(WakeArmed){PauseWake();WakeStatus=Localization.Text("Wake word paused · wake microphone off.");}
        else if(Options.Enabled && Options.WakeWord.Enabled){PauseWake();wakeWanted=true;wakeFailed=false;WakeStatus=VoiceConnected?Localization.Text("Wake word will resume after the voice conversation."):Localization.Text("Preparing wake listening…");}
        else{WakeStatus=Localization.Text("Enable wake word in Options → Voice settings first.");}
        Notify();
    }
    private void PauseWake()
    {wakeWanted=false;wakeVersion++;StopWakeEngines();}
    private void SuspendWake(string reason)
    {wakeVersion++;StopWakeEngines();if(wakeWanted)WakeStatus=reason;}
    private void StopWakeEngines()
    {
        localWakeListening=false;
        localWakeStop?.Cancel();
        wakeClosing=Task.WhenAll(wake.Stop(),localWakeTask);
    }
    internal bool CanUseLocalWake(string language)
    {
        if(!Options.LocalVoice.Managed)return false;
        try
        {
            var model=AssistantOwnSpeech.Asr(Options.LocalVoice);
            return !model.Native && model.Supports(language) && Speech.Installed(model,SpeechCatalog.TtsById("windows"));
        }
        catch(Exception ex) when(ex is System.IO.IOException or ArgumentException or InvalidOperationException){return false;}
    }
    private bool WakeVoiceReady()
    {var selected=Options.LocalVoice.Enabled?Provider:VoiceProvider;return Setup.Voice.Configured && selected!=null && (Options.LocalVoice.Enabled?!string.IsNullOrWhiteSpace(selected.Model):!string.IsNullOrWhiteSpace(selected.RealtimeModel) && !string.IsNullOrWhiteSpace(selected.RealtimeUrl));}
    private void QueueWake()
    {
        if(!backgroundReady || disposed || !wakeWanted || wakeStarting || wakeTriggering || wakeFailed || localWakeStop is {IsCancellationRequested:false} || !Options.Enabled || !Options.WakeWord.Enabled || SpeechCheckBusy || Speech.Busy || Busy || VoiceConnected || WakeListening)return;
        if(!WakeVoiceReady()){wakeFailed=true;WakeStatus=Localization.Text("Complete Voice settings and choose a saved model, then resume wake listening.");return;}
        wakeStarting=true;_ =ArmWake(wakeVersion);
    }
    private async Task ArmWake(int version)
    {
        try
        {
            await wakeClosing;await voiceClosing;long delay=wakeNotBefore-Environment.TickCount64;if(delay>0)await Task.Delay((int)Math.Min(delay,2000));
            if(version!=wakeVersion || disposed || !wakeWanted || !Options.Enabled || !Options.WakeWord.Enabled || SpeechCheckBusy || Speech.Busy || Busy || VoiceConnected)return;
            var options=JsonSerializer.Deserialize<AssistantWakeOptions>(JsonSerializer.Serialize(Options.WakeWord))!;
            options.RecognizerId=AssistantWakeWord.EffectiveSelection(options.RecognizerId,options.Phrase);
            bool automatic=options.RecognizerId is "" or AssistantWakeWord.RussianRecognizerSelection;
            string language=options.RecognizerId==AssistantWakeWord.RussianRecognizerSelection?"ru":Options.LocalVoice.SpeechLanguage;
            if(automatic && CanUseLocalWake(language))
            {
                bool hasWindows;
                try{AssistantWakeWord.RequireRecognizer(options.RecognizerId);hasWindows=true;}
                catch(InvalidOperationException){hasWindows=false;}
                if(!hasWindows)
                {
                    var stop=localWakeStop=new CancellationTokenSource();
                    var speech=new LocalAssistantVoice(Options.LocalVoice,(_,_,_,_)=>throw new InvalidOperationException(),Speech,Options.MicrophoneId);
                    localWakeTask=Task.Run(async()=>
                    {
                        try
                        {
                            await using(speech)
                                await speech.ListenForWake(options.Phrase,language,()=>Dispatch(()=>
                                {
                                    if(version!=wakeVersion)return;
                                    localWakeListening=true;WakeStatus=Localization.Format("Listening on CPU · say “{0}”.",options.Phrase);Notify();
                                }),stop.Token);
                            Dispatch(()=>_=WakeDetected(version));
                        }
                        catch(OperationCanceledException) when(stop.IsCancellationRequested){}
                        catch(Exception ex){Dispatch(()=>{if(version==wakeVersion){localWakeListening=false;wakeFailed=true;WakeStatus=ex.Message;Notify();}});}
                        finally{_=app.Dispatcher.BeginInvoke(()=>{if(ReferenceEquals(localWakeStop,stop))localWakeStop=null;stop.Dispose();});}
                    });
                    return;
                }
            }
            await wake.Start(options,()=>Dispatch(()=>_=WakeDetected(version)),message=>Dispatch(()=>{if(version==wakeVersion){wakeFailed=true;WakeStatus=message;wakeClosing=wake.Stop();Notify();}}),Options.MicrophoneId);
            if(version==wakeVersion && wakeWanted && WakeListening)WakeStatus=Localization.Format("Wake microphone on · say “{0}”. Detection stays on this PC.",options.Phrase);
        }
        catch(Exception ex){if(version==wakeVersion){wakeFailed=true;WakeStatus=Localization.Text("Wake word unavailable: ")+ex.Message;}}
        finally{wakeStarting=false;Dispatch(Notify);}
    }
    private async Task WakeDetected(int version)
    {
        if(disposed || version!=wakeVersion || !wakeWanted || !Options.Enabled || SpeechCheckBusy || Speech.Busy || Busy || VoiceConnected)return;
        wakeTriggering=true;int activation=++wakeVersion;StopWakeEngines();
        try{await wakeClosing;if(disposed || !wakeWanted || activation!=wakeVersion)return;app.OpenVoice();await StartVoice();if(wakeWanted && !VoiceConnected){wakeWanted=false;wakeFailed=true;WakeStatus=Localization.Text("Voice did not start. Check Voice settings, then resume wake listening.");}}
        catch{wakeWanted=false;wakeFailed=true;WakeStatus=Localization.Text("Wake activation failed. Check the microphone and Voice settings.");}
        finally{wakeTriggering=false;Notify();}
    }
    internal async Task EndVoice()
    {
        // Ending a conversation returns to the enabled wake feature even when a
        // provider/setup change paused it before this manually started session.
        bool resume=Options.Enabled && Options.WakeWord.Enabled;StopSession();int version=wakeVersion;await voiceClosing;
        if(disposed || version!=wakeVersion || !resume || !Options.Enabled || !Options.WakeWord.Enabled)return;
        wakeWanted=true;wakeNotBefore=Environment.TickCount64+1200;WakeStatus=Localization.Text("Conversation ended · wake word will resume.");Notify();
    }
}
