using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace SentoBar;

internal sealed partial class AssistantProviderSettings
{
    private sealed record WakeRecognizerChoice(string Id,string Label,bool Available){public override string ToString()=>Label;}
    private FrameworkElement WakeWordSettings()
    {
        var p=new StackPanel();var current=ai.Options.WakeWord;
        var enabled=new CheckBox{Content=Localization.Text("Enable wake word on this PC"),IsChecked=current.Enabled,Margin=new(0,4,0,8)};p.Children.Add(enabled);
        p.Children.Add(UI.Text(Localization.Text("Say the phrase on its own to start a voice conversation."),11,UI.Muted));
        var phrase=UI.CaptionInput("Wake phrase",current.Phrase);phrase.MaxLength=64;Label(p,"Wake phrase",phrase);
        var recognizer=new ComboBox();Label(p,"Wake recognition language",recognizer);UI.LocalizedName(recognizer,"Wake recognition language");
        var languageHint=UI.Text("",12,UI.Muted);languageHint.Margin=new(0,6,0,0);p.Children.Add(languageHint);
        var feedback=UI.Text("",11,UI.Muted);feedback.Margin=new(0,8,0,8);
        System.Speech.Recognition.RecognizerInfo[] installed=[];
        var threshold=new Slider{Minimum=.55,Maximum=.95,TickFrequency=.01,IsSnapToTickEnabled=true,Value=Math.Clamp(current.Confidence,.55,.95)};
        void UpdateLanguageHint()
        {
            if(recognizer.SelectedItem is not WakeRecognizerChoice selected)return;
            string selection=AssistantWakeWord.EffectiveSelection(selected.Id,phrase.Text);
            bool windows=AssistantWakeWord.ResolveRecognizerId(selection,installed.Select(r=>(r.Id,r.Culture.Name)))!=null;
            bool local=!windows && selection is ("" or AssistantWakeWord.RussianRecognizerSelection) &&
                ai.CanUseLocalWake(selection==AssistantWakeWord.RussianRecognizerSelection?"ru":ai.Options.LocalVoice.SpeechLanguage);
            threshold.IsEnabled=windows;
            languageHint.Text=local?Localization.Text("Your installed Whisper model listens on the CPU while wake listening is on."):
                windows?Localization.Text("Windows listens locally for this phrase."):AssistantWakeWord.MissingRecognizerMessage(selection);
            languageHint.Visibility=languageHint.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        }
        phrase.TextChanged+=(_,_)=>UpdateLanguageHint();
        recognizer.SelectionChanged+=(_,_)=>UpdateLanguageHint();
        void Languages()
        {
            string selected=(recognizer.SelectedItem as WakeRecognizerChoice)?.Id??current.RecognizerId;
            installed=[];
            try{installed=AssistantWakeWord.Recognizers();}
            catch(InvalidOperationException ex){feedback.Text=ex.Message;}
            const string russian=AssistantWakeWord.RussianRecognizerSelection;
            bool russianAvailable=AssistantWakeWord.ResolveRecognizerId(russian,installed.Select(r=>(r.Id,r.Culture.Name)))!=null;
            var choices=new List<WakeRecognizerChoice>
            {
                new("",Localization.Text("Automatic"),installed.Length>0),
                new(russian,Localization.Text("Russian wake recognition"),russianAvailable)
            };
            choices.AddRange(installed.Select(r=>new WakeRecognizerChoice(r.Id,r.Description+" · "+r.Culture.DisplayName,true)));
            var selectedChoice=choices.FirstOrDefault(r=>r.Id==selected);
            if(selectedChoice==null)
            {
                selectedChoice=new(selected,Localization.Text("Previously selected recognizer · unavailable"),false);
                choices.Add(selectedChoice);
            }
            recognizer.ItemsSource=choices;
            recognizer.SelectedItem=selectedChoice;
            UpdateLanguageHint();
        }
        var languageActions=new WrapPanel();languageActions.Children.Add(AssistantWindow.Pill("Refresh recognizers",Languages));languageActions.Children.Add(AssistantWindow.Pill("Windows language options",()=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:regionlanguage"){UseShellExecute=true});}catch{feedback.Text=Localization.Text("Open Windows Settings → Time & language → Language & region.");}}));p.Children.Add(languageActions);
        var sensitivity=UI.Text("",11,UI.Muted);void ThresholdLabel()=>sensitivity.Text=Localization.Text("Activation confidence · ")+Math.Round(threshold.Value*100)+"%";ThresholdLabel();threshold.ValueChanged+=(_,_)=>ThresholdLabel();sensitivity.Margin=new(0,10,0,4);p.Children.Add(sensitivity);p.Children.Add(threshold);UI.LocalizedName(threshold,"Wake word activation confidence");p.Children.Add(UI.Text(Localization.Text("This threshold applies to the Windows recognizer."),10,UI.Muted));
        var actions=new WrapPanel{Margin=new(0,8,0,0)};actions.Children.Add(AssistantWindow.Pill("Save & apply wake word",()=>{try{ai.SaveWakeWord(new(){Enabled=enabled.IsChecked==true,Phrase=phrase.Text,RecognizerId=(recognizer.SelectedItem as WakeRecognizerChoice)?.Id??"",Confidence=threshold.Value});feedback.Text="";Feedback(Localization.Text("Wake phrase saved."));}catch(Exception ex){feedback.Text=ex.Message;Feedback(Localization.Text("Wake word not saved · ")+ex.Message,true);}},true));
        var pause=AssistantWindow.Pill("Resume wake word",ai.ToggleWakeListening);actions.Children.Add(pause);p.Children.Add(actions);p.Children.Add(feedback);
        var live=UI.Text("",11,UI.Muted);p.Children.Add(live);
        void Refresh(){live.Text=ai.WakeStatus;pause.Content=ai.WakeArmed?Localization.Text("Pause wake word"):Localization.Text("Resume wake word");pause.IsEnabled=ai.Options.Enabled && ai.Options.WakeWord.Enabled;}
        bool subscribed=false;p.Loaded+=(_,_)=>{if(!subscribed){ai.Changed+=Refresh;subscribed=true;}Refresh();};p.Unloaded+=(_,_)=>{if(subscribed){ai.Changed-=Refresh;subscribed=false;}};
        p.Children.Add(UI.Text(Localization.Text("To stop wake listening, choose Stop all microphones in the tray menu."),10,UI.Muted));Languages();Refresh();
        var expander=new Expander{Header=Localization.Text("Wake word · hands-free voice"),Content=p,Margin=new(0,12,0,14)};UI.LocalizedName(expander,"Wake word settings");return expander;
    }
    private FrameworkElement WidgetNavigationSettings()
    {
        var p=new StackPanel();var toggle=AssistantWindow.Pill("",()=>{});
        void Caption()=>toggle.Content=ai.Options.WidgetNavigation?Localization.Text("AI widget navigation on"):Localization.Text("AI widget navigation off");
        toggle.Click+=(_,_)=>{try{ai.SetWidgetNavigation(!ai.Options.WidgetNavigation);Caption();Feedback(Localization.Text("AI widget navigation saved · ")+(ai.Options.WidgetNavigation?Localization.Text("On"):Localization.Text("Off"))+".");}catch(Exception ex){status.Text=ex.Message;}};Caption();p.Children.Add(toggle);
        p.Children.Add(UI.Text(Localization.Text("Ask “Open Weather”, “Show my tasks” or “Next widget page”. A tool-capable chat or voice model can list enabled pages and open one. Navigation changes the visible page only; it does not read or edit widget contents or start activities."),11,UI.Muted));
        var hermes=new StackPanel();hermes.Children.Add(UI.Text(Localization.Text("1 · Keep SentoBar running with AI and widget navigation enabled.\n2 · Merge the copied sentobar entry under mcp_servers in your Hermes config.yaml. Keep your other server entries.\n3 · Restart Hermes or reload its MCP tools. Ask Hermes to use sento_list_widget_pages or sento_navigate_widget."),11,UI.Muted));
        var buttons=new WrapPanel();
        void Copy(bool wsl){try{string executable=WidgetMcpExecutable();if(wsl){if(executable.Length<3 || executable[1]!=':')throw new IOException(Localization.Text("Use the Windows setup for this installation path."));executable="/mnt/"+char.ToLowerInvariant(executable[0])+executable[2..].Replace('\\','/');}string text="mcp_servers:\n  sentobar:\n    command: "+JsonSerializer.Serialize(executable)+"\n    args: [\"--widget-mcp\"]\n";Clipboard.SetText(text);status.Text=Localization.Text("Hermes MCP setup copied. Merge it into config.yaml, then restart Hermes.");}catch(Exception ex){status.Text=ex.Message;}}
        buttons.Children.Add(AssistantWindow.Pill("Copy Hermes setup",()=>Copy(false)));buttons.Children.Add(AssistantWindow.Pill("Copy WSL setup",()=>Copy(true)));buttons.Children.Add(AssistantWindow.Pill("Save MCP config…",()=>
        {
            try{var dialog=new Microsoft.Win32.SaveFileDialog{Title=Localization.Text("Save SentoBar MCP setup"),FileName="sentobar-mcp.json",Filter="JSON (*.json)|*.json",DefaultExt=".json",AddExtension=true};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(new{mcpServers=new{sentobar=new{command=WidgetMcpExecutable(),args=new[]{"--widget-mcp"}}}},new JsonSerializerOptions{WriteIndented=true}));status.Text=Localization.Text("MCP config saved. Import or merge it in your local MCP client.");}catch(Exception ex){status.Text=ex.Message;}
        }));hermes.Children.Add(buttons);hermes.Children.Add(UI.Text(Localization.Text("The connector uses stdio and a pipe restricted to your Windows account. There is no listening network port. The Windows setup is for Hermes on this PC; the WSL setup requires Windows executable interop in WSL on the same PC."),10,UI.Muted));p.Children.Add(new Expander{Header=Localization.Text("Connect Hermes & other MCP clients"),Content=hermes,Margin=new(0,12,0,0)});
        return new Expander{Header=Localization.Text("Widget navigation · AI tools"),Content=p,Margin=new(0,14,0,8)};
    }
    private static string WidgetMcpExecutable()
    {string installed=Path.Combine(Installer.InstallDirectory,"SentoBar.exe");return File.Exists(installed)?installed:Environment.ProcessPath??throw new IOException(Localization.Text("SentoBar executable path is unavailable."));}
}

internal sealed partial class AssistantRuntime
{
    internal void SetWidgetNavigation(bool value)
    {
        bool previous=Options.WidgetNavigation;Options.WidgetNavigation=value;
        try{app.Store.Save("assistant",Options);}catch{Options.WidgetNavigation=previous;throw;}
        StopSession();Conversation.Clear();Status=value?Localization.Text("AI widget navigation enabled. Start a new chat or voice conversation."):Localization.Text("AI widget navigation is off.");Notify();
    }
}
