using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentoBar;
internal static class AssistantControls
{
    private static void AddModes(Panel row,App app,bool voice)
    {
        foreach(var item in new[]{("Chat",false),("Voice",true)})
        {
            bool selected=item.Item2==voice;var button=Action(item.Item1,item.Item2?"mic":"chat",()=>{if(!selected){if(item.Item2)app.OpenVoice();else app.ShowAssistant();}});
            button.MinWidth=72;
            if(selected)
            {
                GlassDockAppearance.Emphasize(button);
                button.Padding=new(11,6,11,6);
            }
            else button.Foreground=UI.Muted;
            button.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;button.ToolTip=item.Item2?Localization.Text("Voice conversation · the microphone starts only when you choose Start"):Localization.Text("Text conversation");
            if(button.Content is StackPanel content && content.Children[1] is TextBlock label)label.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetHelpText(button,selected?Localization.Text("Current mode"):Localization.Text("Switch conversation mode"));row.Children.Add(button);
        }
    }
    internal static Button Action(string text,string icon,Action action,bool primary=false)
    {
        var button=UI.CaptionButton(text,action,primary);var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var glyph=Icons.Create(icon,14,primary?Brushes.Black:UI.Muted);glyph.Margin=new(0,0,7,0);row.Children.Add(glyph);var label=UI.Caption(text,12,primary?Brushes.Black:null);row.Children.Add(label);
        label.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){Source=button});FollowForeground(glyph,button);
        label.TextWrapping=TextWrapping.NoWrap;button.Content=row;button.Tag=new CornerRadius(8);button.MinHeight=34;button.Margin=new(0);button.Padding=new(12,7,12,7);button.HorizontalContentAlignment=HorizontalAlignment.Center;button.VerticalContentAlignment=VerticalAlignment.Center;if(!primary)button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.FontSize=12;UI.LocalizedName(button,text);return button;
    }
    private static void FollowForeground(FrameworkElement icon,Button button)
    {
        if(icon is Viewbox {Child:Canvas canvas})foreach(var element in canvas.Children)if(element is Shape path)path.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){Source=button});
        if(icon is Shape filled)filled.SetBinding(Shape.FillProperty,new Binding("Foreground"){Source=button});
    }
    internal static void SetActionText(Button button,string text)
    {
        if(button.Content is Grid content && content.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock wrapped)
        {
            if(wrapped.Text!=Localization.Text(text)){wrapped.Text=Localization.Text(text);var glyph=Icons.Create(text.StartsWith("End",StringComparison.Ordinal)?"stop-filled":"mic",14,Brushes.Black);FollowForeground(glyph,button);glyph.Margin=new(0,0,7,0);content.Children.RemoveAt(0);content.Children.Insert(0,glyph);}
        }
        else if(button.Content is StackPanel row && row.Children.Count>1 && row.Children[1] is TextBlock label)
        {
            if(label.Text!=Localization.Text(text)){label.Text=Localization.Text(text);var glyph=Icons.Create(text.StartsWith("End",StringComparison.Ordinal)?"stop-filled":"mic",14,Brushes.Black);FollowForeground(glyph,button);glyph.Margin=new(0,0,7,0);row.Children.RemoveAt(0);row.Children.Insert(0,glyph);}
        }
        else button.Content=Localization.Text(text);
        UI.LocalizedName(button,text);
    }
    internal static FrameworkElement Connection(App app,out TextBlock description,bool voice=false)
    {
        // Keep connection details in the menu so the footer stays one compact row.
        description=UI.Caption("",11,UI.Muted);description.MaxWidth=260;
        var menu=new ContextMenu{Placement=System.Windows.Controls.Primitives.PlacementMode.Top,MinWidth=230};
        UI.LocalizedName(menu,"Sento options");menu.Items.Add(new MenuItem{Header=description,Focusable=false,IsHitTestVisible=false});menu.Items.Add(new Separator());
        menu.Items.Add(Option(Localization.Text("AI settings"),Localization.Text("Providers & API keys"),"settings",app.ShowAssistantProviders));
        menu.Items.Add(Option("Personalization","","notes",app.ShowAssistantPersonalization));
        menu.Items.Add(Option(Localization.Text("Voice settings"),Localization.Text("Models & microphone"),"mic",app.ShowVoiceSettings));
        menu.Items.Add(Option(Localization.Text("Connect Hermes"),Localization.Text("Connect or manage your own agent"),"link",app.ShowHermesSettings));
        var wake=Option(Localization.Text("Wake word"),Localization.Text("Pause or resume local wake listening"),"mic",()=>{if(app.Assistant.Options.WakeWord.Enabled)app.Assistant.ToggleWakeListening();else app.ShowVoiceSettings();});menu.Items.Add(new Separator());menu.Items.Add(wake);
        menu.Opened+=(_,_)=>{string label=app.Assistant.Options.WakeWord.Enabled?(app.Assistant.WakeArmed?Localization.Text("Pause wake word"):Localization.Text("Resume wake word")):Localization.Text("Set up wake word…");if(wake.Header is StackPanel header && header.Children[1] is TextBlock text)text.Text=Localization.Text(label);UI.LocalizedName(wake,label);wake.ToolTip=app.Assistant.WakeStatus;};
        var options=Action("Options","settings",()=>{});
        options.ToolTip=Localization.Text("AI settings, voice setup and Hermes");
        AttachMenu(options,menu);
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};
        AddModes(footer,app,voice);footer.Children.Add(options);
        var frame=new Border{Child=footer,BorderThickness=new(0,1,0,0),Padding=new(0,7,0,0),Margin=new(0,7,0,0)};
        frame.SetResourceReference(Border.BorderBrushProperty,"GlassControlBorder");
        return frame;
    }
    internal static void AttachMenu(Button button,ContextMenu menu)
    {
        button.Click+=(_,_)=>menu.IsOpen=!menu.IsOpen;
        button.ContextMenu=menu;menu.PlacementTarget=button;
        var input=InputManager.Current;
        void DismissOnOptionsPress(object sender,PreProcessInputEventArgs e)
        {
            if(!menu.IsOpen || !button.IsVisible ||
                e.StagingItem.Input is not MouseButtonEventArgs {ChangedButton:MouseButton.Left,ButtonState:MouseButtonState.Pressed} press ||
                press.RoutedEvent!=Mouse.PreviewMouseDownEvent || !new Rect(button.RenderSize).Contains(press.GetPosition(button)))return;
            // An open menu captures the next outside press. Consume a press on
            // its own anchor so the same gesture cannot open the menu again.
            e.Cancel();
            menu.IsOpen=false;
        }
        menu.Opened+=(_,_)=>input.PreProcessInput+=DismissOnOptionsPress;
        menu.Closed+=(_,_)=>input.PreProcessInput-=DismissOnOptionsPress;
        button.Unloaded+=(_,_)=>{input.PreProcessInput-=DismissOnOptionsPress;menu.IsOpen=false;};
    }
    private static MenuItem Option(string title,string subtitle,string icon,Action action)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};var glyph=Icons.Create(icon,15);glyph.Margin=new(0,0,10,0);row.Children.Add(glyph);row.Children.Add(UI.Caption(title,12));
        var item=new MenuItem{Header=row,ToolTip=subtitle.Length==0?null:Localization.Text(subtitle)};UI.LocalizedName(item,title);item.Click+=(_,_)=>action();return item;
    }
    internal static void RefreshConnection(TextBlock label,AssistantRuntime ai,bool voice=false)
    {
        string text;
        if(voice && ai.Options.LocalVoice.Enabled)
        {
            var options=ai.Options.LocalVoice;text=Localization.Text(options.OwnModels?Localization.Text("Your local model files"):options.Managed?Localization.Text("Installed local speech"): Localization.Text("Local ASR & TTS service"));
        }
        else
        {
            var provider=voice?ai.VoiceProvider:ai.Provider;text=provider==null?Localization.Text("Choose a provider to get started"):provider.Name+(provider.Model.Length>0?" · "+provider.Model:"");
        }
        bool configured=voice && ai.Options.LocalVoice.Enabled || (voice?ai.VoiceProvider:ai.Provider)!=null;label.Text=configured?Localization.Format("Selected: {0}",text):Localization.Text("Choose a provider to get started");label.ToolTip=text;
    }
}
