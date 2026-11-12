using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiPlatform.Application.Common.Exceptions;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;

namespace AiPlatform.Infrastructure.Llm;

// OpenAI-compatible (Groq, Cerebras, OpenRouter, Mistral, GitHub Models) — একই format, শুধু URL/key/model ভিন্ন।
public class OpenAiCompatibleChatProvider(IHttpClientFactory httpClientFactory, string name, ProviderConfig config)
    : IChatProvider, IToolCallingProvider
{
    public string Name => name;

    public async Task<ChatResult> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = config.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException(Name, (int)response.StatusCode, raw);

        using var doc = LlmJson.Parse(Name, raw, (int)response.StatusCode);
        var text = doc.RootElement
            .GetProperty("choices")[0].GetProperty("message")
            .GetProperty("content").GetString() ?? string.Empty;

        var (inputTokens, outputTokens) = ReadUsage(doc.RootElement);
        return new ChatResult(text, Name, config.Model, inputTokens, outputTokens);
    }

    private static (int input, int output) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
            return (0, 0);
        var input = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
        var output = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
        return (input, output);
    }

    public async Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = config.Model,
            response_format = new { type = "json_object" },   // JSON বাধ্যতামূলক
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException(Name, (int)response.StatusCode, raw);

        using var doc = LlmJson.Parse(Name, raw, (int)response.StatusCode);
        return doc.RootElement
            .GetProperty("choices")[0].GetProperty("message")
            .GetProperty("content").GetString() ?? "{}";
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new
        {
            model = config.Model,
            stream = true,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException(Name, (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var json = line["data:".Length..].Trim();
            if (json.Length == 0) continue;
            if (json == "[DONE]") break;

            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() > 0 &&
                choices[0].GetProperty("delta").TryGetProperty("content", out var content))
            {
                var text = content.GetString();
                if (!string.IsNullOrEmpty(text)) yield return text;
            }
        }
    }

    // function-calling — tools পাঠাই; response-এ tool_calls থাকলে ফেরত দিই (agent execute করবে), নইলে content।
    public async Task<ChatToolResult> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = config.Model,
            tool_choice = "auto",
            tools = tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.ParametersSchema }
            }).ToArray(),
            messages = messages.Select(SerializeMessage).ToArray()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException(Name, (int)response.StatusCode, raw);

        using var doc = LlmJson.Parse(Name, raw, (int)response.StatusCode);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;

        var toolCalls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                var fn = call.GetProperty("function");
                toolCalls.Add(new ToolCall(
                    call.GetProperty("id").GetString() ?? string.Empty,
                    fn.GetProperty("name").GetString() ?? string.Empty,
                    fn.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}"));
            }
        }

        var (inputTokens, outputTokens) = ReadUsage(doc.RootElement);
        return new ChatToolResult(content, toolCalls, Name, config.Model, inputTokens, outputTokens);
    }

    // OpenAI wire-format: assistant tool-call ও tool-result message আলাদা shape।
    private static object SerializeMessage(ChatMessage m)
    {
        if (m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
            return new
            {
                role = "assistant",
                content = string.IsNullOrEmpty(m.Content) ? null : m.Content,
                tool_calls = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.ArgumentsJson }
                }).ToArray()
            };

        if (m.Role == "tool")
            return new { role = "tool", tool_call_id = m.ToolCallId, content = m.Content };

        return new { role = m.Role, content = m.Content };
    }
}
