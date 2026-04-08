// Copyright (c) Microsoft. All rights reserved.

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotChat.WebApi.Services;

/// <summary>
/// An HTTP delegating handler that patches request bodies for newer OpenAI models
/// (e.g. gpt-5.1, o1, o3) which do not support certain legacy parameters:
/// <list type="bullet">
///   <item><c>max_tokens</c> → renamed to <c>max_completion_tokens</c></item>
///   <item><c>stop</c> → removed (not supported)</item>
/// </list>
/// </summary>
internal sealed class MaxTokensPatchHandler : DelegatingHandler
{
    public MaxTokensPatchHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            string? mediaType = request.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "application/json", System.StringComparison.OrdinalIgnoreCase))
            {
                string body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                bool needsPatch = body.Contains("\"max_tokens\"", System.StringComparison.Ordinal)
                               || body.Contains("\"stop\"", System.StringComparison.Ordinal);

                if (needsPatch)
                {
                    var node = JsonNode.Parse(body);
                    if (node is JsonObject obj)
                    {
                        // Rename max_tokens → max_completion_tokens
                        if (obj.TryGetPropertyValue("max_tokens", out var maxTokensValue))
                        {
                            obj.Remove("max_tokens");
                            obj["max_completion_tokens"] = maxTokensValue?.DeepClone();
                        }

                        // Remove stop sequences (not supported by newer models)
                        obj.Remove("stop");

                        body = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    }

                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
