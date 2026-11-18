using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal static class ChatGptPlanTests
{
    internal static void Run(Action<string,Action> test)
    {
        test("ChatGPT sign-in binds the issued client to the loopback request and state",()=>
        {
            var redirect=new Uri("http://127.0.0.1:45712/auth/callback");
            var attempt=new ChatGptPlanAttempt("urn:uuid:fixture",null);
            var url=attempt.AuthorizationUri(redirect);
            Require(url.Host=="auth.openai.com" && url.Query.Contains("client_id=dynamic_agent_client") &&
                url.Query.Contains("agent_name_hint=NabyScape") && url.Query.Contains("code_challenge_method=S256"));
            Uri Callback(string query)=>new(redirect+"?"+query);
            Require(attempt.ReadCallback(Callback("state=wrong&code=fixture&client_id=oaiapp_fixture"))==null);
            Require(attempt.ReadCallback(Callback($"state={attempt.State}&code=fixture"))==null);
            Require(attempt.ReadCallback(Callback($"state={attempt.State}&code=one&code=two&client_id=oaiapp_fixture"))==null);
            var result=attempt.ReadCallback(Callback($"state={attempt.State}&code=fixture&client_id=oaiapp_fixture"));
            Require(result is {Code:"fixture",ClientId:"oaiapp_fixture",Declined:false});
            Require(attempt.ReadCallback(Callback($"state={attempt.State}&error=access_denied"))?.Declined==true);
        });
        test("ChatGPT plan verifies identity and persists only protected, rotated credentials",()=>Task.Run(async()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"NabyScape-PlanTest-"+Guid.NewGuid().ToString("N"));
            string id=Guid.NewGuid().ToString("N");
            var store=new Store(path);
            try
            {
                using var rsa=RSA.Create(2048);
                var fixture=new SessionFixture(rsa);
                Task<(Uri,ChatGptPlanCallback)> Approve(ChatGptPlanAttempt attempt,CancellationToken token)
                {
                    fixture.Nonce=attempt.Nonce;
                    fixture.Verifier=attempt.Verifier;
                    return Task.FromResult((new Uri("http://127.0.0.1:45712/auth/callback"),
                        new ChatGptPlanCallback("fixture-code","oaiapp_fixture",false)));
                }
                using var session=new ChatGptPlanSession(store,fixture,Approve);
                Require(await session.SignIn(id,CancellationToken.None)=="ChatGPT · f…@example.test");
                string protectedFile=Path.Combine(path,"chatgpt-plan-"+id+".protected");
                Require(!Encoding.UTF8.GetString(File.ReadAllBytes(protectedFile)).Contains("fixture-refresh"));
                var models=await session.Models(id,CancellationToken.None);
                Require(models.Count==1 && models[0].Id=="gpt-6-luna");
                using var reopened=new ChatGptPlanSession(store,new SessionFixture(rsa));
                Require(reopened.IsSignedIn(id) && await reopened.AccessToken(id,CancellationToken.None)=="fixture-access-rotated");
                fixture.UseInvalidNonce=true;
                bool rejected=false;
                try{await session.SignIn(id,CancellationToken.None);}
                catch(IOException){rejected=true;}
                Require(rejected && await reopened.AccessToken(id,CancellationToken.None)=="fixture-access-rotated");
                Require(await session.SignOut(id,CancellationToken.None) && fixture.Revoked && !reopened.IsSignedIn(id));
            }
            finally{Directory.Delete(path,true);}
        }).GetAwaiter().GetResult());
        test("Plan mode sends a streamed text request only to the official Responses endpoint",()=>Task.Run(async()=>
        {
            var fake=new StreamFixture("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hello\"}\n\n"+
                "data: {\"type\":\"response.completed\"}\n\n");
            using var transport=new AssistantTransport(fake);
            var provider=new AssistantProvider{Backend=ChatGptPlanSession.Backend,Protocol="Responses",
                BaseUrl="https://unrelated.example/",Model="gpt-6-luna"};
            var response=await transport.Complete(provider,"fixture-access",new[]{
                new AssistantMessage("system","Be brief"),new AssistantMessage("user","Say hello")},Array.Empty<AssistantTool>(),CancellationToken.None);
            Require(response.Text=="Hello" && fake.Endpoint=="https://api.openai.com/v1/responses" && fake.Authorized);
            using var json=JsonDocument.Parse(fake.Body);
            var root=json.RootElement;
            Require(!root.GetProperty("store").GetBoolean() && root.GetProperty("stream").GetBoolean() &&
                root.GetProperty("instructions").GetString()=="Be brief" && root.GetProperty("input").GetArrayLength()==1 &&
                !root.TryGetProperty("max_output_tokens",out _));
        }).GetAwaiter().GetResult());
        test("An interrupted ChatGPT stream cannot count as a successful reply",()=>Task.Run(async()=>
        {
            using var transport=new AssistantTransport(new StreamFixture("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n"));
            var provider=new AssistantProvider{Backend=ChatGptPlanSession.Backend,Protocol="Responses",Model="gpt-6-luna"};
            try{await transport.Complete(provider,"fixture-access",new[]{new AssistantMessage("user","Hello")},Array.Empty<AssistantTool>(),CancellationToken.None);}
            catch(IOException){return;}
            throw new Exception("The incomplete stream was accepted.");
        }).GetAwaiter().GetResult());
    }

    private static void Require(bool condition)
    {
        if(!condition)throw new Exception("ChatGPT plan assertion failed.");
    }

    private sealed class SessionFixture(RSA rsa) : HttpMessageHandler
    {
        internal string Nonce="",Verifier="";
        internal bool Revoked,UseInvalidNonce;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var url=request.RequestUri!;
            string form=request.Content==null?"":await request.Content.ReadAsStringAsync(token);
            switch(url.AbsoluteUri)
            {
                case "https://auth.openai.com/api/accounts/oauth/token":
                    Require(form.Contains("client_id=oaiapp_fixture"));
                    if(form.Contains("grant_type=refresh_token"))
                    {
                        Require(form.Contains("refresh_token=fixture-refresh"));
                        return Json(new { access_token="fixture-access-rotated",refresh_token="fixture-refresh-rotated",
                            token_type="Bearer",expires_in=3600 });
                    }
                    Require(form.Contains("grant_type=authorization_code") && form.Contains("code_verifier="+Verifier));
                    string identity=new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor{
                        Issuer="https://auth.openai.com",Audience="oaiapp_fixture",
                        Claims=new Dictionary<string,object>{{"sub","fixture-subject"},{"email","fixture@example.test"},
                            {"email_verified",true},{"nonce",UseInvalidNonce?"wrong-nonce":Nonce}},
                        SigningCredentials=new SigningCredentials(new RsaSecurityKey(rsa){KeyId="fixture"},SecurityAlgorithms.RsaSha256)
                    });
                    return Json(new { access_token="fixture-access",refresh_token="fixture-refresh",id_token=identity,
                        token_type="Bearer",expires_in=1,scope="chatgpt.tokens.use.direct" });
                case "https://auth.openai.com/.well-known/jwks.json":
                    var key=rsa.ExportParameters(false);
                    return Json(new { keys=new[]{new { kty="RSA",kid="fixture",use="sig",
                        n=Base64UrlEncoder.Encode(key.Modulus),e=Base64UrlEncoder.Encode(key.Exponent) }} });
                case "https://api.openai.com/v1/models":
                    Require(request.Headers.Authorization?.Parameter=="fixture-access-rotated");
                    return Json(new { models=new[]{new { visibility="list",slug="gpt-6-luna",display_name="Luna" },
                        new { visibility="hidden",slug="hidden",display_name="Hidden" }} });
                case "https://auth.openai.com/api/accounts/oauth/revoke":
                    Require(form.Contains("token=fixture-refresh-rotated") && form.Contains("client_id=oaiapp_fixture"));
                    Revoked=true;
                    return Json(new {});
                default:
                    throw new Exception("Unexpected request to "+url.AbsoluteUri);
            }
        }

        private static HttpResponseMessage Json(object body)=>new(HttpStatusCode.OK){
            Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};
    }

    private sealed class StreamFixture(string events) : HttpMessageHandler
    {
        internal string Body="",Endpoint="";
        internal bool Authorized;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Endpoint=request.RequestUri!.AbsoluteUri;
            Authorized=request.Headers.Authorization is {Scheme:"Bearer",Parameter:"fixture-access"};
            Body=await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK){Content=new StringContent(events,System.Text.Encoding.UTF8,"text/event-stream")};
        }
    }
}
