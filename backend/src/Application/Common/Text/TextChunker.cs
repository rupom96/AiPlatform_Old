using System.Text;

namespace AiPlatform.Application.Common.Text;

// বড় লেখা ছোট overlapping টুকরো করে (শব্দ-সীমানায় কাটে, overlap রাখে যেন context না ভাঙে)।
public static class TextChunker
{
    private const int MaxHeadingLevel = 6;

    public static IReadOnlyList<string> Chunk(string text, int maxChars = 200, int overlapWords = 6)
        => Build(text, maxChars, overlapWords, title: null);

    // একই কাটা, কিন্তু প্রতিটা chunk-এর মাথায় তার প্রসঙ্গ বসিয়ে দেয়:
    //   [Databiz_RAG_Knowledge_Base > 1. Company Overview] - **Chief Executive Officer:** ...
    //
    // কেন দরকার: embedding শুধু ঐ ২০০ অক্ষরটুকুই দেখে। "Chief Executive Officer: Rashed Kamal
    // Hijal" লাইনে কোম্পানির নামই নেই, তাই "Who is the CEO of Databiz?" প্রশ্নের সাথে অর্ধেক
    // মিলত — আর নাম-ভরা অথচ তথ্যহীন chunk (YAML header) ওকে হারিয়ে দিত। মাথায় প্রসঙ্গ
    // থাকলে প্রশ্নের দুই অর্ধেকই এক chunk-এ পাওয়া যায়।
    //
    // prefix বাজেটের বাইরে — নইলে প্রসঙ্গ যোগ করতে গিয়ে মূল লেখা কমে যেত।
    public static IReadOnlyList<string> ChunkWithContext(
        string text, string title, int maxChars = 200, int overlapWords = 6)
        => Build(text, maxChars, overlapWords, title);

    private static IReadOnlyList<string> Build(string text, int maxChars, int overlapWords, string? title)
    {
        var result = new List<string>();
        var current = new List<string>();
        var length = 0;
        var headings = new string?[MaxHeadingLevel];
        // chunk যেখানে *শুরু* হয়েছিল সেই heading — বেশির ভাগ লেখা ঐ অংশেরই।
        var chunkPrefix = title is null ? null : Prefix(title, headings);

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            if (title is not null && TryReadHeading(trimmed, out var level, out var heading))
            {
                // নতুন section শুরু — চলতি chunk এখানেই শেষ করি। নইলে এক chunk দুই section
                // জুড়ে বসত আর prefix-এ কেবল প্রথমটার নাম থাকত, অর্থাৎ প্রসঙ্গটাই মিথ্যা হতো।
                if (current.Count > 0)
                {
                    result.Add(Join(chunkPrefix, current));
                    current = [];
                    length = 0;
                }

                headings[level - 1] = heading;
                for (var deeper = level; deeper < MaxHeadingLevel; deeper++)
                    headings[deeper] = null;

                // heading লাইনটা body-তে যোগ করি না — prefix-এ তো আছেই।
                chunkPrefix = Prefix(title, headings);
                continue;
            }

            foreach (var word in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Count == 0 && title is not null)
                    chunkPrefix = Prefix(title, headings);

                current.Add(word);
                length += word.Length + 1;
                if (length < maxChars)
                    continue;

                result.Add(Join(chunkPrefix, current));
                current = current.Skip(Math.Max(0, current.Count - overlapWords)).ToList();
                length = current.Sum(w => w.Length + 1);
                if (title is not null)
                    chunkPrefix = Prefix(title, headings);
            }
        }

        if (current.Count > 0)
            result.Add(Join(chunkPrefix, current));

        return result;
    }

    private static string Join(string? prefix, List<string> words)
        => prefix is null ? string.Join(" ", words) : prefix + string.Join(" ", words);

    // "## 2. CEO Profile" → level 2, "2. CEO Profile"। '#' ছাড়া বা "#hashtag" হলে false।
    private static bool TryReadHeading(string line, out int level, out string heading)
    {
        level = 0;
        heading = string.Empty;

        var trimmed = line.TrimStart();
        while (level < trimmed.Length && trimmed[level] == '#')
            level++;

        if (level is 0 or > MaxHeadingLevel || level >= trimmed.Length || trimmed[level] != ' ')
            return false;

        heading = trimmed[(level + 1)..].Trim().Trim('#').Trim();
        return heading.Length > 0;
    }

    private static string Prefix(string title, string?[] headings)
    {
        var builder = new StringBuilder("[").Append(title);
        foreach (var heading in headings)
        {
            if (heading is not null)
                builder.Append(" > ").Append(heading);
        }
        return builder.Append("] ").ToString();
    }
}
