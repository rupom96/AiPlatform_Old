using AiPlatform.Infrastructure.Llm;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiPlatform.Application.Tests.Routing;

public class HeuristicModelRouterTests
{
    private static HeuristicModelRouter Build() => new(Options.Create(new RoutingSettings
    {
        Enabled = true,
        CheapProvider = "ollama",
        StrongProvider = "groq",
        WordThreshold = 30,
        BanglaWordThreshold = 12,
        ComplexKeywords = ["explain", "ব্যাখ্যা"],
    }));

    [Fact]
    public void ShortEnglish_RoutesToCheapLocal()
        => Assert.Equal("ollama", Build().Route("what is 2+2?"));

    [Fact]
    public void LongEnglish_RoutesToStrong()
        => Assert.Equal("groq", Build().Route(string.Join(' ', Enumerable.Repeat("word", 31))));

    [Fact]
    public void ComplexKeyword_RoutesToStrong()
        => Assert.Equal("groq", Build().Route("explain it"));

    [Fact]
    public void ShortBangla_StaysLocal()
        => Assert.Equal("ollama", Build().Route("তুমি কে?"));

    // ১২ শব্দের বেশি বাংলা → English threshold (30)-এর নিচে হলেও strong-এ যায়।
    [Fact]
    public void MediumBangla_RoutesToStrong()
        => Assert.Equal("groq", Build().Route(string.Join(' ', Enumerable.Repeat("শব্দ", 13))));

    [Fact]
    public void BanglaKeyword_RoutesToStrong()
        => Assert.Equal("groq", Build().Route("এটা ব্যাখ্যা করো"));
}
