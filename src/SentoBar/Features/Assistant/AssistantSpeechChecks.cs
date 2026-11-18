using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
namespace SentoBar;
internal sealed partial class AssistantProviderSettings
{
    private FrameworkElement SpeechChecks()
    {
        var body=new StackPanel();
        var introduction=UI.Caption("Say a short phrase. The microphone test plays your recording through the Windows default output. The recording stays on this PC and is never saved to a file.",13,UI.Muted);
        introduction.Margin=new(0,0,0,10);
        body.Children.Add(introduction);
        var feedback=UI.Caption("Not tested on this visit.",12,UI.Muted);
        Accessibility.Live(feedback);
        var level=UI.Text("",11,UI.Muted);
        level.Visibility=Visibility.Collapsed;
        CancellationTokenSource? pending=null;
        var startButtons=new Button[3];
        var cancel=AssistantWindow.Pill("Cancel test",()=>pending?.Cancel());
        var microphone=AssistantWindow.Pill("Test microphone",()=>_=Run("microphone"),primary:true);
        var transcription=AssistantWindow.Pill("Test transcription",()=>_=Run("transcription"));
        var sample=AssistantWindow.Pill("Play sample",()=>_=Run("sample"));
        startButtons[0]=microphone;
        startButtons[1]=transcription;
        startButtons[2]=sample;
        cancel.IsEnabled=false;

        async Task Run(string kind)
        {
            if(pending!=null)return;
            if(ai.SpeechCheckBusy || ai.Speech.Busy || ai.VoiceConnected || ai.Busy)
            {
                Accessibility.Announce(feedback,Localization.Text("End the current conversation and finish model setup before testing."));
                return;
            }
            bool replay=kind=="microphone";
            if(!replay && !ai.Options.LocalVoice.Enabled)
            {
                Accessibility.Announce(feedback,Localization.Text("Realtime voice is tested by Start conversation. These individual tests need a saved local or external ASR/TTS setup."));
                return;
            }

            using var check=new CancellationTokenSource(replay?TimeSpan.FromSeconds(20):TimeSpan.FromMinutes(3));
            pending=check;
            cancel.IsEnabled=true;
            foreach(var button in startButtons)button.IsEnabled=false;
            bool began=false;

            void WhileRunning(Action update)
            {
                void Apply()
                {
                    if(ReferenceEquals(pending,check) && !check.IsCancellationRequested)update();
                }
                if(Dispatcher.CheckAccess())Apply();else Dispatcher.BeginInvoke(Apply);
            }
            void ShowStatus(string message)=>WhileRunning(()=>
            {
                level.Visibility=Visibility.Collapsed;
                Accessibility.Announce(feedback,message);
            });
            void ShowLevel(int percent)=>WhileRunning(()=>
            {
                level.Text=Localization.Format("Input level {0}%",percent);
                level.Visibility=Visibility.Visible;
            });

            try
            {
                ai.BeginSpeechCheck(check.Cancel);
                began=true;
                ShowStatus(Localization.Text("Preparing the audio check…"));
                await ai.WaitForMicrophoneRelease().WaitAsync(check.Token);

                string result;
                if(replay)result=await AssistantAudioCheck.Replay(ai.Options.MicrophoneId,ShowStatus,ShowLevel,check.Token);
                else
                {
                    var options=JsonSerializer.Deserialize<AssistantLocalVoiceOptions>(JsonSerializer.Serialize(ai.Options.LocalVoice))!;
                    await using var test=new LocalAssistantVoice(options,(_,_,_,_)=>throw new InvalidOperationException(Localization.Text("Speech checks cannot send chat.")),ai.Speech,ai.Options.MicrophoneId);
                    result=await test.Check(kind,ShowStatus,ShowLevel,check.Token);
                }
                check.Token.ThrowIfCancellationRequested();
                ShowStatus(result);
                Feedback(result);
            }
            catch(OperationCanceledException)
            {
                string message=Localization.Text("Audio test cancelled or timed out.");
                Accessibility.Announce(feedback,message);
                Feedback(message);
            }
            catch(Exception ex)
            {
                string message=replay
                    ?ex is IOException?ex.Message:Localization.Text("Audio test failed. Check your Windows sound settings and try again.")
                    :Localization.Text("Test needs attention · ")+ex.Message;
                Accessibility.Announce(feedback,message);
                Feedback(message,true);
            }
            finally
            {
                pending=null;
                level.Visibility=Visibility.Collapsed;
                cancel.IsEnabled=false;
                foreach(var button in startButtons)button.IsEnabled=true;
                if(began)ai.EndSpeechCheck();
            }
        }

        body.Children.Add(new WrapPanel{Children={microphone,cancel}});
        body.Children.Add(feedback);
        body.Children.Add(level);
        var modelHint=UI.Caption("Transcription sends a short recording to the configured speech service. Play sample speaks a fixed phrase with the saved voice.",11,UI.Muted);
        modelHint.Margin=new(0,14,0,6);
        body.Children.Add(modelHint);
        body.Children.Add(new WrapPanel{Children={transcription,sample}});
        body.Unloaded+=(_,_)=>pending?.Cancel();
        body.IsVisibleChanged+=(_,_)=>{if(!body.IsVisible)pending?.Cancel();};
        var expander=new Expander{Header=Localization.Text("Test microphone & speech"),Content=body,Margin=new(0,12,0,8)};
        UI.LocalizedName(expander,"Speech checks");
        return expander;
    }
}
internal sealed partial class LocalAssistantVoice
{
    internal async Task<string> Check(string kind,Action<string> status,Action<int> level,CancellationToken token)
    {
        if(kind=="sample")
        {
            status(Localization.Text("Preparing the saved speaking voice…"));
            await PrepareCheck(false,status,token);
            string sample=options.SpeechLanguage=="ru"?"Привет, это Сенто. В одной минуте 60 секунд. Заряд батареи 25%.":"Hello. This is Sento. Your speaking voice is ready.";
            byte[] pcm=await Synthesize(sample,token);
            status(Localization.Text("Playing sample · your microphone is off."));
            using var stream=new RawSourceWaveStream(new MemoryStream(pcm),new WaveFormat(24000,16,1));
            await AssistantAudioCheck.Play(stream,token);
            return Localization.Text("Speaking sample finished. If you heard it, your selected voice and speaker work. Listening was not tested.");
        }
        byte[] captured=await AssistantAudioCheck.Capture(microphoneId,status,level,token);
        if(!AssistantAudioCheck.HasSignal(captured))return AssistantAudioCheck.NoSignal;
        status(Localization.Text("Microphone off · testing the saved listening model…"));
        string text=await TranscribeOnce(captured,status,token);
        return string.IsNullOrWhiteSpace(text)?Localization.Text("Listening model replied but recognized no words. Try speaking clearly or choose another model."):Localization.Text("Transcription checked: “")+text[..Math.Min(240,text.Length)].Trim()+Localization.Text("”. This sample was not sent to chat.");
    }
    internal async Task<string> TranscribeOnce(byte[] pcm,Action<string> status,CancellationToken token)
    {
        await PrepareCheck(true,status,token);
        return await TranscribePcm(pcm,token);
    }
    private async Task PrepareCheck(bool listening,Action<string> status,CancellationToken token)
    {
        if(!options.Managed || !listening && AssistantOwnSpeech.Tts(options).Id=="windows")return;managed=new AssistantManagedSpeech(assets!,options);await managed.StartCheck(listening,status,token);if(managed.AsrUrl!=null){options.AsrProtocol="WhisperCpp";options.AsrUrl=managed.AsrUrl.AbsoluteUri;}
    }
}
