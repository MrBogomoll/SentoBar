using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SentoBar;

internal static class InstallationInputTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        static void Reject(Action action){try{action();}catch(IOException){return;}catch(InvalidDataException){return;}throw new Exception("Unsafe input accepted");}
        test("Local speech worker accepts the private pipe name created by the parent",()=>
        {
            using var worker=new AssistantSpeechWorker();
            Check(AssistantSpeechWorker.IsPipeName(worker.PipeName));
            Check(!AssistantSpeechWorker.IsPipeName(worker.PipeName+"0"));
            Check(!AssistantSpeechWorker.IsPipeName("SentoBar-tts-"+new string('z',32)));
        });
        test("Installed icon uses the current embedded artwork at small and large sizes",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"SentoBar-brand-icon-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{string file=Installer.WriteIcon(root);foreach(int size in new[]{16,32,64}){using var icon=new System.Drawing.Icon(file,new System.Drawing.Size(size,size));Check(icon.Width==size && icon.Height==size);}}
            finally{Directory.Delete(root,true);}
        });
        test("Fresh profiles use the complete bundled preferences and keep personal paths out",()=>
        {
            using var stream=typeof(FactoryDefaults).Assembly.GetManifestResourceStream("SentoBar.DefaultPreferences.json")!;
            using var json=JsonDocument.Parse(stream);
            foreach(var property in typeof(Settings).GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))
                if(property.CanWrite && property.Name!="Shortcuts")Check(json.RootElement.TryGetProperty(property.Name,out _));
            var companion=new AssistantOptions();Check(companion.Enabled && companion.IslandCharacter && companion.WidgetNavigation && !companion.WakeWord.Enabled && companion.Providers.Count==0);
            var defaults=FactoryDefaults.Create();Check(defaults.UiLanguage=="ru");Check(defaults.LyricsEnabled && defaults.ClipboardHistory && defaults.DownloadsEnabled && defaults.AutoStart && defaults.NotificationDelivery=="Both" && !defaults.AutoDownloadUpdates);
            Check(defaults.Monitor=="" && defaults.DisplayMode=="Primary" && defaults.WeatherCity=="" && defaults.WeatherLatitude==null && defaults.DownloadsFolder=="" && defaults.CountdownSoundFile=="" && defaults.Shortcuts.Count==2);
            string root=Path.Combine(Path.GetTempPath(),"SentoBar-fresh-preference-fixture-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new Store(root);var fresh=store.Load<Settings>("settings");Check(JsonSerializer.Serialize(fresh)==JsonSerializer.Serialize(defaults));
                fresh.LyricsEnabled=false;fresh.NotificationDelivery="Off";store.Save("settings",fresh);
                var existing=store.Load<Settings>("settings");Check(!existing.LyricsEnabled && existing.NotificationDelivery=="Off");
            }
            finally{foreach(string file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}
            Check(Installer.Uninstaller.Contains("Get-SentoBarDataRoots") && Installer.Uninstaller.Contains("Remove-SentoBarSavedKeys") && Installer.Uninstaller.Contains("Также удалить мои данные и скачанные голосовые модели"));
        });
        test("Selected wake microphone stream satisfies the real Windows Speech adapter without hardware",()=>
        {
            using var stream=new AssistantWakeInput(null);
            var type=typeof(System.Speech.Recognition.SpeechRecognitionEngine).Assembly.GetType("System.Speech.Internal.SapiInterop.SpAudioStreamWrapper",true)!;
            var format=new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000,System.Speech.AudioFormat.AudioBitsPerSample.Sixteen,System.Speech.AudioFormat.AudioChannel.Mono);
            using var adapter=(IDisposable)Activator.CreateInstance(type,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{stream,format},null)!;
            Check(stream.Length==long.MaxValue && stream.Position==0 && stream.Seek(0,SeekOrigin.Begin)==0);
            stream.Feed(new byte[]{1,2,3,4});var bytes=new byte[4];var count=System.Runtime.InteropServices.Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                type.GetMethod("Read")!.Invoke(adapter,new object[]{bytes,4,count});
                Check(System.Runtime.InteropServices.Marshal.ReadIntPtr(count).ToInt64()==4 && bytes[3]==4 && stream.Position==4 && stream.Seek(0,SeekOrigin.Current)==4);
            }
            finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(count);}
            var waiting=System.Threading.Tasks.Task.Run(()=>stream.Read(new byte[4],0,4));stream.Dispose();
            Check(waiting.Wait(3000) && waiting.Result==0);
        });
        test("Wake recognizer supports optional Russian without changing the automatic or previously selected engine",()=>
        {
            var installed=new[]{("other","de-DE"),("english","en-US"),("russian","ru-RU")};
            const string russian=AssistantWakeWord.RussianRecognizerSelection;
            Check(AssistantWakeWord.ResolveRecognizerId("",installed)=="english");
            Check(AssistantWakeWord.ResolveRecognizerId("other",installed)=="other");
            Check(AssistantWakeWord.ResolveRecognizerId(russian,installed)=="russian");
            Check(AssistantWakeWord.ResolveRecognizerId(russian,installed[..2])==null);
            Check(AssistantWakeWord.ResolveRecognizerId("",installed[..1])=="other");
            Check(AssistantWakeWord.ResolveRecognizerId("removed",installed)==null);
            Check(AssistantWakeWord.EffectiveSelection("","Привет Сенто")==russian);
            Check(AssistantWakeWord.EffectiveSelection("other","Сенто")=="other");
            Check(LocalAssistantVoice.MatchesWakePhrase("Привет, Сенто!","привет сенто"));
            Check(LocalAssistantVoice.MatchesWakePhrase("Привет, Сента!", "Привет Сенто"));
            Check(LocalAssistantVoice.MatchesWakePhrase("Сента.", "Sento"));
            Check(!LocalAssistantVoice.MatchesWakePhrase("Не говори привет Сенто","Привет Сенто"));
            Check(RussianSpeechNumbers.Speak("1/60 часа, −0,05 и 22%.")=="одна шестидесятая часа, минус ноль целых пять сотых и двадцать два процента.");
            Check(RussianSpeechNumbers.Speak("2 минуты и 1 003 секунды")=="две минуты и одна тысяча три секунды");
            Check(AssistantWakeWord.ResolveRecognizerId("",Array.Empty<(string,string)>())==null);
            var saved=JsonSerializer.Deserialize<AssistantWakeOptions>(JsonSerializer.Serialize(new AssistantWakeOptions{RecognizerId=russian,Phrase="Привет Сенто"}))!;
            Check(!saved.Enabled && saved.RecognizerId==russian && AssistantWakeWord.Phrase(saved.Phrase)=="Привет Сенто");
        });
        test("Installer rollback copies the verified legacy binary in place without accepting corrupt bytes",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"SentoBar-restore-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string source=Path.Combine(root,"previous.exe"),target=Path.Combine(root,"SentoBar.exe");File.WriteAllText(source,"saved executable");File.WriteAllText(target,"current executable");
                Reject(()=>UpdateRecovery.RestoreLegacyBinary(source,target,new string('A',64)));
                Check(File.ReadAllText(target)=="current executable" && Directory.GetFiles(root).Length==2);
                string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
                UpdateRecovery.RestoreLegacyBinary(source,target,hash);Check(File.ReadAllText(target)=="saved executable" && Directory.GetFiles(root).Length==2);
            }
            finally{foreach(string file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}
        });
        test("Panel navigation migrates every legacy style and imported layout to dots",()=>
        {
            foreach(string style in new[]{"Icons","IconsLabels","Text","Dots","invalid"})
            {
                var settings=new Settings{CategoryStyle=style,LaunchStyle="Icons"};settings.Normalize();Check(settings.CategoryStyle=="Dots" && settings.LaunchStyle=="Icons");
                var imported=new Settings();CustomizationProfile.Import(JsonSerializer.Serialize(new{format="luma.layout.v1",values=new{CategoryStyle=style}}),imported);Check(imported.CategoryStyle=="Dots");
            }
            Check(new Settings().CategoryStyle=="Dots" && FactoryDefaults.Create().CategoryStyle=="Dots");
        });
        test("Microphone selection survives device reordering and rejects missing or ambiguous inputs",()=>
        {
            var devices=new[]{new AssistantMicrophone("","Default",-1),new AssistantMicrophone("usb-a","Desk microphone",3),new AssistantMicrophone("usb-b","Headset",0)};
            Check(AssistantMicrophones.Resolve("",devices)==-1 && AssistantMicrophones.Resolve("usb-a",devices)==3);
            Reject(()=>AssistantMicrophones.Resolve("unplugged",devices));
            Reject(()=>AssistantMicrophones.Resolve("usb-a",new[]{devices[1],devices[1] with{Number=1}}));
            var saved=JsonSerializer.Deserialize<AssistantOptions>(JsonSerializer.Serialize(new AssistantOptions{MicrophoneId="usb-a"}))!;
            Check(saved.MicrophoneId=="usb-a" && new AssistantOptions().MicrophoneId=="");
        });
        test("Microphone playback starts after capture and runs to completion with the original PCM",()=>Task.Run(async()=>
        {
            byte[] phrase={0x00,0x20,0x00,0xE0,0x00,0x00};
            var captured=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var playing=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var played=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls=0;
            var check=AssistantAudioCheck.Replay(_=>captured.Task,async (audio,token)=>
            {
                Check(captured.Task.IsCompletedSuccessfully);
                Check(audio.WaveFormat.SampleRate==16000 && audio.WaveFormat.BitsPerSample==16 && audio.WaveFormat.Channels==1);
                var received=new byte[phrase.Length];audio.ReadExactly(received);
                Check(received.AsSpan().SequenceEqual(phrase) && audio.ReadByte()==-1);
                calls++;playing.SetResult();await played.Task.WaitAsync(token);
            },_=>{},CancellationToken.None);
            Check(calls==0 && !check.IsCompleted);
            captured.SetResult(phrase);
            try{await playing.Task.WaitAsync(TimeSpan.FromSeconds(3));Check(!check.IsCompleted);}
            finally{played.TrySetResult();}
            Check((await check).StartsWith("Recording played",StringComparison.Ordinal) && calls==1);
        }).GetAwaiter().GetResult());
        test("Microphone playback skips silent audio and cancelled recording",()=>Task.Run(async()=>
        {
            var silence=await AssistantAudioCheck.Replay(_=>Task.FromResult(new byte[320]),(_,_)=>throw new Exception("Silence was replayed"),_=>{},CancellationToken.None);
            Check(silence.StartsWith("No microphone signal",StringComparison.Ordinal));
            using var cancelled=new CancellationTokenSource();
            try
            {
                await AssistantAudioCheck.Replay(_=>{cancelled.Cancel();return Task.FromResult(new byte[]{0,0x40});},(_,_)=>throw new Exception("Cancelled recording was replayed"),_=>{},cancelled.Token);
            }
            catch(OperationCanceledException){return;}
            throw new Exception("Cancellation was ignored");
        }).GetAwaiter().GetResult());
        test("Microphone playback reports a speaker failure after recording audio",()=>Task.Run(async()=>
        {
            var outputError=new IOException("Removed device");
            try
            {
                await AssistantAudioCheck.Replay(_=>Task.FromResult(new byte[]{0,0x40}),(_,_)=>Task.FromException(outputError),_=>{},CancellationToken.None);
            }
            catch(IOException error) when(ReferenceEquals(error.InnerException,outputError)){return;}
            throw new Exception("The failed output device was reported as successful playback");
        }).GetAwaiter().GetResult());
        test("Chat dictation waits for the recording and transcribes the captured audio once",()=>Task.Run(async()=>
        {
            var captured=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            byte[] spoken={0,0x40,0,0};
            int calls=0;
            var pending=AssistantDictation.Transcribe(_=>captured.Task,(pcm,_)=>{
                Check(pcm.AsSpan().SequenceEqual(spoken));calls++;return Task.FromResult("  Привет, Сенто!  ");
            },CancellationToken.None);
            Check(calls==0 && !pending.IsCompleted);
            captured.SetResult(spoken);
            Check(await pending=="Привет, Сенто!" && calls==1);
        }).GetAwaiter().GetResult());
        test("Chat dictation never submits silence or returns a cancelled transcription",()=>Task.Run(async()=>
        {
            try
            {
                await AssistantDictation.Transcribe(_=>Task.FromResult(new byte[320]),(_,_)=>throw new Exception("Silence reached the recognizer"),CancellationToken.None);
                throw new Exception("Silence produced a transcript");
            }
            catch(IOException error) when(error.Message==AssistantAudioCheck.NoSignal){}
            using var cancellation=new CancellationTokenSource();
            try
            {
                await AssistantDictation.Transcribe(_=>Task.FromResult(new byte[]{0,0x40}),(_,_)=>{
                    cancellation.Cancel();return Task.FromResult("Discarded");
                },cancellation.Token);
            }
            catch(OperationCanceledException){return;}
            throw new Exception("Cancelled dictation returned a transcript");
        }).GetAwaiter().GetResult());
        test("Rename migration recognizes marked historical fork builds and their own startup command",()=>
        {
            foreach(string version in new[]{"2.26.1-ru.1","1.0.0+revision","1.0.0_b.9","1.1.6","1.2.0","1.2.0_b.14+revision"})
                Check(InstallPaths.KnownForkBuild("NabyScape",version));
            foreach(string version in new[]{"2.26.1","1.0.1","1.1.60","1.2.0_b.","1.2.0_b.1-unknown"})
                Check(!InstallPaths.KnownForkBuild("NabyScape",version));
            Check(!InstallPaths.KnownForkBuild("Another product","1.2.0"));
            string folder=@"D:\My Apps\NabyScape";
            string executable=Path.Combine(folder,"NabyScape.exe");
            Check(Installer.IsForkStartup('"'+executable+'"'+" --background",folder));
            Check(Installer.IsForkStartup(executable+" --background",folder));
            Check(!Installer.IsForkStartup(null,folder) && !Installer.IsForkStartup(executable+".backup",folder));
        });
        test("Installer accepts dedicated custom folders and rejects root data relative and network paths",()=>
        {
            string custom=Path.Combine(Path.GetTempPath(),"SentoBar-install-fixture","SentoBar");
            Check(Path.GetFileName(InstallPaths.Default)=="SentoBar" && InstallPaths.Validate(custom)==custom);
            Check(InstallPaths.FromArguments(new[]{"--silent","--install-dir",custom},InstallPaths.Default)==custom);
            Reject(()=>InstallPaths.FromArguments(Array.Empty<string>(),InstallPaths.Legacy));
            foreach(string path in new[]{@"C:\",@"C:\Users",@"SentoBar",@"\\server\Apps\SentoBar",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SentoBar")})Reject(()=>InstallPaths.Validate(path));
            Reject(()=>InstallPaths.Validate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SentoBar","notes","SentoBar")));
            Reject(()=>InstallPaths.FromArguments(new[]{"--install-dir"},custom));
            Reject(()=>InstallPaths.FromArguments(new[]{"--install-dir",custom,"--install-dir",custom},custom));
        });
    }
}
