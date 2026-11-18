using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace SentoBar;
internal sealed partial class AssistantProviderSettings
{
    private bool switchingProvider;
    internal bool HasProviderDraft=>Selected is {UsesChatGptPlan:false} saved && !loadingProvider && (key.Password.Length>0 || ReadDraftIdentity()!=ReadSavedIdentity(saved));
    internal Task<bool> ConfirmProviderChanges()
    {
        if(!HasProviderDraft)return Task.FromResult(true);

        var dialog=new Window
        {
            Title=Localization.Text("Unsaved provider changes"),Width=420,SizeToContent=SizeToContent.Height,
            ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Owner=Window.GetWindow(this),Background=UI.Brush("#16191F"),Foreground=System.Windows.Media.Brushes.White
        };
        var panel=new StackPanel{Margin=new(22)};
        panel.Children.Add(UI.Heading(Localization.Text("Keep your connection changes?"),18));
        panel.Children.Add(UI.Text(Localization.Text("Save this provider before continuing, discard its changes, or keep editing."),12,UI.Muted));

        var failure=UI.Text("",12);
        failure.Margin=new(0,12,0,0);
        failure.Visibility=Visibility.Collapsed;
        UI.SetStatusForeground(failure,error:true);
        UI.LocalizedName(failure,"Connection save error");
        panel.Children.Add(failure);

        bool saved=false,discarded=false;
        var actions=new WrapPanel{Margin=new(0,16,0,0)};
        async Task SaveDraft()
        {
            if(!actions.IsEnabled)return;
            actions.IsEnabled=false;
            try
            {
                saved=await SaveProvider(message=>
                {
                    failure.Visibility=Visibility.Visible;
                    Accessibility.Announce(failure,message);
                });
                if(saved)dialog.Close();
            }
            finally{actions.IsEnabled=true;}
        }
        var save=AssistantWindow.Pill("Save",()=>_=SaveDraft(),true);
        save.IsDefault=true;
        actions.Children.Add(save);
        actions.Children.Add(AssistantWindow.Pill("Discard",()=>{discarded=true;dialog.Close();}));
        actions.Children.Add(AssistantWindow.Pill("Keep editing",dialog.Close));
        panel.Children.Add(actions);
        dialog.Content=panel;
        dialog.ShowDialog();

        if(discarded)
        {
            syncing=true;
            list.SelectedItem=editorProvider;
            syncing=false;
            LoadSelected();
        }
        else if(!saved)connectionEditor.IsExpanded=true;
        return Task.FromResult(saved || discarded);
    }
    private async Task ChangeEditedProvider()
    {
        if(switchingProvider)return;switchingProvider=true;var requested=list.SelectedItem as AssistantProvider;
        try{syncing=true;list.SelectedItem=editorProvider;syncing=false;if(!await ConfirmProviderChanges())return;syncing=true;list.SelectedItem=ai.Options.Providers.Find(p=>p.Id==requested?.Id);syncing=false;LoadSelected();connectionEditor.IsExpanded=true;if(Selected is {UsesChatGptPlan:true} plan && ai.ChatGptPlan.IsSignedIn(plan.Id))await LoadPlanModels();}
        finally{syncing=false;switchingProvider=false;}
    }
}
