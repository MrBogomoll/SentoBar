using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SentoBar;

internal sealed partial class AssistantRuntime
{
    private async Task<string?> ChatCredential(AssistantProvider provider,CancellationToken token)
        =>provider.UsesChatGptPlan?await ChatGptPlan.AccessToken(provider.Id,token):Keys.Read(provider.Id);

    internal async Task<AssistantProvider> SignInChatGptPlan(AssistantProvider? existing,CancellationToken token)
    {
        if(existing==null && Options.Providers.Count>=12)
            throw new InvalidOperationException(Localization.Text("Keep up to 12 providers. Remove one before adding another."));
        string id=existing?.Id??Guid.NewGuid().ToString("N");
        var label=await ChatGptPlan.SignIn(id,token);
        if(Options.Providers.Any(other=>other.Id!=id && other.Name==label))label+=" · "+id[..6];
        var provider=new AssistantProvider{
            Id=id,Name=label,Backend=ChatGptPlanSession.Backend,Protocol="Responses",
            BaseUrl=ChatGptPlanSession.ApiBase,Model=existing?.Model??""};
        SavePlanProvider(provider);
        return provider;
    }

    private void SavePlanProvider(AssistantProvider provider)
    {
        int index=Options.Providers.FindIndex(other=>other.Id==provider.Id);
        var previous=index>=0?Options.Providers[index]:null;
        string previouslySelected=Options.SelectedProvider;
        if(index<0)Options.Providers.Add(provider);
        else Options.Providers[index]=provider;
        Options.SelectedProvider=provider.Id;
        try{app.Store.Save("assistant",Options);}
        catch
        {
            if(previous==null)Options.Providers.Remove(provider);
            else Options.Providers[index]=previous;
            Options.SelectedProvider=previouslySelected;
            throw;
        }
        StopSession();
        Conversation.Clear();
        ForgetConnectionCheck(provider.Id);
        Status=Localization.Text("Provider saved on this device.");
        SetupChanged();
    }

    internal void SelectChatGptPlanModel(AssistantProvider provider,string model)
    {
        var current=Options.Providers.Single(item=>item.Id==provider.Id && item.UsesChatGptPlan);
        string oldModel=current.Model;
        current.Model=model;
        try{app.Store.Save("assistant",Options);}
        catch{current.Model=oldModel;throw;}
        if(Provider?.Id==current.Id){StopSession();Conversation.Clear();}
        ForgetConnectionCheck(current.Id);
        Status=Localization.Text("Provider saved on this device.");
        SetupChanged();
    }

    internal async Task<bool> SignOutChatGptPlan(AssistantProvider provider,CancellationToken token)
    {
        if(Provider?.Id==provider.Id){StopSession();Conversation.Clear();}
        bool revoked=await ChatGptPlan.SignOut(provider.Id,token);
        ForgetConnectionCheck(provider.Id);
        SetupChanged();
        return revoked;
    }

    internal async Task CheckChatGptPlan(AssistantProvider provider,CancellationToken token)
    {
        try
        {
            var access=await ChatGptPlan.AccessToken(provider.Id,token);
            var reply=await transport.Complete(provider,access,new[]{
                new AssistantMessage("user","Reply with OK only.")},Array.Empty<AssistantTool>(),token);
            if(reply.Text.Length==0)throw new InvalidOperationException(Localization.Text("ChatGPT returned no text."));
            RecordConnectionCheck(provider,true,provider.Name+" · "+provider.Model+Localization.Text(" returned a chat response."));
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            RecordConnectionCheck(provider,false,ex.Message);
            throw;
        }
    }
}
