using AiPlatform.Application.Common.Models;

namespace AiPlatform.Infrastructure.Rag;

// শব্দ-ভিত্তিক (lexical) ranking — Okapi BM25। vector search অর্থ ধরে, এটা হুবহু শব্দ ধরে (নাম, সংখ্যা, "Shiplu")।
// ponytail: stemming নেই — "অফিসের" আর "অফিস" আলাদা শব্দ। বাংলা বিভক্তি-সমেত শব্দে তাই দুর্বল; নাম/ইংরেজি/সংখ্যায় ভালো।
public static class Bm25Ranker
{
    private const double K1 = 1.5;
    private const double B = 0.75;
    private const int MinTokenLength = 2;

    public static IReadOnlyList<RetrievedChunk> Rank(string query, IReadOnlyList<RetrievedChunk> documents, int topN)
    {
        var queryTerms = Tokenize(query).Distinct().ToList();
        if (queryTerms.Count == 0 || documents.Count == 0)
            return [];

        var tokenized = documents.Select(d => Tokenize(d.Text)).ToList();
        var averageLength = tokenized.Average(t => (double)t.Count);
        if (averageLength == 0)
            return [];

        // শব্দ কতগুলো document-এ আছে (document frequency)
        var documentFrequency = queryTerms.ToDictionary(
            term => term, term => tokenized.Count(tokens => tokens.Contains(term)));

        var scored = new List<RetrievedChunk>();
        for (var i = 0; i < documents.Count; i++)
        {
            var tokens = tokenized[i];
            var score = 0.0;
            foreach (var term in queryTerms)
            {
                var df = documentFrequency[term];
                if (df == 0) continue;

                var tf = tokens.Count(t => t == term);
                if (tf == 0) continue;

                var idf = Math.Log(1 + (documents.Count - df + 0.5) / (df + 0.5));
                score += idf * tf * (K1 + 1) / (tf + K1 * (1 - B + B * tokens.Count / averageLength));
            }

            if (score > 0)
                scored.Add(documents[i] with { Score = score });
        }

        return scored.OrderByDescending(c => c.Score).Take(topN).ToList();
    }

    // অক্ষর/সংখ্যা ছাড়া সবকিছুতে ভাঙে (বাংলা যুক্তাক্ষর-চিহ্নও letter/mark হিসেবে শব্দের ভেতরে থাকে)।
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && IsWordChar(text[i]);
            if (isWordChar)
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                if (i - start >= MinTokenLength)
                    tokens.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }
        return tokens;
    }

    // বাংলা স্বরচিহ্ন/হসন্ত (Mn/Mc) letter নয়, তাই আলাদা করে শব্দের অংশ ধরি — নইলে "কর্মদিবস" ভেঙে টুকরো হয়।
    private static bool IsWordChar(char c)
    {
        if (char.IsLetterOrDigit(c)) return true;
        var category = char.GetUnicodeCategory(c);
        return category is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark;
    }
}
