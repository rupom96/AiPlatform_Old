using System.Text.RegularExpressions;

namespace AiPlatform.Application.Features.Agent;

// এক বার্তায় অনেক প্রশ্ন (১০–২০টা) এলে আলাদা করে — প্রতিটার আলাদা agent run হয়, কারণ ছোট model এক loop-এ
// এতগুলো tool call একসাথে সামলাতে পারে না। LLM লাগে না: নিয়ম সরল ও অনুমানযোগ্য।
//   ১) প্রতিটা অ-ফাঁকা লাইন = এক প্রশ্ন (আগের "1." / "-" / "*" তালিকা-চিহ্ন বাদ)।
//   ২) এক লাইনে একাধিক '?' থাকলে প্রতিটা '?'-তে ভাগ; শেষ '?'-এর পরের লেখা (যেমন "Buyer wise dekhao.") শেষ প্রশ্নের সাথে থাকে।
//   ৩) এক লাইনে একটাই '?' থাকলে ভাগ হয় না — "কত বিক্রি? ক্রেতা অনুযায়ী দেখাও।" এক প্রশ্ন।
// লাইন/'?' ছাড়া এক অনুচ্ছেদে লেখা অনেক প্রশ্ন ভাগ হয় না (তখন model-ই সামলায়)।
public static partial class QuestionSplitter
{
    [GeneratedRegex(@"^\s*(?:\d+\s*[.)]|[-*•])\s+")]
    private static partial Regex ListMarker();

    private static readonly char[] QuestionMarks = ['?', '？'];

    public static QuestionSplit Split(string message, int maxQuestions)
    {
        var limit = Math.Max(1, maxQuestions);
        var all = new List<string>();

        foreach (var rawLine in message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = ListMarker().Replace(rawLine, string.Empty).Trim();
            if (line.Length > 0)
                all.AddRange(SplitAtQuestionMarks(line));
        }

        return all.Count <= limit
            ? new QuestionSplit(all, 0)
            : new QuestionSplit(all.Take(limit).ToList(), all.Count - limit);
    }

    private static IEnumerable<string> SplitAtQuestionMarks(string line)
    {
        if (line.Count(c => Array.IndexOf(QuestionMarks, c) >= 0) < 2)
            return [line];

        var pieces = new List<string>();
        var start = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (Array.IndexOf(QuestionMarks, line[i]) < 0)
                continue;

            var piece = line[start..(i + 1)].Trim();
            if (piece.Trim(QuestionMarks).Trim().Length > 0)
                pieces.Add(piece);
            start = i + 1;
        }

        if (pieces.Count == 0)
            return [line];   // শুধু "??" ধরনের লাইন — ভাগ করার মতো কিছু নেই

        var tail = line[start..].Trim();
        if (tail.Length > 0)
            pieces[^1] = $"{pieces[^1]} {tail}";

        return pieces;
    }
}
