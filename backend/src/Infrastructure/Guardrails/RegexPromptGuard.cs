using System.Text.RegularExpressions;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Guardrails;

// regex-ভিত্তিক guard: config injection-pattern match → block; PII (email/phone/cc) → redact।
// ponytail: regex যথেষ্ট প্রথম defense; দরকার হলে পরে LLM-classifier guard-এ upgrade।
public class RegexPromptGuard : IPromptGuard
{
    // PII — code-এ fixed (universal format)। redacted token দিয়ে replace।
    private static readonly (Regex Pattern, string Token)[] PiiRules =
    [
        (new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled), "[REDACTED_EMAIL]"),
        // credit-card: 13-16 digit, optional space/dash separator; `+`-prefixed নয় (ওটা phone)
        (new Regex(@"(?<!\+)\b(?:\d[ -]?){13,16}\b", RegexOptions.Compiled), "[REDACTED_CC]"),
        // phone: +country সহ বা ছাড়া 10-15 digit (separator সহ)
        (new Regex(@"(?<!\w)\+?\d[\d ().-]{8,}\d(?!\w)", RegexOptions.Compiled), "[REDACTED_PHONE]"),
    ];

    private readonly GuardrailsSettings _settings;
    private readonly Regex[] _injectionPatterns;

    public RegexPromptGuard(IOptions<GuardrailsSettings> options)
    {
        _settings = options.Value;
        _injectionPatterns = _settings.InjectionPatterns
            .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            .ToArray();
    }

    public bool IsEnabled => _settings.Enabled;

    public GuardResult Check(string input)
    {
        var match = _injectionPatterns.FirstOrDefault(r => r.IsMatch(input));
        if (match is not null)
            return GuardResult.Block("Input rejected: possible prompt-injection attempt.");

        if (!_settings.RedactPii)
            return GuardResult.Allow(input);

        var safe = input;
        foreach (var (pattern, token) in PiiRules)
            safe = pattern.Replace(safe, token);

        return GuardResult.Allow(safe);
    }
}
