using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentoBar;

internal sealed record AssistantChatSummary(string Id,string Title,DateTimeOffset Updated);

internal sealed class AssistantChatArchive
{
    private const int MaximumBytes=32*1024*1024;
    private const int MaximumImageCharacters=4*1024*1024;
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("SentoBar.AssistantChats.v1");
    private static readonly JsonSerializerOptions Json=new(){DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    private readonly string path;
    private readonly bool readable;
    private Document document=new(1,null,[]);
    private AssistantChatSummary[]? summaries;
    internal string OpenError {get;}="";
    internal string? ActiveId=>document.ActiveId;
    internal IReadOnlyList<AssistantChatSummary> Summaries=>summaries??=document.Chats.OrderByDescending(c=>c.Updated)
        .Select(c=>new AssistantChatSummary(c.Id,c.Title,c.Updated)).ToArray();

    internal sealed record Document(int Version,string? ActiveId,SavedChat[] Chats);
    internal sealed record SavedChat(string Id,string Title,DateTimeOffset Updated,AssistantMessage[] Messages);
    internal sealed class CapacityException():IOException(Localization.Text("Saved chats are out of space. Delete an old chat to free up local history."));

    internal AssistantChatArchive(Store store)
    {
        path=Path.Combine(store.DirectoryPath,"assistant-chats.protected");
        try
        {
            if(Read() is {} saved)document=saved;
            readable=true;
        }
        catch(Exception ex) when(IsArchiveFailure(ex))
        {
            OpenError=Localization.Text("Could not read saved chats on this device. The original history was kept.");
        }
    }

    internal IReadOnlyList<AssistantMessage>? Messages(string id)=>document.Chats.FirstOrDefault(c=>c.Id==id)?.Messages;

    internal bool Select(string? id)
    {
        if(id!=null && Messages(id)==null)return false;
        if(ActiveId!=id)document=document with{ActiveId=id};
        return true;
    }

    internal bool SaveCurrent(IReadOnlyList<AssistantMessage> transcript)
    {
        var messages=PublicTranscript(transcript);
        if(messages.Length==0)return false;
        string id=ActiveId??Guid.NewGuid().ToString("N");
        var old=document.Chats.FirstOrDefault(c=>c.Id==id);
        var chat=new SavedChat(id,old?.Title??Title(messages),DateTimeOffset.Now,messages);
        document=new(1,id,[chat,..document.Chats.Where(c=>c.Id!=id)]);
        summaries=null;
        return true;
    }

    internal bool Delete(string id)
    {
        if(Messages(id)==null)return false;
        document=document with{ActiveId=ActiveId==id?null:ActiveId,Chats=document.Chats.Where(c=>c.Id!=id).ToArray()};
        summaries=null;
        return true;
    }

    internal Document Snapshot()=>document;
    internal void RestoreDocument(Document previous){document=previous;summaries=null;}

    // Copy only the visible chat. Tool arguments, raw shared-screen frames, provider metadata,
    // authenticated URLs and files other than prepared, local chat pictures stay out of the archive.
    internal static AssistantMessage[] PublicTranscript(IEnumerable<AssistantMessage> messages)
    {
        var safe=new List<AssistantMessage>();
        foreach(var message in messages)
        {
            if(message is null || message.Role is not ("user" or "assistant") || message.UiNoticePrefix!=null ||
                message.Text==null || message.Text.StartsWith("Updated shared screen after the reviewed action.",StringComparison.Ordinal))continue;
            var images=message.Attachments?.Where(LocalImage).Take(AssistantAttachments.MaximumFiles)
                .Select(file=>new AssistantAttachment(file.Name,file.Mime,file.Source,Note:file.Note)).ToArray();
            if(string.IsNullOrWhiteSpace(message.Text) && images is not {Length:>0})continue;
            safe.Add(new(message.Role,message.Text,Attachments:images is {Length:>0}?images:null));
        }
        return safe.ToArray();
    }

    private static bool LocalImage(AssistantAttachment file)
        =>file!=null && !string.IsNullOrEmpty(file.Name) && file.Name.Length<=512 && file.Source!=null &&
            file.Source.Length<=MaximumImageCharacters && string.IsNullOrEmpty(file.Origin) &&
            string.IsNullOrEmpty(file.ProviderId) && (file.Mime is "image/jpeg" or "image/png") &&
            file.Source.StartsWith("data:"+file.Mime+";base64,",StringComparison.Ordinal);

    private static string Title(AssistantMessage[] messages)
    {
        var text=messages.FirstOrDefault(m=>m.Role=="user" && !string.IsNullOrWhiteSpace(m.Text))?.Text;
        if(string.IsNullOrWhiteSpace(text))text=messages.SelectMany(m=>m.Attachments??[]).FirstOrDefault()?.Name;
        if(string.IsNullOrWhiteSpace(text))text=messages.FirstOrDefault(m=>!string.IsNullOrWhiteSpace(m.Text))?.Text;
        if(string.IsNullOrWhiteSpace(text))return "";
        var singleLine=string.Join(" ",text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        if(singleLine.Length<=64)return singleLine;
        return singleLine[..(char.IsHighSurrogate(singleLine[63])?63:64)]+"…";
    }

    private Document? Read()
    {
        try
        {
            ValidatePath(path);
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(stream.Length is <=0 or >MaximumBytes)throw new IOException();
            byte[] cipher=new byte[(int)stream.Length];stream.ReadExactly(cipher);
            byte[] clear=ProtectedData.Unprotect(cipher,Entropy,DataProtectionScope.CurrentUser);
            try
            {
                var result=JsonSerializer.Deserialize<Document>(clear,Json);
                if(result is not {Version:1,Chats:{}})throw new IOException();
                var chats=result.Chats.Where(c=>c!=null && Guid.TryParseExact(c.Id,"N",out _) && c.Messages!=null)
                    .DistinctBy(c=>c.Id).Select(c=>c with{Title=c.Title??"",Messages=PublicTranscript(c.Messages)}).ToArray();
                return new(1,chats.Any(c=>c.Id==result.ActiveId)?result.ActiveId:null,chats);
            }
            finally{CryptographicOperations.ZeroMemory(clear);}
        }
        catch(FileNotFoundException){return null;}
        catch(DirectoryNotFoundException){return null;}
    }

    internal void Write(Document snapshot)
    {
        if(!readable)throw new IOException(OpenError);
        ValidatePath(path);
        string staged=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        byte[] clear=JsonSerializer.SerializeToUtf8Bytes(snapshot,Json);
        try
        {
            if(clear.Length>MaximumBytes-4096)throw new CapacityException();
            byte[] cipher=ProtectedData.Protect(clear,Entropy,DataProtectionScope.CurrentUser);
            if(cipher.Length>MaximumBytes)throw new CapacityException();
            using(var stream=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                stream.Write(cipher);
                stream.Flush(true);
            }
            File.Move(staged,path,true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            try{File.Delete(staged);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        }
    }

    internal static bool IsArchiveFailure(Exception ex)
        =>ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException;

    private static void ValidatePath(string path)
    {
        try
        {
            if((File.GetAttributes(path)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)throw new IOException();
        }
        catch(FileNotFoundException){}
        catch(DirectoryNotFoundException){}
    }
}
