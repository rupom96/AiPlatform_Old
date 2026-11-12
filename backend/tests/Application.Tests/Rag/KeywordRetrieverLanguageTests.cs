using AiPlatform.Infrastructure.Rag;
using Xunit;

namespace AiPlatform.Application.Tests.Rag;

// KB বাংলায়, keyword retriever হুবহু substring মেলায় — তাই ইংরেজি query-তে শূন্য ফেরত আসে।
// এটাই ছিল cerebras-এর "তথ্য পাওয়া যায়নি" (2026-07-25)। আচরণটা এখানে লিখে রাখি যাতে কেউ
// পরে ভুল করে "keyword-ই যথেষ্ট" ধরে না নেয় — এই শূন্যই KnowledgeBaseSearchTool-এর
// hybrid fallback-এর ট্রিগার।
public class KeywordRetrieverLanguageTests
{
    private readonly KeywordRetriever _retriever = new();

    [Fact]
    public async Task BengaliQuery_FindsWarrantyPassages()
    {
        var chunks = await _retriever.RetrieveAsync("ওয়ারেন্টি বাতিলের কারণ কী?", 3, CancellationToken.None);

        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Text.Contains("বাতিল"));
    }

    [Fact]
    public async Task EnglishQuery_FindsNothing_SoFallbackMustHandleIt()
    {
        var chunks = await _retriever.RetrieveAsync("what voids the warranty", 3, CancellationToken.None);

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task WarrantyDuration_IsNotInTheKnowledgeBase()
    {
        // মেয়াদ ERP DB-তে (get_warranty)। KB-তে সংখ্যা ঢুকলে দুই উৎস পরস্পরবিরোধী হবে।
        var chunks = await _retriever.RetrieveAsync("ওয়ারেন্টি কতদিনের মেয়াদ?", 5, CancellationToken.None);

        Assert.DoesNotContain(chunks, c => c.Text.Contains("১ বছর") || c.Text.Contains("১২ মাস"));
    }
}
