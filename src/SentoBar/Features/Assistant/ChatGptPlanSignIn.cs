using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentoBar;

internal sealed record ChatGptPlanCallback(string Code,string ClientId,bool Declined);

internal sealed class ChatGptPlanAttempt(string hostId,string? clientId)
{
    internal const string CallbackPath="/auth/callback";
    internal const string Resource="https://api.openai.com/v1";
    internal const string Scope="openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    internal string State {get;}=Secret();
    internal string Nonce {get;}=Secret();
    internal string Verifier {get;}=Secret();

    private static string Secret()=>Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');

    internal Uri AuthorizationUri(Uri redirect)
    {
        var fields=new Dictionary<string,string>{
            ["client_id"]=clientId??"dynamic_agent_client",["ext_agent_host_id"]=hostId,
            ["response_type"]="code",["redirect_uri"]=redirect.AbsoluteUri,["scope"]=Scope,
            ["resource"]=Resource,["state"]=State,["nonce"]=Nonce,["code_challenge_method"]="S256",
            ["code_challenge"]=Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)))};
        if(clientId==null)fields["agent_name_hint"]="SentoBar";
        return new Uri("https://auth.openai.com/api/accounts/authorize?"+
            string.Join("&",fields.Select(field=>field.Key+"="+Uri.EscapeDataString(field.Value))));
    }

    internal ChatGptPlanCallback? ReadCallback(Uri uri)
    {
        if(uri.Scheme!="http" || uri.Host!="127.0.0.1" || uri.AbsolutePath!=CallbackPath ||
            uri.Fragment.Length!=0 || uri.UserInfo.Length!=0)return null;
        var fields=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var part in uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        {
            var pair=part.Split('=',2);
            if(pair.Length!=2 || !fields.TryAdd(WebUtility.UrlDecode(pair[0]),WebUtility.UrlDecode(pair[1])))return null;
        }
        if(!fields.TryGetValue("state",out var state) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(State),Encoding.UTF8.GetBytes(state)))return null;
        if(fields.ContainsKey("error"))return new("",clientId??"",true);
        var issued=fields.GetValueOrDefault("client_id")??clientId;
        if(issued==null || !issued.StartsWith("oaiapp_",StringComparison.Ordinal) ||
            issued.Length is <=7 or >128 || issued.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) ||
            clientId!=null && issued!=clientId)return null;
        if(!fields.TryGetValue("code",out var code) || code.Length is 0 or >4096 || code.Any(char.IsControl))return null;
        return new(code,issued,false);
    }
}

internal static class ChatGptPlanSignIn
{
    internal static async Task<(Uri Redirect,ChatGptPlanCallback Callback)> Authorize(ChatGptPlanAttempt attempt,CancellationToken token)
    {
        var listener=new TcpListener(IPAddress.Loopback,0);
        try
        {
            listener.Start();
            int port=((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect=new Uri($"http://127.0.0.1:{port}{ChatGptPlanAttempt.CallbackPath}");
            Process.Start(new ProcessStartInfo(attempt.AuthorizationUri(redirect).AbsoluteUri){UseShellExecute=true});
            while(!token.IsCancellationRequested)
            {
                using var client=await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                using var request=CancellationTokenSource.CreateLinkedTokenSource(token);
                request.CancelAfter(TimeSpan.FromSeconds(4));
                using var stream=client.GetStream();
                ChatGptPlanCallback? callback;
                try{callback=await ReadRequest(stream,redirect,attempt,request.Token).ConfigureAwait(false);}
                catch(OperationCanceledException) when(!token.IsCancellationRequested){continue;}
                catch(IOException){continue;}
                try{await Reply(stream,callback!=null,request.Token).ConfigureAwait(false);}
                catch(Exception ex) when(ex is IOException or OperationCanceledException){}
                if(callback!=null)return (redirect,callback);
            }
            throw new OperationCanceledException(token);
        }
        finally{listener.Stop();}
    }

    private static async Task<ChatGptPlanCallback?> ReadRequest(NetworkStream stream,Uri redirect,ChatGptPlanAttempt attempt,CancellationToken token)
    {
        byte[] bytes=new byte[8192];
        int used=0;
        while(used<bytes.Length)
        {
            int count=await stream.ReadAsync(bytes.AsMemory(used),token).ConfigureAwait(false);
            if(count==0)return null;
            used+=count;
            if(bytes.AsSpan(0,used).IndexOf("\r\n\r\n"u8)<0)continue;
            string first=Encoding.ASCII.GetString(bytes,0,used).Split("\r\n",2)[0];
            var parts=first.Split(' ');
            if(parts.Length!=3 || parts[0]!="GET" || !parts[1].StartsWith(ChatGptPlanAttempt.CallbackPath+"?",StringComparison.Ordinal))
                return null;
            return Uri.TryCreate(redirect,parts[1],out var uri)?attempt.ReadCallback(uri):null;
        }
        return null;
    }

    private static async Task Reply(NetworkStream stream,bool valid,CancellationToken token)
    {
        byte[] body=Encoding.UTF8.GetBytes(valid?"Вернитесь в SentoBar, чтобы завершить вход.":"Запрос входа не распознан.");
        string status=valid?"200 OK":"400 Bad Request";
        byte[] headers=Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(headers,token).ConfigureAwait(false);
        await stream.WriteAsync(body,token).ConfigureAwait(false);
    }
}
