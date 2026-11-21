using System.Text.Json;
using AiPlatform.Application.Features.Agent;
using AiPlatform.Infrastructure.Agent;
using Xunit;

namespace AiPlatform.Application.Tests.Agent;

// তারিখের হিসাব কোডে — ছোট model "২০২৬-১১-২১ + ২ মাস"-এ ভুল তারিখ (২০২৬-১২-২০, ২০২৪-০২-২০) দিয়েছিল।
public class AddToDateToolTests
{
    // বাংলাদেশে ২০২৬-১১-২১ ০২:৩০ = UTC ২০২৬-১১-২০ ২০:৩০: UTC ধরলে "আজ" এক দিন পিছিয়ে থাকে (আসল বাগ)।
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 20, 20, 30, 0, TimeSpan.Zero);

    private static AddToDateTool Tool(string timeZone = "Asia/Dhaka") =>
        new(new AgentSettings { TimeZone = timeZone }, new FixedClock(NowUtc));

    private static Task<string> Run(AddToDateTool tool, string json) =>
        tool.ExecuteAsync(JsonDocument.Parse(json).RootElement, CancellationToken.None);

    [Fact]
    public async Task TwoMonthsFromToday_IsComputedExactly_InTheConfiguredTimeZone()
    {
        var output = await Run(Tool(), """{"months":2}""");

        Assert.Contains("2026-11-21 +2 month(s) = 2027-01-21 (Thursday)", output);
        Assert.Contains("Today in Asia/Dhaka is 2026-11-21", output);
    }

    [Fact]
    public async Task UtcTimeZone_GivesTheEarlierDate()
    {
        var output = await Run(Tool("UTC"), """{"months":2}""");

        Assert.Contains("2026-11-20 +2 month(s) = 2027-01-20", output);
    }

    [Fact]
    public async Task ExplicitStartDate_IsUsed_AndMonthEndIsClamped()
    {
        var output = await Run(Tool(), """{"startDate":"2026-01-31","months":1}""");

        Assert.Contains("2026-01-31 +1 month(s) = 2026-02-28", output);
    }

    [Fact]
    public async Task MonthsAndDays_AreCombined_AndNegativeGoesBack()
    {
        var output = await Run(Tool(), """{"startDate":"2026-03-15","months":-1,"days":10}""");

        Assert.Contains("2026-03-15 -1 month(s) +10 day(s) = 2026-02-25", output);
    }

    [Fact]
    public async Task NumbersSentAsStrings_AreAccepted()
    {
        var output = await Run(Tool(), """{"months":"2","days":"0"}""");

        Assert.Contains("= 2027-01-21", output);
    }

    [Theory]
    [InlineData("""{}""", "Provide months and/or days")]
    [InlineData("""{"months":0,"days":0}""", "Provide months and/or days")]
    [InlineData("""{"months":1.5}""", "months must be a whole number")]
    [InlineData("""{"months":"two"}""", "months must be a whole number")]
    [InlineData("""{"months":99999}""", "months must be a whole number")]
    [InlineData("""{"days":9999999}""", "days must be a whole number")]
    [InlineData("""{"startDate":"21/11/2026","months":1}""", "startDate must be a valid date")]
    [InlineData("""{"startDate":"2026-02-30","months":1}""", "startDate must be a valid date")]
    [InlineData("""{"startDate":20261121,"months":1}""", "startDate must be a valid date")]
    public async Task InvalidInput_GivesAClearMessage_NotAnException(string json, string expected)
    {
        var output = await Run(Tool(), json);

        Assert.Contains(expected, output);
    }

    [Fact]
    public async Task OutOfRangeResult_IsReportedNotThrown()
    {
        var output = await Run(Tool(), """{"startDate":"9999-12-01","months":12}""");

        Assert.Contains("out of the supported range", output);
    }

    [Fact]
    public async Task BlankStartDate_MeansToday()
    {
        var output = await Run(Tool(), """{"startDate":"","days":1}""");

        Assert.Contains("2026-11-21 +1 day(s) = 2026-11-22", output);
    }

    [Fact]
    public void Metadata_TellsTheModelToUseItForDateMath()
    {
        var tool = Tool();

        Assert.Equal("add_to_date", tool.Name);
        Assert.Contains("Never calculate dates yourself", tool.Description);
    }

    [Fact]
    public void AgentClock_UnknownTimeZone_FallsBackToUtc()
    {
        var today = AgentClock.Today(new AgentSettings { TimeZone = "Not/AZone" }, new FixedClock(NowUtc));

        Assert.Equal(new DateOnly(2026, 11, 20), today);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
