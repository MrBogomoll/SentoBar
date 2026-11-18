using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SentoBar;

internal sealed partial class AssistantTransport
{
    private async Task<AssistantReply> CompleteWithChatGptPlan(AssistantProvider provider,string? access,
        IReadOnlyList<AssistantMessage> messages,CancellationToken cancellationToken)
    {
        if(messages.Any(m=>m.Role=="tool" || m.Calls is {Count:>0} || m.ImageDataUrl!=null ||
            m.Role!="user" && m.Attachments is {Count:>0}))
            throw new InvalidOperationException(Localization.Text("ChatGPT plan chat accepts text and images attached by you."));
        ValidateInput(provider,messages,Array.Empty<AssistantTool>());
        if(string.IsNullOrEmpty(access) || access.Length>32000 || access.Any(char.IsControl))
            throw ChatGptPlanError(HttpStatusCode.Unauthorized);

        object Content(AssistantMessage message)
        {
            if(message.Attachments is not {Count:>0} attachments)return message.Text;
            var parts=new JsonArray(Text("input_text",message.Text));
            foreach(var file in attachments)
                parts.Add(new JsonObject{["type"]="input_image",["image_url"]=file.Source,["detail"]="auto"});
            return parts;
        }
        bool hasImages=messages.Any(m=>m.Attachments is {Count:>0});

        // The subscription endpoint takes streamed, stateless requests with no paid-API fallback.
        var body=new
        {
            model=provider.Model,
            instructions=string.Join("\n",messages.Where(m=>m.Role=="system").Select(m=>m.Text)),
            input=messages.Where(m=>m.Role is "user" or "assistant").Select(m=>new{role=m.Role,content=Content(m)}),
            store=false,
            stream=true
        };
        var payload=JsonSerializer.SerializeToUtf8Bytes(body);
        if(payload.Length>MaximumBodyBytes)throw new InvalidOperationException(Localization.Text("The assistant request is too large. Reduce the conversation or image size."));
        using var request=new HttpRequestMessage(HttpMethod.Post,ChatGptPlanSession.ApiBase+"responses"){
            Content=new ByteArrayContent(payload)};
        request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
            if(hasImages && (int)response.StatusCode is 400 or 413 or 415 or 422)
                throw new IOException(Localization.Text("ChatGPT rejected the message with images. Try a smaller image or another model."));
            if(!response.IsSuccessStatusCode)throw ChatGptPlanError(response.StatusCode);
            // A plan response can omit Content-Type. The SSE parser still requires
            // framed events and a response.completed event before accepting the reply.
            if(response.Content.Headers.ContentType is {} contentType &&
                !string.Equals(contentType.MediaType,"text/event-stream",StringComparison.OrdinalIgnoreCase))
                throw new IOException(Localization.Text("ChatGPT returned an invalid reply stream."));

            var answer=new StringBuilder();
            await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            await foreach(var (_,data) in SseFrames(stream,deadline.Token))
            {
                if(data=="[DONE]")break;
                using var document=JsonDocument.Parse(data);
                var json=document.RootElement;
                string kind=Get(json,"type");
                switch(kind)
                {
                    case "response.output_text.delta":
                    case "response.refusal.delta":
                        string delta=Get(json,"delta");
                        if(answer.Length+delta.Length>MaximumTextCharacters)throw new IOException(Localization.Text("The provider response exceeded its size limit."));
                        answer.Append(delta);
                        break;
                    case "response.completed":
                        if(answer.Length==0 && json.TryGetProperty("response",out var complete))
                        {
                            var parsed=Parse(provider,complete,Array.Empty<AssistantTool>());
                            answer.Append(parsed.Text);
                        }
                        if(string.IsNullOrWhiteSpace(answer.ToString()))throw new IOException(Localization.Text("ChatGPT returned no text."));
                        return new AssistantReply(answer.ToString(),Array.Empty<AssistantCall>());
                    case "response.failed":
                    case "error":
                        var error=json;
                        if(error.TryGetProperty("response",out var failed))error=failed;
                        if(error.TryGetProperty("error",out var details) && details.ValueKind==JsonValueKind.Object)error=details;
                        throw Get(error,"code") switch
                        {
                            "subscription_sharing_usage_limit_exceeded"=>ChatGptPlanError(HttpStatusCode.TooManyRequests),
                            "subscription_sharing_usage_unavailable"=>new IOException(Localization.Text("ChatGPT plan usage is temporarily unavailable.")),
                            _=>new IOException(Localization.Text("ChatGPT could not complete the reply."))
                        };
                    case "response.incomplete":
                        throw new IOException(Localization.Text("ChatGPT did not complete the reply."));
                }
            }
            throw new IOException(Localization.Text("ChatGPT did not complete the reply."));
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
        {throw new TimeoutException(Localization.Text("The ChatGPT reply timed out."));}
        catch(HttpRequestException)
        {throw new IOException(Localization.Text("Could not reach ChatGPT. Check your connection and try again."));}
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or DecoderFallbackException)
        {throw new IOException(Localization.Text("ChatGPT returned an invalid reply stream."));}
    }

    internal static IOException ChatGptPlanError(HttpStatusCode status)
    {
        string message=status switch
        {
            HttpStatusCode.Unauthorized=>"Sign in to ChatGPT again in Sento settings.",
            HttpStatusCode.Forbidden=>"This ChatGPT account cannot use the plan for this request.",
            HttpStatusCode.NotFound=>"The selected model is unavailable. Refresh the ChatGPT model list.",
            HttpStatusCode.TooManyRequests=>"ChatGPT usage limit reached. Check Settings → Usage in ChatGPT.",
            _=>"ChatGPT could not complete the request."
        };
        return new IOException(Localization.Text(message));
    }
}
