namespace AiPlatform.Application.Common.Models;

// prompt guard-এর ফল। Blocked হলে Reason সেট থাকে; নইলে SafeInput = redacted input।
public record GuardResult(bool Blocked, string? Reason, string SafeInput)
{
    public static GuardResult Allow(string safeInput) => new(false, null, safeInput);
    public static GuardResult Block(string reason) => new(true, reason, string.Empty);
}
