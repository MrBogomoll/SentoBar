using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SentoBar;

internal sealed partial class AssistantRuntime
{
    private readonly AssistantChatArchive chatArchive;
    private readonly DispatcherTimer chatSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(450)};
    private Task<string> chatWrite=Task.FromResult("");
    private bool chatDirty;
    internal IReadOnlyList<AssistantChatSummary> ChatHistory=>chatArchive.Summaries;
    internal string? ActiveChatId=>chatArchive.ActiveId;
    internal string ChatHistoryError {get;private set;}="";
    private bool CanChangeChat=>!disposed && !Busy && !VoiceActive && !ImportingFiles && !SpeechCheckBusy;

    private void InitializeChatHistory()
    {
        ChatHistoryError=chatArchive.OpenError;
        if(chatArchive.ActiveId is {} id && chatArchive.Messages(id) is {} saved)Conversation.Restore(saved);
        Conversation.Cleared+=()=>{if(chatArchive.ActiveId!=null){chatArchive.Select(null);QueueChatSave();}};
        Conversation.Committed+=()=>
        {
            if(disposed || !chatArchive.SaveCurrent(Conversation.Messages))return;
            QueueChatSave();Notify();
        };
        chatSaveTimer.Tick+=(_,_)=>FlushChatSave();
    }

    internal bool NewChat()
    {
        if(!CanChangeChat)return false;
        Clear();return true;
    }

    internal bool OpenChat(string id)
    {
        if(!CanChangeChat || chatArchive.Messages(id) is not {} saved)return false;
        if(ActiveChatId==id)return true;
        StopSession();chatArchive.Select(id);
        Conversation.Restore(saved);draftFiles.Clear();
        Status=Localization.Text("Saved chat opened.");QueueChatSave();Notify();return true;
    }

    internal bool DeleteChat(string id)
    {
        if(!CanChangeChat)return false;
        var before=chatArchive.Snapshot();
        if(!chatArchive.Delete(id))return false;
        chatSaveTimer.Stop();
        var snapshot=chatArchive.Snapshot();
        // An explicit deletion has a durable commit boundary. Older queued writes
        // finish first so a dismissed chat cannot reappear after a restart.
        var written=chatWrite=WriteChatAfter(chatWrite,snapshot);
        ChatHistoryError=written.GetAwaiter().GetResult();
        if(ChatHistoryError.Length>0)
        {
            chatArchive.RestoreDocument(before);
            Notify();return false;
        }
        chatDirty=false;
        if(before.ActiveId==id){StopSession();Conversation.Clear();draftFiles.Clear();}
        Status=Localization.Text("Saved chat deleted.");Notify();return true;
    }

    private void QueueChatSave()
    {
        chatDirty=true;
        chatSaveTimer.Stop();chatSaveTimer.Start();
    }

    private void FlushChatSave()
    {
        chatSaveTimer.Stop();
        if(!chatDirty)return;
        chatDirty=false;
        var written=chatWrite=WriteChatAfter(chatWrite,chatArchive.Snapshot());
        _=ReportChatSave(written);
    }

    private async Task<string> WriteChatAfter(Task<string> previous,AssistantChatArchive.Document snapshot)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await Task.Run(()=>chatArchive.Write(snapshot)).ConfigureAwait(false);
            return "";
        }
        catch(Exception ex) when(AssistantChatArchive.IsArchiveFailure(ex))
        {
            return chatArchive.OpenError.Length>0?chatArchive.OpenError:
                ex is AssistantChatArchive.CapacityException?ex.Message:
                Localization.Text("Could not save Sento chat history on this device.");
        }
    }

    private async Task ReportChatSave(Task<string> written)
    {
        string error=await written;
        if(disposed || !ReferenceEquals(chatWrite,written) || ChatHistoryError==error)return;
        ChatHistoryError=error;Notify();
    }

    private void FinishChatHistory()
    {
        FlushChatSave();
        _=chatWrite.GetAwaiter().GetResult();
    }
}
