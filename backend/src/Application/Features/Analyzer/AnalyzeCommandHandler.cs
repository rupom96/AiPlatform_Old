using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Analyzer;

// ⚠️ APP-SPECIFIC DEMO FEATURE — customer-message analyzer। structured-output (LLM→typed JSON)
// pattern-এর উদাহরণ; নতুন project-এ নিজের schema/prompt দিয়ে বদলাও বা পুরো Analyzer folder মুছে ফেলো
// (সাথে AnalyzerController + tests) — docs/template-guide.md-র strip তালিকা দেখো।
public class AnalyzeCommandHandler(IChatProviderResolver resolver)
    : IRequestHandler<AnalyzeCommand, Result<MessageAnalysis>>
{
    // Hybrid convention: instruction/structure English (format বেশি reliable), free-text output বাংলায়।
    private const string SystemPrompt =
        "You are a customer-support assistant. Analyze the customer message and respond ONLY with JSON " +
        "matching this shape: {\"sentiment\":\"positive|neutral|negative\",\"category\":\"short topic\"," +
        "\"priority\":\"low|medium|high\",\"summary\":\"one short sentence\"}. No extra text. " +
        "Write the \"category\" and \"summary\" values in Bengali (Bangla).";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<MessageAnalysis>> Handle(AnalyzeCommand request, CancellationToken cancellationToken)
    {
        var provider = resolver.Resolve(request.Provider);
        if (provider is null)
        {
            var requested = string.IsNullOrWhiteSpace(request.Provider) ? "default" : request.Provider;
            return Error.Validation("Analyzer.UnknownProvider", $"No configured LLM provider available for '{requested}'.");
        }

        var json = await provider.CompleteJsonAsync(
            [new ChatMessage("system", SystemPrompt), new ChatMessage("user", request.Text)], cancellationToken);

        MessageAnalysis? analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<MessageAnalysis>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return Error.Failure("Analyzer.ParseFailed", "The model returned invalid JSON.");
        }

        return analysis is null
            ? Error.Failure("Analyzer.ParseFailed", "The model returned empty analysis.")
            : analysis;
    }
}
