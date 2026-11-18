using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SentoBar;

// Controlled WPF fixtures only. No provider calls, credentials, microphone,
// desktop capture, clipboard access or injected mouse/keyboard input.
internal static class AssistantDiagnostics
{
    internal sealed class FixtureVoice : IAssistantVoice
    {
        public bool Connected {get;private set;}=true;
        internal bool Stopped,Disposed;
        public event Action<string,string>? Transcript {add{} remove{}}
        public event Action<string>? Status {add{} remove{}}
        public event Action<AssistantCall>? ToolCall {add{} remove{}}
        public event Action<float,bool>? Level {add{} remove{}}
        public CancellationToken CallToken(string id)=>new(true);
        public Task Start(AssistantProvider provider,string? key,string instructions,IReadOnlyList<AssistantTool> tools,IReadOnlyList<AssistantMessage> history,CancellationToken token)=>throw new InvalidOperationException("Fixture voice cannot open devices or a provider.");
        public Task SendText(string text,CancellationToken token)=>Task.CompletedTask;
        public Task SendImage(string image,CancellationToken token)=>Task.CompletedTask;
        public Task SendImage(Func<CancellationToken,Task<string?>> capture,CancellationToken token)=>throw new InvalidOperationException("Fixture voice cannot capture.");
        public Task SendToolResult(AssistantCall call,string result,CancellationToken token,Func<CancellationToken,Task<string?>>? freshImage=null)=>Task.CompletedTask;
        public Task Stop(){Connected=false;Stopped=true;return Task.CompletedTask;}
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
    }
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();var ai=app.Assistant;
        check("Assistant fixture starts without a provider or session permissions",ai.Provider==null && !ai.Desktop.Sharing && !ai.VoiceConnected && ai.Keys is AssistantMemoryKeys);
        var previousWake=ai.Options.WakeWord;
        ai.SaveWakeWord(new(){Enabled=true,Phrase="Привет Сенто",RecognizerId=AssistantWakeWord.RussianRecognizerSelection});
        var savedWake=app.Store.Load<AssistantOptions>("assistant").WakeWord;
        check("Wake phrase survives saving before speech services are ready",savedWake.Enabled && savedWake.Phrase=="Привет Сенто" && !ai.WakeListening);
        ai.SaveWakeWord(previousWake);
        var profile=new AssistantProvider{Name="Fixture provider",Model="fixture-model",Tools=true,Vision=true};
        ai.Options.Providers.Add(profile);ai.Options.SelectedProvider=profile.Id;ai.Options.Enabled=true;ai.SaveOptions();
        const string shortMessage="алё";
        string longMessage="Первая строка.\n"+new string('Ж',120);
        ai.Conversation.Record("user",shortMessage);
        ai.Conversation.Record("assistant","Of course. We can keep a **short task list** here, then start a timer when you are ready. Nothing runs until you review it.");
        ai.Conversation.Record("user",longMessage);
        int Subscribers()=>((MulticastDelegate?)typeof(AssistantRuntime).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ai))?.GetInvocationList().Length??0;
        int before=Subscribers();var window=new AssistantWindow(app){ShowActivated=false};window.Show();await Task.Delay(100);
        var view=Diagnostics.Descendants(window).OfType<AssistantView>().Single();
        var input=view.Editor;input.Text="A draft stays while the app updates.";input.Select(2,7);
        app.ChangeSettings();await Task.Delay(50);
        check("Assistant settings refresh preserves composer and selection",input.Text=="A draft stays while the app updates." && input.SelectionStart==2 && input.SelectionLength==7);
        input.Text="Начало конец";input.CaretIndex=7;
        view.InsertDictation("середина");
        check("Chat dictation inserts editable text without sending or replacing the draft",
            input.Text=="Начало середина конец" && ai.Conversation.Messages.Count==3);
        window.UpdateLayout();double oneLine=input.ActualHeight;
        input.Text=string.Join("\n",Enumerable.Repeat("One visible line",6));window.UpdateLayout();
        double sixLines=input.ActualHeight;
        input.Text=string.Join("\n",Enumerable.Repeat("One visible line",100));window.UpdateLayout();
        var inputScroll=(ScrollViewer)input.Template.FindName("PART_ContentHost",input);
        check("Long chat drafts grow with the number of lines before starting to scroll",
            sixLines>oneLine+48 && input.ActualHeight>sixLines && input.ActualHeight<=input.MaxHeight+.5 && inputScroll.ScrollableHeight>0);
        input.Clear();window.UpdateLayout();
        check("Chat composer shrinks after clearing the text",input.ActualHeight<=oneLine+.5);
        var editorHost=(Grid)input.Parent;
        var composerLayout=(Grid)editorHost.Parent;
        var composer=(Border)composerLayout.Parent;
        var point=new Point(composer.ActualWidth-22,input.TranslatePoint(new Point(0,15),composer).Y);
        var hit=composer.InputHitTest(point) as UIElement;
        bool editorHit=false;
        for(DependencyObject? current=hit;current!=null && current!=composer;current=VisualTreeHelper.GetParent(current))
            if(current==input){editorHit=true;break;}
        check("The far right side of the composer is inside the same editable input",editorHit && input.ActualWidth>composer.ActualWidth-27);
        var plan=new AssistantProvider{Name="Plan fixture",Backend=ChatGptPlanSession.Backend,Model="fixture-model",Protocol="Responses"};
        ai.Options.Providers.Add(plan);ai.Options.SelectedProvider=plan.Id;
        typeof(AssistantView).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,null);
        check("ChatGPT plan chat has a visible image picker and dictation next to Send",
            Diagnostics.Descendants(view).OfType<Button>().Any(b=>b.IsVisible && UI.Identity(b)=="Attach images") &&
            Diagnostics.Descendants(view).OfType<Button>().Any(b=>b.IsVisible && UI.Identity(b) is "Dictate" or "Set up dictation"));
        ai.Options.SelectedProvider=profile.Id;
        typeof(AssistantView).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,null);
        var optionsButton=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Options");
        check("Chat and voice share the centered footer arrangement",optionsButton.Parent is WrapPanel {HorizontalAlignment:HorizontalAlignment.Center,Children.Count:3});
        optionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
        bool menuOpened=optionsButton.ContextMenu.IsOpen;
        optionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
        check("Pressing Options again closes its menu",menuOpened && !optionsButton.ContextMenu.IsOpen);
        var historyButton=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Chat history");
        historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
        var chatMenu=historyButton.ContextMenu;
        check("Chat history menu shows the current saved chat and a new chat action",
            chatMenu.IsOpen && chatMenu.Items.OfType<MenuItem>().Any(item=>UI.Identity(item)=="New chat") &&
            chatMenu.Items.OfType<MenuItem>().Any(item=>item.IsChecked && item.Header is StackPanel header &&
                header.Children.OfType<TextBlock>().Any(line=>line.Text==shortMessage)));
        chatMenu.IsOpen=false;
        var replyVoice=new FixtureVoice();
        typeof(AssistantRuntime).GetField("voice",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(ai,replyVoice);
        var dismissReply=(Button)typeof(OverlayWindow).GetField("assistantReplyDismissButton",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(island)!;
        dismissReply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        check("The reply dismissal button ends the active voice conversation",replyVoice.Stopped && replyVoice.Disposed && !ai.VoiceConnected);
        var orb=Diagnostics.Descendants(window).OfType<AssistantOrb>().Single();
        var transcript=Diagnostics.Descendants(window).OfType<ScrollViewer>().Single(s=>UI.Identity(s)=="AI conversation");
        var messages=(StackPanel)transcript.Content;
        check("Assistant idle orb has no render subscription",!orb.Rendering);orb.State=AssistantState.Thinking;check("Assistant active orb wakes on demand",orb.Rendering);orb.ReduceMotion=true;check("Assistant Reduce Motion detaches continuous rendering",!orb.Rendering);orb.State=AssistantState.Idle;orb.ReduceMotion=false;
        foreach(var size in new[]{(480d,690d),(360d,420d)})
        {
            window.Width=size.Item1;window.Height=size.Item2;await Task.Delay(70);window.UpdateLayout();
            var labels=Diagnostics.Descendants(messages).OfType<TextBlock>().ToArray();
            var shortText=labels.Single(label=>label.Text==shortMessage);
            var longText=labels.Single(label=>label.Text==longMessage);
            var shortRow=(Border)messages.Children[0];
            var shortBounds=shortText.TransformToAncestor(messages).TransformBounds(new Rect(shortText.RenderSize));
            var longBounds=longText.TransformToAncestor(messages).TransformBounds(new Rect(longText.RenderSize));
            check("Short outgoing text hugs the right edge without a fill at "+size.Item1,shortRow.Background==Brushes.Transparent && shortBounds.Width<messages.ActualWidth/2 && shortBounds.Right>=messages.ActualWidth-16);
            check("Long outgoing text wraps at the same right edge at "+size.Item1,longText.TextAlignment==TextAlignment.Right && longBounds.Height>shortBounds.Height*2 && longBounds.Left>=0 && Math.Abs(shortBounds.Right-longBounds.Right)<1);
            check("Assistant reply keeps left-aligned Markdown at "+size.Item1,labels.Any(label=>label.TextAlignment==TextAlignment.Left && label.Inlines.OfType<System.Windows.Documents.Bold>().Any()));
            foreach(double scale in new[]{1d,1.5,2d})Capture(window,$"assistant-chat-{size.Item1}-{scale:0.0}x",scale);
            var outer=(ScrollViewer)Diagnostics.Descendants(window).OfType<ScrollViewer>().First();outer.ScrollToEnd();window.UpdateLayout();
            check("Assistant compact layout keeps form scrollable at "+size.Item1,window.ActualWidth<=size.Item1+1 && input.ActualWidth>200 && input.ActualHeight>30 && outer.ComputedHorizontalScrollBarVisibility==Visibility.Collapsed);outer.ScrollToTop();
        }
        window.Close();await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        check("Assistant closing window releases listeners and orb rendering",Subscribers()==before && !orb.Rendering);
        for(int i=0;i<10;i++){var cycle=new AssistantWindow(app){ShowActivated=false};cycle.Show();cycle.Close();}
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        check("Assistant repeated open/close does not accumulate runtime listeners",Subscribers()==before);
        var providers=new AssistantProvidersWindow(app){ShowActivated=false};providers.Show();await Task.Delay(70);
        foreach(var size in new[]{(600d,740d),(360d,420d)})
        {
            providers.Width=size.Item1;providers.Height=size.Item2;await Task.Delay(70);providers.UpdateLayout();
            foreach(double scale in new[]{1d,1.5,2d})Capture(providers,$"assistant-providers-{size.Item1}-{scale:0.0}x",scale);
            var scroll=Diagnostics.Descendants(providers).OfType<ScrollViewer>().First();scroll.ScrollToEnd();providers.UpdateLayout();
            check("Assistant providers fit compact width with vertical access at "+size.Item1,scroll.ScrollableHeight>0 && scroll.ComputedHorizontalScrollBarVisibility==Visibility.Collapsed);scroll.ScrollToTop();
        }
        check("Assistant custom caption has accessible window controls",new[]{"Minimize","Maximize or restore","Close"}.All(name=>Diagnostics.Descendants(providers).OfType<Button>().Any(b=>UI.Identity(b)==name && AutomationProperties.GetName(b)==Localization.Text(name))));
        providers.Close();ai.Clear();
        check("Assistant Clear removes in-memory messages",ai.Conversation.Messages.Count==0);
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Synthetic WPF fixtures, not a live AI response. 1x/1.5x/2x are render pixel densities of actual WPF windows, not mixed-DPI monitor emulation. No API, microphone, desktop screenshot, credential vault or native input was used. Physical DPI switching and live provider/device interoperability remain manual acceptance checks.");
    }
    private static void Capture(FrameworkElement element,string name,double scale)
    {
        element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*scale),(int)Math.Ceiling(element.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(Path.Combine(OutputDirectory(),name+".png"));encoder.Save(stream);
    }
    private static string OutputDirectory(){var args=Environment.GetCommandLineArgs();return args[Array.IndexOf(args,"--diagnostics")+1];}
}
