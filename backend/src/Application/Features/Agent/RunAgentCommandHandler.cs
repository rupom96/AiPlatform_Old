using System.Diagnostics;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Agent;

// agentic loop: LLM-কে tools দিই → tool_calls এলে execute → feed back → content এলে থামি।
// Tool menu: IToolCatalog দিয়ে Qdrant থেকে relevant subset (অনেক tool-এ prompt ছোট রাখে)।
public class RunAgentCommandHandler(
    IChatProviderResolver resolver,
    IEnumerable<ITool> tools,
    IToolCatalog toolCatalog,
    IAgentRetrievalTrace retrievalTrace,
    ILlmUsageRecorder usageRecorder,
    AgentSettings settings)
    : IRequestHandler<RunAgentCommand, Result<AgentResponse>>
{
    private const int ScoreTopN = 8;
    private readonly AgentSettings _settings = settings;

    private static string BuildSystemPrompt() =>
        $"You are a helpful assistant with access to tools. Today's date is {DateTime.UtcNow:yyyy-MM-dd} (UTC). " +
        "When the user names a month or day without a year, assume the current year. " +
        "Use tools when they help: ERP tools (get_sales, get_stock, get_warranty, …) for live database figures; " +
        "search_knowledge_base for uploaded documents and company policy text. " +
        "You may call multiple tools in one turn (e.g. sales numbers + a policy doc) and combine them " +
        "into a single clear answer. " +
        "Never invent numbers — if a tool returns no data, say so plainly instead of reporting zero. " +
        "Copy every number, amount, date, serial and invoice number from the tool result exactly as given. " +
        "Never round, approximate, convert, re-scale or reformat them, and never do arithmetic of your own on them — " +
        "if an amount already carries its currency and separators, reproduce that string character for character. " +
        "If the question is ambiguous in a way that changes which tool arguments you would use, " +
        "ask the user one short clarifying question instead of guessing. " +
        "When you have enough information, give the final answer in Bengali.";

    public async Task<Result<AgentResponse>> Handle(RunAgentCommand request, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
            return Error.Validation("Agent.Disabled", "Agent feature is disabled.");

        if (resolver.Resolve(request.Provider, request.Model) is not IToolCallingProvider provider)
        {
            var requested = string.IsNullOrWhiteSpace(request.Provider) ? "default" : request.Provider;
            return Error.Validation("Agent.NoToolSupport",
                $"Provider '{requested}' does not support tool-calling. Use an OpenAI-compatible provider (e.g. groq).");
        }

        retrievalTrace.Clear();

        var allTools = tools.ToList();
        var toolMap = allTools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        await toolCatalog.SyncAsync(allTools, cancellationToken);
        var promptTools = await toolCatalog.SelectForPromptAsync(allTools, request.Message, cancellationToken);
        var toolHits = await toolCatalog.SearchScoresAsync(request.Message, ScoreTopN, cancellationToken);

        var toolDefs = promptTools
            .Select(t => new ToolDefinition(t.Name, t.Description, t.ParametersSchema))
            .ToList();

        var messages = new List<ChatMessage>
        {
            new("system", BuildSystemPrompt()),
            new("user", request.Message),
        };
        var toolsUsed = new List<string>();
        var llmMs = 0L;

        for (var iteration = 0; iteration < _settings.MaxIterations; iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await provider.CompleteWithToolsAsync(messages, toolDefs, cancellationToken);
            stopwatch.Stop();
            llmMs += stopwatch.ElapsedMilliseconds;

            usageRecorder.Record(new LlmUsageRecord(
                "agent", result.Provider, result.Model, result.InputTokens, result.OutputTokens, stopwatch.ElapsedMilliseconds));

            if (result.ToolCalls.Count == 0)
            {
                var documentHits = toolsUsed.Contains("search_knowledge_base", StringComparer.OrdinalIgnoreCase)
                    ? retrievalTrace.DocumentHits
                    : null;
                return new AgentResponse(
                    result.Content ?? string.Empty,
                    toolsUsed,
                    llmMs,
                    toolHits.Count > 0 ? toolHits : null,
                    documentHits is { Count: > 0 } ? documentHits : null);
            }

            messages.Add(new ChatMessage("assistant", result.Content ?? string.Empty, ToolCalls: result.ToolCalls));

            foreach (var call in result.ToolCalls)
            {
                toolsUsed.Add(call.Name);
                var output = await ExecuteToolAsync(toolMap, call, cancellationToken);
                messages.Add(new ChatMessage("tool", output, ToolCallId: call.Id));
            }
        }

        return Error.Failure("Agent.MaxIterations",
            $"Agent did not finish within {_settings.MaxIterations} steps.");
    }

    private static async Task<string> ExecuteToolAsync(
        IReadOnlyDictionary<string, ITool> toolMap, ToolCall call, CancellationToken cancellationToken)
    {
        if (!toolMap.TryGetValue(call.Name, out var tool))
            return $"Unknown tool: {call.Name}";

        try
        {
            using var args = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            return await tool.ExecuteAsync(args.RootElement, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Tool '{call.Name}' failed: {ex.Message}";
        }
    }
}
