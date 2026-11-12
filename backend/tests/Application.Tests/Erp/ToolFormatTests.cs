using System.Globalization;
using Xunit;

namespace AiPlatform.Application.Tests.Erp;

// 2026-11-05: get_sales ফেরত দিয়েছিল 2028479.00, groq উত্তরে লিখল "১৯,৯৯,৯৯৯" —
// নিজে গোল করতে গিয়ে ভুল সংখ্যা। অঙ্ক আগেই সাজানো থাকলে তোলা ছাড়া উপায় থাকে না।
public class ToolFormatTests
{
    [Theory]
    [InlineData("2028479", "2,028,479 BDT")]
    [InlineData("305000", "305,000 BDT")]
    [InlineData("1593", "1,593 BDT")]
    [InlineData("0", "0 BDT")]
    public void WholeAmounts_GetThousandSeparatorsAndNoDecimals(string amount, string expected)
        => Assert.Equal(expected, Money(decimal.Parse(amount, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("1234.50", "1,234.50 BDT")]
    [InlineData("0.75", "0.75 BDT")]
    public void FractionalAmounts_KeepTwoDecimals(string amount, string expected)
        => Assert.Equal(expected, Money(decimal.Parse(amount, CultureInfo.InvariantCulture)));

    // ".00" লেজ রাখলে মডেল ওটাকে অর্থবহ ভেবে আরও "সরল" করতে যেত।
    [Fact]
    public void TrailingZeroDecimals_AreDropped()
        => Assert.Equal("2,028,479 BDT", Money(2028479.00m));

    // process-এর culture বদলালেও একই প্রশ্নে একই সংখ্যা আসতে হবে।
    [Fact]
    public void Formatting_DoesNotFollowTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // de-DE-তে separator উল্টো (1.234,50) — InvariantCulture না দিলে এখানেই ধরা পড়ত।
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1,234.50 BDT", Money(1234.50m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ToolFormat internal (Infrastructure-এর ভেতরের helper) — reflection-এ ডাকি,
    // শুধু test-এর জন্য public করার চেয়ে এটাই কম ক্ষতিকর (LlmJsonTests-এর মতোই)।
    private static string Money(decimal amount)
    {
        var type = typeof(AiPlatform.Infrastructure.Agent.GetSalesTool).Assembly
            .GetType("AiPlatform.Infrastructure.Agent.ToolFormat")!;

        return (string)type.GetMethod("Money")!.Invoke(null, [amount])!;
    }
}
