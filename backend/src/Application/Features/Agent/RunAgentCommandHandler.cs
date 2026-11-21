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
    AgentSettings settings,
    MultiQuestionRunner multiQuestionRunner)
    : IRequestHandler<RunAgentCommand, Result<AgentResponse>>
{
    private const int ScoreTopN = 8;
    private readonly AgentSettings _settings = settings;

    // assistant-এর পরিচয় + কোম্পানির ছোট স্থির তথ্য। শুধু এখানে দেওয়া তথ্যই বলবে — বাকি জানা নেই বলবে।
    private const string IdentityFacts =
        "Your name is DBZ AI. You were developed by Saiful Islam Pavel. " +
        "Company facts you may state when asked: The Databiz Software Ltd was founded in 1991; " +
        "its CEO is Rashed Kamal Hijal; its office is in Mirpur DOHS; its sister concern is CNEWS. ";

    // তথ্য-নিয়ম দুই রকম, কখনো একসাথে নয় — দুটো একসাথে থাকলে ছোট model দ্বন্দ্বে পড়ে "খুঁজে পাচ্ছি না" বলত।
    // ১) passage আগেই দেওয়া আছে (retrieval-first): সেখান থেকেই উত্তর; না থাকলে "উল্লেখ নেই"।
    private const string RuleWithPassages =
        "The user's message contains document passages already retrieved for the question. " +
        "Answer questions about the company, its people and its policies from those passages " +
        "(they may be in English; translate into Bengali). " +
        "If the passages do not contain the answer, say plainly that it is not specified in the documents — never guess. " +
        // "আজ resign করলে শেষ কর্মদিবস কবে?" — নিয়ম (নোটিশ দুই মাস) ডকুমেন্টে আছে, তারিখটা নেই; মডেল "তথ্য নেই" বলে থেমে
        // যেত। কিন্তু মডেলকে নিজে হিসাব করতে বললে ভুল তারিখ দেয় (২০২৬-১২-২০, ২০২৪-০২-২০ — সঠিক ২০২৭-০১-২০), তাই
        // নিয়মটা passage থেকে নেয়, তারিখ বের করে add_to_date tool (কোড)।
        "If the question needs a date worked out from a rule in the passages (for example the last working day if I resign " +
        "today, given a notice period), take the number from the passages and call add_to_date to get the exact date — " +
        "never calculate dates yourself. Then state the rule, the resulting date, and that it was calculated. " +
        "Do not answer 'not specified' only because the final date is not written in the passages. " +
        // passage-নিয়মটা এত জোরালো যে মডেল "জুলাইয়ের বিক্রি" প্রশ্নেও "নথিতে নেই" বলত; ERP-র প্রশ্ন আলাদা করে ডাকতে হবে।
        "BUT questions about sales, stock, warranty or orders are never answered by passages: " +
        "call the matching ERP tool (get_sales, get_stock, get_warranty) for those. ";

    // ২) passage নেই (খোঁজা বন্ধ বা কিছু মেলেনি): আগে tool, তারপর হার মানা। আগে এখানে সরাসরি "জানি না" বলতে
    // বলা ছিল — ছোট model কিছু না খুঁজেই ফিরিয়ে দিত, অথচ তথ্য ডকুমেন্টে/ERP-তে ছিল।
    private const string RuleWithoutPassages =
        "For every other question, never answer from memory and never say you lack information before checking: " +
        "first use the tool that fits (ERP tools for live figures, search_knowledge_base for documents and text) " +
        "and answer only from what the tools return. " +
        "Only if the tools return nothing relevant, say plainly that you do not have that information — never guess. ";

    private const string KnowledgeBaseToolName = "search_knowledge_base";

    private const string ToolCallTextCorrection =
        "You wrote a tool call as plain text instead of calling the tool. Do not write tool calls in your answer. " +
        "Call the tool now through the function-calling interface, then answer in Bengali using its result.";

    private static string BuildSystemPrompt(bool hasPassages, DateOnly today, string timeZone) =>
        $"You are DBZ AI, a helpful assistant with access to tools. {IdentityFacts}" +
        (hasPassages ? RuleWithPassages : RuleWithoutPassages) +
        $"Today's date is {today:yyyy-MM-dd} ({timeZone}). " +
        "Use tools when they help: ERP tools (get_sales, get_stock, get_warranty, …) for live database figures; " +
        "search_knowledge_base for uploaded documents and company policy text. " +
        "You may call multiple tools in one turn (e.g. sales numbers + a policy doc) and combine them " +
        "into a single clear answer. " +
        // ছোট model "জুলাই ২০২৬"-কে অস্পষ্ট ভেবে তারিখ জিজ্ঞেস করত (tool call-ই করত না) — তাই তারিখের নিয়ম স্পষ্ট ও জিজ্ঞাসার সীমা ছোট।
        "Dates: when the user names a month (with or without a year) or a relative period, work out the exact dates " +
        "yourself — a month means its first to last day, no year means the current year — and call the tool right away. " +
        "Never ask the user to confirm or supply dates. " +
        "Ask one short clarifying question only when two readings would need different tools or filters " +
        "(for example customers versus products); otherwise pick the most likely reading and answer. " +
        "Never invent numbers — if a tool returns no data, say so plainly instead of reporting zero. " +
        "Copy every number, amount, date, serial and invoice number from the tool result exactly as given. " +
        "Never round, approximate, convert, re-scale or reformat them, and never do arithmetic of your own on them — " +
        "if an amount already carries its currency and separators, reproduce that string character for character. " +
        "Plain text only: no Markdown (no **, no backticks, no # headings); use '-' for list items. " +
        "When you have enough information, give the final answer in Bengali.";

    // passage প্রশ্নের ঠিক পাশে (user message-এ) — system prompt-এর শেষে রাখলে ছোট model উপেক্ষা করত।
    // প্রশ্নের ঠিক আগের এক লাইন: system prompt-এর ERP নির্দেশ passage-ভরা user message-এ ফিকে হয়ে যেত
    // (মডেল "জুলাইয়ের বিক্রি" প্রশ্নে tool না ডেকে "নথিতে নেই" বলত)।
    private const string PassageUsageHint =
        "(Answer company/policy/people questions from the passages above. " +
        "For sales, stock, warranty or order figures, ignore the passages and call the matching ERP tool.)";

    private static string BuildUserMessage(string question, string? passages) =>
        passages is null ? question : $"{passages}\n\n{PassageUsageHint}\nQuestion: {question}";

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

        // এক বার্তায় একাধিক প্রশ্ন → প্রতিটার আলাদা run (নিচের বাকি অংশ সবসময় ঠিক এক প্রশ্ন সামলায়)
        if (!request.SkipSplit && _settings.SplitQuestions)
        {
            var split = QuestionSplitter.Split(request.Message, _settings.MaxQuestionsPerMessage);
            if (split.Questions.Count > 1)
                return await multiQuestionRunner.RunAsync(split, request, cancellationToken);
        }

        retrievalTrace.Begin(request.RetrievalMethod);

        var allTools = tools.ToList();
        var toolMap = allTools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        await toolCatalog.SyncAsync(allTools, cancellationToken);
        var promptTools = await toolCatalog.SelectForPromptAsync(allTools, request.Message, cancellationToken);
        var toolHits = await toolCatalog.SearchScoresAsync(request.Message, ScoreTopN, cancellationToken);

        var toolDefs = promptTools
            .Select(t => new ToolDefinition(t.Name, t.Description, t.ParametersSchema))
            .ToList();

        var preRetrieved = await PreRetrieveAsync(toolMap, request.Message, cancellationToken);

        var messages = new List<ChatMessage>
        {
            new("system", BuildSystemPrompt(preRetrieved is not null, AgentClock.Today(_settings), _settings.TimeZone)),
            new("user", BuildUserMessage(request.Message, preRetrieved)),
        };
        var toolsUsed = new List<string>();
        var llmMs = 0L;
        var correctedToolCallText = false;

        for (var iteration = 0; iteration < _settings.MaxIterations; iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await provider.CompleteWithToolsAsync(messages, toolDefs, cancellationToken);
            stopwatch.Stop();
            llmMs += stopwatch.ElapsedMilliseconds;

            usageRecorder.Record(new LlmUsageRecord(
                "agent", result.Provider, result.Model, result.InputTokens, result.OutputTokens, stopwatch.ElapsedMilliseconds));

            // model tool না ডেকে "add_to_date(months=2)" লিখে দিলে সেটা উত্তর নয় — একবার সংশোধন করে আবার চেষ্টা। একবারই:
            // আবার করলে অনন্ত চক্র হতো; তাই দ্বিতীয়বার যা আসে সেটাই যায়।
            if (result.ToolCalls.Count == 0 && !correctedToolCallText
                && ToolCallText.LooksLikeToolCall(result.Content, toolMap.Keys))
            {
                correctedToolCallText = true;
                messages.Add(new ChatMessage("assistant", result.Content ?? string.Empty));
                messages.Add(new ChatMessage("user", ToolCallTextCorrection));
                continue;
            }

            if (result.ToolCalls.Count == 0)
            {
                // আগে-থেকে-খোঁজা হলেও hits দেখাই (toolsUsed-এ নয় — ERP প্রশ্নে "tool চলেছে" দেখানো ভুল হতো)।
                var documentHits = preRetrieved is not null
                                   || toolsUsed.Contains(KnowledgeBaseToolName, StringComparer.OrdinalIgnoreCase)
                    ? retrievalTrace.DocumentHits
                    : null;
                return new AgentResponse(
                    result.Content ?? string.Empty,
                    toolsUsed,
                    llmMs,
                    toolHits.Count > 0 ? toolHits : null,
                    documentHits is { Count: > 0 } ? documentHits : null,
                    result.Provider,
                    result.Model,
                    documentHits is { Count: > 0 } ? retrievalTrace.MethodUsed : null);
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

    // retrieval-first: ডকুমেন্ট-খোঁজা tool-টাই প্রশ্ন দিয়ে সরাসরি চালাই (fallback/trace/formatting সব এক জায়গায় থাকে)।
    // কিছু না পেলে বা বন্ধ থাকলে null — তখন আগের মতোই মডেল নিজে tool ডাকবে।
    private async Task<string?> PreRetrieveAsync(
        IReadOnlyDictionary<string, ITool> toolMap, string question, CancellationToken cancellationToken)
    {
        if (!_settings.PreRetrieveDocuments || !toolMap.TryGetValue(KnowledgeBaseToolName, out var tool))
            return null;

        try
        {
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { query = question }));
            var passages = await tool.ExecuteAsync(args.RootElement, cancellationToken);
            var hits = retrievalTrace.DocumentHits;
            if (hits.Count == 0)
                return null;

            // Rerank চললে reranker-এর relevance score দিয়ে gate (অনেক নির্ভুল; ERP ≤০.০০৩ বনাম ডকুমেন্ট ≥০.০০৬৭)।
            var relevance = retrievalTrace.BestRerankScore;
            if (relevance is not null && _settings.PreRetrieveMinRerankScore > 0)
                return relevance < _settings.PreRetrieveMinRerankScore ? null : passages;

            // নইলে মূল vector cosine: hybrid-এ hits[0].Score RRF (অন্য মাপ), তাই সরাসরি তুলনা ভুল।
            // vector score না থাকলে (keyword fallback) gate প্রযোজ্য নয় — passage যায়।
            var similarity = retrievalTrace.BestVectorScore;
            return similarity is not null && similarity < _settings.PreRetrieveMinScore ? null : passages;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // retrieval ব্যর্থ হলে উত্তর থামে না; মডেল নিজে tool ডাকার সুযোগ পায়
        }
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
