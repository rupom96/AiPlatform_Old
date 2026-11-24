using System.Text.RegularExpressions;

namespace AiPlatform.Application.Features.Agent;

// ছোট model (gemma4:e4b, reasoning ছাড়া) মাঝে মাঝে tool সত্যি না ডেকে উত্তরের জায়গায় tool-এর নাম লিখে দেয়:
//   "add_to_date(months=2)"  /  "get_sales(groupBy="product", month="2026-10")"
// ফলে user ওই লেখাটাই উত্তর হিসেবে পায় (বা model নিজে বানানো ভুল তারিখ বসায়)। এটা ধরার জন্য এই চেক।
public static class ToolCallText
{
    public static bool LooksLikeToolCall(string? content, IEnumerable<string> toolNames)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        foreach (var name in toolNames)
        {
            // "tool_name(" — নামের আগে অক্ষর/সংখ্যা/_ থাকলে অন্য শব্দের অংশ, তাই boundary দেখি।
            if (Regex.IsMatch(content, $@"(?<![\w]){Regex.Escape(name)}\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
        }
        return false;
    }
}
