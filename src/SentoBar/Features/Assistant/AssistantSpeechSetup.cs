using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace SentoBar;
internal sealed partial class AssistantProviderSettings
{
    private Func<bool>? saveManagedPack;
    private static Border SpeechCard(FrameworkElement content,double padding,Thickness margin)
    {
        var card=new Border{Child=content,Padding=new(padding),Margin=margin,BorderThickness=new(1)};
        card.SetResourceReference(Border.BackgroundProperty,"SettingsCard");
        card.SetResourceReference(Border.BorderBrushProperty,"SettingsLineSoft");
        card.SetResourceReference(Border.CornerRadiusProperty,"SettingsCardRadius");
        return card;
    }
    private FrameworkElement AutomaticSpeechSettings(ComboBox mode)
    {
        var service=ai.Speech;var p=new StackPanel();p.Children.Add(UI.Heading(Localization.Text("Voice models for your PC"),16));
        p.Children.Add(UI.Text(Localization.Text("Choose your language, then install the suggested speech pack."),12,UI.Muted));
        var hardware=UI.Text(Localization.Text("Hardware has not been scanned yet."),11,UI.Muted);hardware.Margin=new(0,10,0,6);p.Children.Add(hardware);
        var language=new ComboBox{ItemsSource=new[]{"English","Español","Français","Deutsch","Português","Italiano","日本語","한국어","Русский","العربية","Nederlands","Türkçe","हिन्दी","Tiếng Việt","Bahasa Indonesia"}};
        string[] codes={"en","es","fr","de","pt","it","ja","ko","ru","ar","nl","tr","hi","vi","id"};string selectedLanguage=(SpeechCatalog.Tts.FirstOrDefault(v=>v.Id==ai.Options.LocalVoice.ManagedTts)??SpeechCatalog.Tts[0]).Language;
        if(selectedLanguage is "any" or "multi")selectedLanguage=ai.Options.LocalVoice.SpeechLanguage;
        if(!ai.Options.LocalVoice.Enabled && Localization.Language=="ru" && selectedLanguage=="en")selectedLanguage="ru";
        language.SelectedIndex=Math.Max(0,Array.IndexOf(codes,selectedLanguage));Label(p,"Language for spoken replies",language);
        var extras=new StackPanel();
        var priority=new ComboBox{ItemsSource=new[]{"Light","Balanced","Quality"},SelectedItem=ai.Options.LocalVoice.SpeechPreference};if(priority.SelectedIndex<0)priority.SelectedIndex=1;Label(extras,"Performance preference",priority);
        var recommendation=UI.Text("",12);recommendation.Margin=new(0,10,0,4);p.Children.Add(recommendation);
        var downloadSummary=UI.Text("",13);downloadSummary.FontWeight=FontWeights.SemiBold;downloadSummary.Margin=new(0,2,0,5);p.Children.Add(downloadSummary);
        var downloadList=UI.Text("",11,UI.Muted);downloadList.Margin=new(4,6,0,0);
        var files=new Expander{Header=Localization.Text("Files and download sizes"),Content=downloadList,Margin=new(0,0,0,8)};
        p.Children.Add(files);
        var asr=new ComboBox{ItemsSource=SpeechCatalog.Asr,SelectedItem=SpeechCatalog.Asr.FirstOrDefault(m=>m.Id==ai.Options.LocalVoice.ManagedAsr)??SpeechCatalog.Asr[1]};Label(extras,"Listening model",asr);
        var tts=new ComboBox();Label(extras,"Speaking voice",tts);
        var gpu=new CheckBox{Content=UI.Text(Localization.Text("Use NVIDIA GPU when supported · CPU fallback included"),11),IsChecked=ai.Options.LocalVoice.ManagedGpu,Margin=new(0,12,0,6)};extras.Children.Add(gpu);
        var profileBody=new StackPanel();var profileList=new Expander{Header=Localization.Text("Hardware profiles · CPU / RAM / VRAM"),Content=profileBody,Margin=new(0,12,0,8)};extras.Children.Add(profileList);
        var profileRows=new System.Collections.Generic.List<(SpeechHardwareProfile Profile,TextBlock Text,Button Button)>();
        foreach(var profile in SpeechCatalog.Profiles)
        {
            var body=new StackPanel();body.Children.Add(UI.Text(profile.Name,12));var text=UI.Text("",10,UI.Muted);text.Margin=new(0,4,0,6);body.Children.Add(text);
            var choose=AssistantWindow.Pill("Use this profile",()=>{});body.Children.Add(choose);profileRows.Add((profile,text,choose));
            profileBody.Children.Add(SpeechCard(body,12,new(0,4,0,4)));
        }
        var detail=UI.Text("",11,UI.Muted);detail.Margin=new(0,6,0,8);extras.Children.Add(detail);
        var actions=new WrapPanel();var scan=AssistantWindow.Pill("Scan hardware",()=>{});var recommended=AssistantWindow.Pill("Use suggested models",()=>{});var install=AssistantWindow.Pill("Install & use",()=>{},true);var cancel=AssistantWindow.Pill("Cancel download",service.Cancel);var openVoice=AssistantWindow.Pill("Open voice mode",app.OpenVoice,true);
        foreach(var button in new[]{install,openVoice,cancel,scan})actions.Children.Add(button);p.Children.Add(actions);
        extras.Children.Add(recommended);
        var progress=new ProgressBar{Minimum=0,Maximum=100,Height=4,BorderThickness=new(0),Margin=new(0,4,0,8)};
        progress.SetResourceReference(Control.ForegroundProperty,"CheckedFill");progress.SetResourceReference(Control.BackgroundProperty,"ControlBorder");p.Children.Add(progress);
        var message=UI.Text("",11,UI.Muted);p.Children.Add(message);
        var catalogueBody=new StackPanel();var catalogue=new Expander{Header=Localization.Text("ASR & TTS library · curated October 2026"),Content=catalogueBody,Margin=new(0,14,0,10)};
        var descriptions=new System.Collections.Generic.Dictionary<string,TextBlock>();
        var modelButtons=new System.Collections.Generic.Dictionary<string,Button>();
        foreach(var model in SpeechCatalog.Asr)
        {
            var entry=new StackPanel();entry.Children.Add(UI.Text(model.Name,12));var text=UI.Text("",10,UI.Muted);text.Margin=new(0,4,0,6);entry.Children.Add(text);descriptions[model.Id]=text;
            var choose=AssistantWindow.Pill("Choose model",()=>asr.SelectedItem=model);modelButtons[model.Id]=choose;entry.Children.Add(choose);catalogueBody.Children.Add(SpeechCard(entry,10,new(0,3,0,3)));
        }
        catalogueBody.Children.Add(UI.Text(Localization.Text("Speaking models"),13));
        foreach(var voice in SpeechCatalog.Tts)
        {
            catalogueBody.Children.Add(AssistantWindow.Pill(voice.Name,()=>{if(voice.Supports(Language()))tts.SelectedItem=voice;}));
            catalogueBody.Children.Add(UI.Text(SpeechCatalog.VoiceDetails(voice),10,UI.Muted));
        }
        extras.Children.Add(catalogue);
        var links=new WrapPanel();links.Children.Add(AssistantWindow.Pill("Model details",()=>Process.Start(new ProcessStartInfo(asr.SelectedItem is SpeechAsrModel a?SpeechCatalog.ModelLink(a):"https://github.com/ggml-org/whisper.cpp"){UseShellExecute=true})));
        var license=AssistantWindow.Pill("Voice details & license",()=>{if(tts.SelectedItem is SpeechTtsModel m && m.Id!="windows")Process.Start(new ProcessStartInfo(SpeechCatalog.VoiceLink(m)){UseShellExecute=true});});links.Children.Add(license);links.Children.Add(AssistantWindow.Pill("Open speech folder",service.OpenFolder));extras.Children.Add(links);
        extras.Children.Add(UI.Text(Localization.Text("Suggestions are memory budgets, not measured speed or a quality ranking. Other apps and local chat share RAM/VRAM. New compatible pack versions arrive through Sento updates. Native TTS uses CPU to leave VRAM available for ASR. AMD/Intel GPUs use the CPU path. AI replies still use your selected chat provider or Hermes."),10,UI.Muted));
        p.Children.Add(new Expander{Header=Localization.Text("Model details & alternatives"),Content=extras,Margin=new(0,14,0,10)});
        bool scanning=false,subscribed=false,refreshQueued=false;string? savedMessage=null;
        string Language()=>codes[Math.Max(0,language.SelectedIndex)];string Preference()=>priority.SelectedItem as string??"Balanced";
        void Voices()
        {
            string keepAsr=(asr.SelectedItem as SpeechAsrModel)?.Id??ai.Options.LocalVoice.ManagedAsr;var listeners=SpeechCatalog.Asr.Where(a=>a.Supports(Language())).ToArray();asr.ItemsSource=listeners;asr.SelectedItem=listeners.FirstOrDefault(a=>a.Id==keepAsr)??listeners[0];
            string keep=(tts.SelectedItem as SpeechTtsModel)?.Id??ai.Options.LocalVoice.ManagedTts;var choices=SpeechCatalog.Tts.Where(v=>v.Supports(Language())).ToArray();tts.ItemsSource=choices;tts.SelectedItem=choices.FirstOrDefault(v=>v.Id==keep)??choices[0];
        }
        void Refresh()
        {
            if(asr.SelectedItem is not SpeechAsrModel a || tts.SelectedItem is not SpeechTtsModel t)return;
            var h=service.Hardware;bool useGpu=h?.UseGpu(a)==true && gpu.IsChecked==true;bool busy=service.Busy || scanning,installed=!busy && service.Installed(a,t,useGpu);
            hardware.ToolTip=h?.Summary;hardware.Text=h==null?Localization.Text("Scan this PC to find models that fit."):Localization.Text("Hardware detected · suggestions available");
            var pack=h==null?null:SpeechCatalog.RecommendPack(h,Language(),Preference());var suggestion=pack?.Asr;recommendation.ToolTip=pack?.Reason;recommendation.Text=h==null?Localization.Text("Choose a language and scan your hardware."):!h.Supported?AssistantSpeechPlatform.NativePackUnavailable:!h.Fits(SpeechCatalog.Asr[0])?Localization.Text("Memory is low. Close other apps and scan again."):Localization.Text("Suggested pack: ")+suggestion!.Name+" + "+pack!.Tts.Name;
            mode.IsEnabled=language.IsEnabled=priority.IsEnabled=asr.IsEnabled=tts.IsEnabled=scan.IsEnabled=!busy;recommended.IsEnabled=!busy && h!=null && h.FitsPack(suggestion!,pack!.Tts);gpu.IsEnabled=!busy && h?.UseGpu(a)==true;
            install.Content=installed?Localization.Text("Use installed pack"):Localization.Text("Install & use");install.IsEnabled=!busy && h!=null && (installed || h.FitsPack(a,t));cancel.Visibility=service.CanCancel?Visibility.Visible:Visibility.Collapsed;catalogue.IsEnabled=profileList.IsEnabled=!busy;
            bool selected=ai.Options.Enabled && ai.Options.LocalVoice is {Enabled:true,Managed:true,OwnModels:false} active && active.ManagedAsr==a.Id && active.ManagedTts==t.Id && active.ManagedGpu==useGpu && active.SpeechLanguage==Language() && active.SpeechPreference==Preference();
            openVoice.Visibility=installed && selected?Visibility.Visible:Visibility.Collapsed;
            install.Visibility=openVoice.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
            scan.Visibility=h==null && !scanning?Visibility.Visible:Visibility.Collapsed;
            if(h?.Supported==true && h.FitsPack(a,t))
                recommendation.Text=Localization.Format("Listening: {0}\nSpeaking: {1}",a.Name,t.Name);
            if(!busy)
            {
                var downloads=service.DownloadPlan(a,t,useGpu);
                long bytes=downloads.Where(item=>item.State==SpeechDownloadState.Required).Sum(item=>item.UpperBytes);
                bool hasCache=downloads.Any(item=>item.State==SpeechDownloadState.Cached);
                downloadSummary.Text=installed?Localization.Text("Already installed · 0 MB to download"):bytes==0?
                    Localization.Text("Files available on disk · download if verification fails"):
                    Localization.Format("Estimated download: {0}",DownloadSize(bytes));
                downloadList.Text=string.Join("\n\n",downloads.Select(item=>item.Name+" · "+item.FileName+" · "+
                    (item.Bytes==0?Localization.Format("up to {0}",DownloadSize(item.UpperBytes)):DownloadSize(item.Bytes))+" · "+
                    Localization.Text(item.State switch{SpeechDownloadState.Installed=>"Already installed",SpeechDownloadState.Cached=>"Cached · verified before use",_=>"To download"})));
                if(hasCache)downloadList.Text+="\n\n"+Localization.Text("A damaged cached file will be downloaded again.");
            }
            files.Visibility=h?.Supported==true?Visibility.Visible:Visibility.Collapsed;
            downloadSummary.Visibility=files.Visibility;
            detail.Text=Localization.Format("Recognition: {0} · speech synthesis: CPU",useGpu?Localization.Text("NVIDIA GPU with CPU fallback"):"CPU")+Localization.Text("\nMemory budget: ")+a.RamGb.ToString("0.#")+Localization.Text(" GB total, ")+SpeechHardware.PackFreeGb(a,t).ToString("0.#")+Localization.Text(" GB available.");
            detail.Text+="\n"+SpeechCatalog.AsrDetails(a)+"\n"+SpeechCatalog.VoiceDetails(t);
            if(t.Engine=="supertonic")detail.Text+=Localization.Text("\nSynthesis steps: ")+(Preference()=="Quality"?12:Preference()=="Light"?4:8)+Localization.Text(" · OpenRAIL-M model license (separate from the app's MIT license).");
            if(h?.Supported==true && !h.FitsPack(a,t))detail.Text+=Localization.Text("\nClose other apps or choose a smaller combination before starting.");
            if(AssistantSpeechPlatform.NativePacksSupported && !AssistantSpeechInstall.CppRuntimePresent())detail.Text+=Localization.Text("\nA missing Microsoft C++ runtime is installed too; Windows may request administrator approval.");
            license.Visibility=t.Id=="windows"?Visibility.Collapsed:Visibility.Visible;progress.Visibility=service.Busy?Visibility.Visible:Visibility.Collapsed;progress.Value=service.Progress;
            message.Text=savedMessage??(busy || service.Progress>0 || h==null?service.Status:"");
            message.Visibility=message.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
            if(profileList.IsExpanded && !busy)foreach(var row in profileRows)
            {
                var candidate=h==null?null:SpeechCatalog.ProfilePack(h,row.Profile,Language(),Preference());row.Button.IsEnabled=candidate!=null;
                row.Text.Text=candidate!=null?candidate.Asr.Name+" + "+candidate.Tts.Name+Localization.Text("\nFits available memory. ")+candidate.Reason:SpeechCatalog.AsrById(row.Profile.AsrId).Name+"\n"+(h==null?Localization.Text("Scan hardware to check availability."):Localization.Text("Needs matching hardware, a supported language and enough free RAM/VRAM."));
            }
            foreach(var model in SpeechCatalog.Asr)modelButtons[model.Id].IsEnabled=!busy && model.Supports(Language());
            if(catalogue.IsExpanded && !busy)foreach(var model in SpeechCatalog.Asr)descriptions[model.Id].Text=(model.DownloadBytes/1000000d).ToString("0")+Localization.Text(" MB model · ")+model.RamGb.ToString("0.#")+Localization.Text(" GB RAM budget · ")+SpeechCatalog.AsrDetails(model)+"\n"+(h==null?Localization.Text("Scan hardware to check fit."):h.FitsPack(model,t)?Localization.Text("Fits the current memory budget with this voice."):Localization.Text("Not enough available memory for this combination."));
        }
        void Changed(){if(!subscribed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Changed));return;}if(!p.IsVisible || refreshQueued)return;refreshQueued=true;Dispatcher.BeginInvoke(()=>{refreshQueued=false;if(subscribed && p.IsVisible)Refresh();});}
        void Recommend(){if(service.Hardware is not {} h)return;SelectPack(SpeechCatalog.RecommendPack(h,Language(),Preference()));}
        void SelectPack(SpeechPack pack){asr.SelectedItem=pack.Asr;tts.SelectedItem=pack.Tts;gpu.IsChecked=pack.Gpu;savedMessage=null;Refresh();}
        async System.Threading.Tasks.Task Scan()
        {
            if(scanning || service.Busy)return;scanning=true;savedMessage=Localization.Text("Reading CPU, available RAM and GPU…");Refresh();
            try{await service.Scan();if(!ai.Options.LocalVoice.Managed)Recommend();savedMessage=null;}
            catch(Exception){savedMessage=Localization.Text("Could not check this PC. Try Scan hardware again.");}
            finally{scanning=false;Refresh();}
        }
        bool UsePack()
        {
            try
            {
                if(service.Busy)throw new InvalidOperationException(Localization.Text("Wait for installation to finish or cancel it first."));if(asr.SelectedItem is not SpeechAsrModel a || tts.SelectedItem is not SpeechTtsModel t)return false;
                if(!a.Supports(Language()) || !t.Supports(Language()))throw new InvalidOperationException(Localization.Text("Choose listening and speaking models that support the selected language."));
                bool useGpu=service.Hardware?.UseGpu(a)==true && gpu.IsChecked==true;ai.UseSpeechPack(a,t,useGpu,Preference(),Language());RefreshEnabled();Feedback(Localization.Text("Speech models saved · ")+a.Name+" / "+t.Name+".");
                savedMessage=Localization.Text("Speech pack ready. Open Voice and start a conversation.");Refresh();return true;
            }
            catch(Exception ex){savedMessage=ex.Message;Feedback(Localization.Text("Speech models not saved · ")+ex.Message,true);Refresh();return false;}
        }
        saveManagedPack=UsePack;Voices();
        foreach(var row in profileRows)row.Button.Click+=(_,_)=>{if(service.Hardware is {} h && SpeechCatalog.ProfilePack(h,row.Profile,Language(),Preference()) is {} pack)SelectPack(pack);};
        scan.Click+=async(_,_)=>await Scan();recommended.Click+=(_,_)=>Recommend();asr.SelectionChanged+=(_,_)=>{savedMessage=null;Refresh();};tts.SelectionChanged+=(_,_)=>{savedMessage=null;Refresh();};language.SelectionChanged+=(_,_)=>{Voices();Recommend();Refresh();};priority.SelectionChanged+=(_,_)=>{Recommend();Refresh();};gpu.Checked+=(_,_)=>Refresh();gpu.Unchecked+=(_,_)=>Refresh();catalogue.Expanded+=(_,_)=>Refresh();profileList.Expanded+=(_,_)=>Refresh();
        install.Click+=async(_,_)=>{if(asr.SelectedItem is not SpeechAsrModel a || tts.SelectedItem is not SpeechTtsModel t)return;bool useGpu=service.Hardware?.UseGpu(a)==true && gpu.IsChecked==true;savedMessage=null;if(!service.Installed(a,t,useGpu)){ai.StopSession();if(!await service.Install(a,t,useGpu)){savedMessage=service.Status;Refresh();return;}}UsePack();};
        bool initialized=false;
        async System.Threading.Tasks.Task Open()
        {
            if(!initialized){initialized=true;if(service.Hardware==null)await Scan();else if(!ai.Options.LocalVoice.Managed)Recommend();}
            Refresh();
        }
        p.Loaded+=async(_,_)=>{if(subscribed)return;subscribed=true;service.Changed+=Changed;if(p.IsVisible)await Open();};
        p.IsVisibleChanged+=async(_,_)=>{if(p.IsVisible && p.IsLoaded)await Open();};p.Unloaded+=(_,_)=>{subscribed=false;service.Changed-=Changed;};Refresh();
        return SpeechCard(p,16,new(0,12,0,10));
    }
    private static string DownloadSize(long bytes)=>bytes<1_000_000?
        Localization.Format("{0:0.#} KB",bytes/1000d):Localization.Format("{0:0.#} MB",bytes/1_000_000d);
}
