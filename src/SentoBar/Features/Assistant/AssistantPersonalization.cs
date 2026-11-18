using System;
using System.Windows;
using System.Windows.Controls;

namespace SentoBar;

internal sealed partial class AssistantProviderSettings
{
    internal void OpenPersonalization()=>SelectSetupSection("Personalization");

    private void BuildPersonalization(StackPanel page)
    {
        page.Children.Add(UI.Heading(Localization.Text("Personalization"),20));
        page.Children.Add(UI.Text(Localization.Text("Sento includes these preferences in requests to your selected model in chat and voice."),13,UI.Muted));
        TextBox Editor(string label,string value)
        {
            var field=UI.CaptionInput(label,value);
            field.AcceptsReturn=true;
            field.TextWrapping=TextWrapping.Wrap;
            field.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
            field.MaxLength=AssistantOptions.PersonalizationFieldLimit;
            field.Height=96;
            Label(page,label,field);
            return field;
        }

        var about=Editor("About me",ai.Options.AboutUser);
        var answers=Editor("How to answer",ai.Options.ReplyPreferences);
        var save=AssistantWindow.Pill("Save personalization",()=>
        {
            try
            {
                ai.SavePersonalization(about.Text,answers.Text);
                Feedback(Localization.Text("Personalization saved."),success:true);
            }
            catch(Exception){Feedback(Localization.Text("Could not save personalization."),error:true);}
        },true);
        save.Margin=new(0,12,0,6);save.HorizontalAlignment=HorizontalAlignment.Left;
        page.Children.Add(save);
    }
}

internal sealed partial class AssistantRuntime
{
    internal void SavePersonalization(string aboutUser,string replyPreferences)
    {
        var oldAbout=Options.AboutUser;
        var oldInstructions=Options.Instructions;
        Options.SetPersonalization(aboutUser,replyPreferences);
        try{app.Store.Save("assistant",Options);}
        catch{Options.AboutUser=oldAbout;Options.Instructions=oldInstructions;throw;}
    }
}
