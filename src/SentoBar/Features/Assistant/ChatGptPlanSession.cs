using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SentoBar;

internal sealed record ChatGptPlanModel(string Id,string Name)
{
    public override string ToString()=>Name;
}

internal sealed class ChatGptPlanSession : IDisposable
{
    internal const string Backend="ChatGptPlan";
    internal const string ApiBase=ChatGptPlanAttempt.Resource+"/";
    private const string Issuer="https://auth.openai.com";
    private const string TokenUrl=Issuer+"/api/accounts/oauth/token";
    private const string PlanScope="chatgpt.tokens.use.direct";

    private sealed class Host{public string Id {get;set;}="";}
    private sealed record Tokens(string Access,string Refresh,string Identity,DateTimeOffset Expires,string Scope);
    private sealed record Registration(string ClientId,string Subject,string Email,Tokens? Tokens);
    private readonly Store store;
    private readonly HttpClient client;
    private readonly Func<ChatGptPlanAttempt,CancellationToken,Task<(Uri Redirect,ChatGptPlanCallback Callback)>> authorize;
    private readonly SemaphoreSlim gate=new(1,1);

    internal ChatGptPlanSession(Store store,HttpMessageHandler? handler=null,
        Func<ChatGptPlanAttempt,CancellationToken,Task<(Uri Redirect,ChatGptPlanCallback Callback)>>? authorize=null)
    {
        this.store=store;
        this.authorize=authorize??ChatGptPlanSignIn.Authorize;
        client=new HttpClient(handler??AssistantTransport.CreateHandler()){Timeout=TimeSpan.FromSeconds(25)};
    }

    private string HostId()
    {
        var host=store.Load<Host>("chatgpt-plan-host");
        if(host.Id.StartsWith("urn:uuid:",StringComparison.Ordinal) && Guid.TryParse(host.Id[9..],out _))return host.Id;
        host.Id="urn:uuid:"+Guid.NewGuid();
        store.Save("chatgpt-plan-host",host);
        return host.Id;
    }

