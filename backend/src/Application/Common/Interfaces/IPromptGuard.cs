using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// LLM-এ যাওয়ার আগে input পরীক্ষা: prompt-injection হলে block, PII থাকলে redact।
// output নয় — trust boundary হলো user→LLM input।
public interface IPromptGuard
{
    bool IsEnabled { get; }

    // Blocked=true হলে caller LLM call করবে না। নইলে SafeInput (PII-redacted) forward করবে।
    GuardResult Check(string input);
}
