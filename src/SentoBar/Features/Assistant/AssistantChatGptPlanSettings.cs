using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SentoBar;

internal sealed partial class AssistantProviderSettings
{
    private readonly TextBlock planSummary=UI.Caption("",12,UI.Muted);
    private readonly ComboBox planModels=new(){MinHeight=40};
    private readonly StackPanel planModelControls=new();
    private Button? planSignIn,planAdd,planSignOut,planLoadModels,planCancel;
    private bool syncingPlanModels;

    private void BuildChatGptPlanControls(Panel page)
    {
        var content=new StackPanel();
        content.Children.Add(UI.Heading(Localization.Text("ChatGPT plan chat"),16));
        var description=UI.Caption("Use your ChatGPT plan for chat and for replies in local voice mode. Manage app limits and extra credits in ChatGPT.",12,UI.Muted);
        description.Margin=new(0,5,0,8);
        content.Children.Add(description);
        var actions=new WrapPanel();
        planSignIn=AssistantWindow.Pill("Continue with ChatGPT",()=>_=ConnectPlan(Selected is {UsesChatGptPlan:true} plan?plan:null),true);
        planAdd=AssistantWindow.Pill("Add another ChatGPT account",()=>_=ConnectPlan(null));
        planSignOut=AssistantWindow.Pill("Sign out of ChatGPT",()=>_=SignOutPlan());
        planCancel=AssistantWindow.Pill("Cancel",CancelProviderRequest);
        var usage=AssistantWindow.Pill("ChatGPT usage",()=>Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage"){UseShellExecute=true}));
        foreach(var button in new[]{planSignIn,planAdd,planSignOut,usage,planCancel})actions.Children.Add(button);
        content.Children.Add(actions);
        content.Children.Add(planSummary);
        Label(planModelControls,"ChatGPT model",planModels);
        planLoadModels=AssistantWindow.Pill("Refresh ChatGPT models",()=>_=LoadPlanModels());
        planLoadModels.Margin=new(0,8,0,0);
        planModelControls.Children.Add(planLoadModels);
        content.Children.Add(planModelControls);
        planModels.SelectionChanged+=(_,_)=>ChoosePlanModel();

        var card=new Border{Child=content,Padding=new(14),Margin=new(0,0,0,14)};
        card.SetResourceReference(FrameworkElement.StyleProperty,"GlassFieldFrame");
        page.Children.Add(card);
    }

    private void ResetChatGptPlanPicker(AssistantProvider? provider)
    {
        var initial=provider is {UsesChatGptPlan:true,Model.Length:>0}?
            new[]{new ChatGptPlanModel(provider.Model,provider.Model)}:Array.Empty<ChatGptPlanModel>();
        ApplyPlanModels(initial,provider?.Model??"");
        RefreshChatGptPlanControls();
    }

    private void RefreshChatGptPlanControls()
    {
        if(planSignIn==null)return;
        var plan=Selected is {UsesChatGptPlan:true} profile?profile:null;
        bool connected=plan!=null && ai.ChatGptPlan.IsSignedIn(plan.Id);
        planSignIn.IsEnabled=!providerBusy;

        planAdd!.IsEnabled=!providerBusy;
        planSignOut!.IsEnabled=!providerBusy;
        planAdd.Visibility=plan!=null?Visibility.Visible:Visibility.Collapsed;
        planSignOut.Visibility=connected?Visibility.Visible:Visibility.Collapsed;
        planCancel!.Visibility=providerBusy?Visibility.Visible:Visibility.Collapsed;
        planModelControls.Visibility=connected?Visibility.Visible:Visibility.Collapsed;
        planModels.IsEnabled=planLoadModels!.IsEnabled=!providerBusy;
        planSummary.Text=connected?Localization.Format("Signed in as {0}",plan!.Name):
            plan!=null?Localization.Text("Sign in to ChatGPT again in Sento settings."):"";
        planSummary.Visibility=planSummary.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }

    private async Task ConnectPlan(AssistantProvider? existing)
    {
        if(providerBusy || !await ConfirmProviderChanges())return;
        await WithPlanRequest("Finish signing in through your browser.",TimeSpan.FromMinutes(5),async request=>
        {
            var provider=await ai.SignInChatGptPlan(existing,request.Token);
            if(providerRequest!=request)return;
            Reload(provider.Id,keepRequest:true);
            SetProviderBusy(true);
            await LoadPlanCatalog(provider,request);
        });
    }

    private async Task LoadPlanModels()
    {
        if(Selected is not {UsesChatGptPlan:true} provider)return;
        await WithPlanRequest("Loading ChatGPT models…",TimeSpan.FromSeconds(45),
            request=>LoadPlanCatalog(provider,request));
    }

    private async Task LoadPlanCatalog(AssistantProvider provider,CancellationTokenSource request)
    {
        var choices=await ai.ChatGptPlan.Models(provider.Id,request.Token);
        if(providerRequest!=request || Selected?.Id!=provider.Id)return;
        string selected=choices.Any(model=>model.Id==provider.Model)?provider.Model:
            choices.Any(model=>model.Id=="gpt-6-luna")?"gpt-6-luna":"";
        if(provider.Model!=selected)ai.SelectChatGptPlanModel(provider,selected);
        ApplyPlanModels(choices,selected);
        ConnectionResult(choices.Count==0?Localization.Text("This ChatGPT account returned no selectable models."):
            selected.Length==0?Localization.Text("Choose a model from the ChatGPT list."):
            Localization.Format("ChatGPT model saved · {0}",selected),error:choices.Count==0,success:selected.Length>0);
    }

    private void ApplyPlanModels(IReadOnlyList<ChatGptPlanModel> choices,string model)
    {
        syncingPlanModels=true;
        try{planModels.ItemsSource=choices;planModels.SelectedItem=choices.FirstOrDefault(choice=>choice.Id==model);}
        finally{syncingPlanModels=false;}
    }

    private void ChoosePlanModel()
    {
        if(syncingPlanModels || providerBusy || Selected is not {UsesChatGptPlan:true} provider ||
            planModels.SelectedItem is not ChatGptPlanModel model || provider.Model==model.Id)return;
        try
        {
            ai.SelectChatGptPlanModel(provider,model.Id);
            ConnectionResult(Localization.Format("ChatGPT model saved · {0}",model.Name),success:true);
        }
        catch(IOException)
        {
            ApplyPlanModels((IReadOnlyList<ChatGptPlanModel>)planModels.ItemsSource,provider.Model);
            ConnectionResult(Localization.Text("Could not save the ChatGPT model."),true);
        }
    }

    private async Task TestPlanConnection(AssistantProvider provider)
    {
        if(string.IsNullOrWhiteSpace(provider.Model))
        {
            ConnectionResult(Localization.Text("Choose a model from the ChatGPT list."),true);
            return;
        }
        await WithPlanRequest("Checking a short ChatGPT reply…",TimeSpan.FromMinutes(2),async request=>
        {
            await ai.CheckChatGptPlan(provider,request.Token);
            if(providerRequest==request)ConnectionResult(Localization.Text("ChatGPT returned a test reply using your plan."),success:true);
        });
    }

    private async Task SignOutPlan()
    {
        if(Selected is not {UsesChatGptPlan:true} provider)return;
        await WithPlanRequest("Signing out of ChatGPT…",TimeSpan.FromMinutes(1),async request=>
        {
            bool revoked=await ai.SignOutChatGptPlan(provider,request.Token);
            if(providerRequest!=request)return;
            ResetChatGptPlanPicker(provider);
            ConnectionResult(Localization.Text(revoked?"Signed out of ChatGPT.":"Signed out locally. You can disconnect SentoBar in ChatGPT settings."),error:!revoked);
        });
    }

    private async Task WithPlanRequest(string message,TimeSpan timeout,Func<CancellationTokenSource,Task> operation)
    {
        if(providerBusy)return;
        using var request=new CancellationTokenSource(timeout);
        providerRequest=request;
        SetProviderBusy(true);
        ConnectionResult(Localization.Text(message));
        try{await operation(request);}
        catch(OperationCanceledException)
        {
            if(providerRequest==request)ConnectionResult(Localization.Text("The ChatGPT operation timed out. Try again."),true);
        }
        catch(Exception ex)
        {
            if(providerRequest==request)ConnectionResult(ex is IOException or InvalidOperationException or TimeoutException?
                ex.Message:Localization.Text("Could not connect to ChatGPT. Try again."),true);
        }
        finally
        {
            if(providerRequest==request){providerRequest=null;SetProviderBusy(false);}
        }
    }
}