    internal async Task<string> SignIn(string id,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var previous=Read(id);
            var attempt=new ChatGptPlanAttempt(HostId(),previous?.ClientId);
            var (redirect,callback)=await authorize(attempt,token);
            if(callback.Declined)throw new InvalidOperationException(Localization.Text("ChatGPT sign-in was declined."));

            using var reply=await Post(TokenUrl,new Dictionary<string,string>{
                ["grant_type"]="authorization_code",["client_id"]=callback.ClientId,["code"]=callback.Code,
                ["code_verifier"]=attempt.Verifier,["redirect_uri"]=redirect.AbsoluteUri,["resource"]=ChatGptPlanAttempt.Resource
            },token);
            var tokens=ReadTokens(reply.RootElement);
            var (subject,email)=await VerifyIdentity(tokens.Identity,callback.ClientId,attempt.Nonce,token);
            if(previous!=null && previous.Subject!=subject)
                throw new InvalidOperationException(Localization.Text("Choose the original ChatGPT account or add another connection."));
            token.ThrowIfCancellationRequested();
            Write(id,new(callback.ClientId,subject,email,tokens));
            return AccountLabel(email);
        }
        finally{gate.Release();}
    }

    internal bool IsSignedIn(string id)
    {
        try{return Read(id)?.Tokens is {Refresh.Length:>0};}
        catch(IOException){return false;}
    }

    internal async Task<string> AccessToken(string id,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var registration=Read(id);
            if(registration?.Tokens==null)throw SignInRequired();
            var saved=registration.Tokens;
            if(saved.Expires>DateTimeOffset.UtcNow.AddMinutes(2))return saved.Access;
            using var exchange=await Post(TokenUrl,new Dictionary<string,string>{
                ["grant_type"]="refresh_token",["client_id"]=registration.ClientId,
                ["refresh_token"]=saved.Refresh,["resource"]=ChatGptPlanAttempt.Resource},CancellationToken.None);
            var renewed=ReadTokens(exchange.RootElement,saved.Scope);
            renewed=renewed with {Identity=saved.Identity};
            Write(id,registration with {Tokens=renewed});
            token.ThrowIfCancellationRequested();
            return renewed.Access;
        }
        finally{gate.Release();}
    }

    internal async Task<IReadOnlyList<ChatGptPlanModel>> Models(string id,CancellationToken token)
    {
        string access=await AccessToken(id,token);
        using var request=new HttpRequestMessage(HttpMethod.Get,ApiBase+"models");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode)throw AssistantTransport.ChatGptPlanError(response.StatusCode);
        using var json=await ReadJson(response,2*1024*1024,token);
        if(!json.RootElement.TryGetProperty("models",out var models) || models.ValueKind!=JsonValueKind.Array)
            throw new IOException(Localization.Text("ChatGPT returned an invalid model list."));
        return models.EnumerateArray().Where(model=>Field(model,"visibility")=="list").Select(model=>
            new ChatGptPlanModel(Field(model,"slug"),Field(model,"display_name")))
            .Where(model=>model.Id.Length is >0 and <=160 && model.Name.Length is >0 and <=160 &&
                !model.Id.Any(char.IsControl) && !model.Name.Any(char.IsControl))
            .DistinctBy(model=>model.Id).Take(300).ToArray();
    }

    internal async Task<bool> SignOut(string id,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var registration=Read(id);
            if(registration?.Tokens==null)return true;
            bool revoked=false;
            for(int attempt=0;attempt<2 && !revoked;attempt++)
            {
                if(attempt>0)await Task.Delay(500,token);
                try
                {
                    using var request=new HttpRequestMessage(HttpMethod.Post,Issuer+"/api/accounts/oauth/revoke"){
                        Content=new FormUrlEncodedContent(new Dictionary<string,string>{
                            ["token"]=registration.Tokens.Refresh,["token_type_hint"]="refresh_token",["client_id"]=registration.ClientId})};
                    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
                    revoked=response.StatusCode==HttpStatusCode.OK;
                    if(!revoked && (int)response.StatusCode<500)break;
                }
                catch(HttpRequestException){}
                catch(OperationCanceledException) when(!token.IsCancellationRequested){}
            }
            Write(id,registration with {Tokens=null});
            return revoked;
        }
        finally{gate.Release();}
    }

    private async Task<(string Subject,string Email)> VerifyIdentity(string identity,string clientId,string nonce,CancellationToken token)
    {
        using var response=await client.GetAsync(Issuer+"/.well-known/jwks.json",HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode)throw IdentityError();
        var jwks=await AssistantTransport.ReadBounded(response,256*1024,token);
        var validation=await new JsonWebTokenHandler().ValidateTokenAsync(identity,new TokenValidationParameters{
            ValidIssuer=Issuer,ValidAudience=clientId,
            IssuerSigningKeys=new JsonWebKeySet(Encoding.UTF8.GetString(jwks)).GetSigningKeys(),
            ValidateIssuerSigningKey=true,RequireSignedTokens=true,RequireExpirationTime=true,ValidateLifetime=true,
            ValidAlgorithms=new[]{SecurityAlgorithms.RsaSha256},ClockSkew=TimeSpan.FromSeconds(5)});
        if(!validation.IsValid || validation.SecurityToken is not JsonWebToken jwt ||
            !jwt.TryGetPayloadValue<string>("nonce",out var returnedNonce) || returnedNonce!=nonce ||
            string.IsNullOrWhiteSpace(jwt.Subject) || jwt.Subject.Length>256 ||
            !jwt.TryGetPayloadValue<long>("iat",out _))throw IdentityError();
        string email=jwt.TryGetPayloadValue<bool>("email_verified",out var verified) && verified &&
            jwt.TryGetPayloadValue<string>("email",out var address) && address?.Length<=254 && !address.Any(char.IsControl)?address:"";
        return (jwt.Subject,email);
    }

    private async Task<JsonDocument> Post(string url,Dictionary<string,string> fields,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,url){Content=new FormUrlEncodedContent(fields)};
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode)
        {
            if(fields["grant_type"]=="refresh_token" && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                throw SignInRequired();
            throw new IOException(Localization.Format("ChatGPT authorization failed (HTTP {0}).",(int)response.StatusCode));
        }
        return await ReadJson(response,128*1024,token);
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage reply,int limit,CancellationToken token)
        =>JsonDocument.Parse(await AssistantTransport.ReadBounded(reply,limit,token));

    private static Tokens ReadTokens(JsonElement data,string? previousScope=null)
    {
        string access=Field(data,"access_token"),refresh=Field(data,"refresh_token"),identity=Field(data,"id_token");
        string scope=Field(data,"scope");
        if(scope.Length==0)scope=previousScope??"";
        if(!scope.Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains(PlanScope,StringComparer.Ordinal))
            throw new InvalidOperationException(Localization.Text("Permission to use the ChatGPT plan was not granted."));
        if(!string.Equals(Field(data,"token_type"),"Bearer",StringComparison.OrdinalIgnoreCase) ||
            access.Length is 0 or >32000 || refresh.Length is 0 or >32000 || identity.Length>32000 ||
            previousScope==null && identity.Length==0 || access.Any(char.IsControl) || refresh.Any(char.IsControl) ||
            !data.TryGetProperty("expires_in",out var expires) || !expires.TryGetInt32(out var seconds) || seconds is <=0 or >86400)
            throw IdentityError();
        return new(access,refresh,identity,DateTimeOffset.UtcNow.AddSeconds(seconds),scope);
    }

    private static string Field(JsonElement data,string name)=>data.ValueKind==JsonValueKind.Object &&
        data.TryGetProperty(name,out var item) && item.ValueKind==JsonValueKind.String?item.GetString()??"":"";
    private static Exception SignInRequired()=>new InvalidOperationException(Localization.Text("Sign in to ChatGPT again in Sento settings."));
    private static Exception IdentityError()=>new IOException(Localization.Text("ChatGPT could not verify this sign-in."));
    private static string AccountLabel(string email)
    {
        int domain=email.IndexOf('@');
        return domain>0?"ChatGPT · "+email[0]+"…"+email[domain..]:"ChatGPT";
    }

    private string PathFor(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Invalid ChatGPT connection ID.",nameof(id));
        return Path.Combine(store.DirectoryPath,"chatgpt-plan-"+id+".protected");
    }
    private static byte[] Entropy(string id)=>Encoding.UTF8.GetBytes("SentoBar.ChatGptPlan:"+id);

    private Registration? Read(string id)
    {
        try
        {
            string path=PathFor(id);
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(file.Length is <1 or >262144)throw new IOException();
            byte[] protectedBytes=new byte[(int)file.Length];
            file.ReadExactly(protectedBytes);
            byte[] clear;
            try{clear=ProtectedData.Unprotect(protectedBytes,Entropy(id),DataProtectionScope.CurrentUser);}
            catch(CryptographicException)
            {
                // Previously encrypted profiles are copied byte-for-byte during migration.
                clear=ProtectedData.Unprotect(protectedBytes,Encoding.UTF8.GetBytes("NabyScape.ChatGptPlan:"+id),DataProtectionScope.CurrentUser);
            }
            try{return JsonSerializer.Deserialize<Registration>(clear)??throw new IOException();}
            finally{CryptographicOperations.ZeroMemory(clear);}
        }
        catch(FileNotFoundException){return null;}
        catch(DirectoryNotFoundException){return null;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {throw new IOException(Localization.Text("Could not read the protected ChatGPT session."));}
    }

    private void Write(string id,Registration registration)
    {
        string path=PathFor(id),staged=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        byte[] clear=JsonSerializer.SerializeToUtf8Bytes(registration);
        try
        {
            byte[] encrypted=ProtectedData.Protect(clear,Entropy(id),DataProtectionScope.CurrentUser);
            using(var stream=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                stream.Write(encrypted);
                stream.Flush(true);
            }
            File.Move(staged,path,true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            try{File.Delete(staged);}catch(IOException){}
        }
    }

    public void Dispose()=>client.Dispose();
}
