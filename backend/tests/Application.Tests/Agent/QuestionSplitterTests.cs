using AiPlatform.Application.Features.Agent;
using Xunit;

namespace AiPlatform.Application.Tests.Agent;

// ব্যবহারকারী এক বার্তায় ১০–২০ প্রশ্ন দেয়; ছোট model-এর জন্য তা আলাদা করার নিয়মগুলো এখানে পাহারা দেওয়া।
public class QuestionSplitterTests
{
    [Fact]
    public void SingleQuestion_StaysOne()
    {
        var split = QuestionSplitter.Split("July 2026 er total sales koto?", 20);

        Assert.Equal(["July 2026 er total sales koto?"], split.Questions);
        Assert.Equal(0, split.Skipped);
    }

    // বাস্তব ঘটনা (2026-11-19): দুই লাইনের বার্তায় model একটা প্রশ্ন ফেলে দিচ্ছিল।
    [Fact]
    public void EachLine_IsOneQuestion()
    {
        var split = QuestionSplitter.Split("Who is Rashed Kamal?\nJuly 2026 er total sales koto? Buyer wise dekhao.", 20);

        Assert.Equal(
            ["Who is Rashed Kamal?", "July 2026 er total sales koto? Buyer wise dekhao."],
            split.Questions);
    }

    // এক লাইনে একটাই '?' — পরের "ক্রেতা অনুযায়ী দেখাও" আলাদা প্রশ্ন নয়, একই প্রশ্নের অংশ।
    [Fact]
    public void OneQuestionMark_KeepsTrailingInstructionTogether()
    {
        var split = QuestionSplitter.Split("কত বিক্রি? ক্রেতা অনুযায়ী দেখাও।", 20);

        Assert.Single(split.Questions);
    }

    [Fact]
    public void SeveralQuestionMarksOnOneLine_SplitAtEach_TailJoinsLast()
    {
        var split = QuestionSplitter.Split("CEO ke? Office kothay? Stock koto? Buyer wise dekhao.", 20);

        Assert.Equal(["CEO ke?", "Office kothay?", "Stock koto? Buyer wise dekhao."], split.Questions);
    }

    [Theory]
    [InlineData("1. প্রথম?\n2) দ্বিতীয়?\n- তৃতীয়?\n* চতুর্থ?\n• পঞ্চম?")]
    [InlineData("প্রথম?\r\n\r\nদ্বিতীয়?\n   \nতৃতীয়?\nচতুর্থ?\nপঞ্চম?")]   // CRLF ও ফাঁকা লাইন
    public void ListMarkersAndBlankLines_AreIgnored(string message)
    {
        var split = QuestionSplitter.Split(message, 20);

        Assert.Equal(["প্রথম?", "দ্বিতীয়?", "তৃতীয়?", "চতুর্থ?", "পঞ্চম?"], split.Questions);
    }

    [Fact]
    public void MoreThanLimit_KeepsFirstN_AndReportsSkipped()
    {
        var message = string.Join('\n', Enumerable.Range(1, 25).Select(i => $"প্রশ্ন {i}?"));

        var split = QuestionSplitter.Split(message, 20);

        Assert.Equal(20, split.Questions.Count);
        Assert.Equal("প্রশ্ন 1?", split.Questions[0]);
        Assert.Equal(5, split.Skipped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n \r\n ")]
    public void EmptyMessage_GivesNoQuestions(string message)
    {
        Assert.Empty(QuestionSplitter.Split(message, 20).Questions);
    }

    [Fact]
    public void OnlyQuestionMarks_DoesNotThrow()
    {
        var split = QuestionSplitter.Split("??", 20);

        Assert.Equal(["??"], split.Questions);
    }

    [Fact]
    public void NonPositiveLimit_IsTreatedAsOne()
    {
        var split = QuestionSplitter.Split("এক?\nদুই?", 0);

        Assert.Single(split.Questions);
        Assert.Equal(1, split.Skipped);
    }
}
