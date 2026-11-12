using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Analyzer;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Analyzer;

public class AnalyzeCommandHandlerTests
{
    private readonly IChatProviderResolver _resolver = Substitute.For<IChatProviderResolver>();

    private void SetupProvider(string jsonReply)
    {
        var provider = Substitute.For<IChatProvider>();
        provider.CompleteJsonAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(jsonReply);
        _resolver.Resolve(Arg.Any<string?>()).Returns(provider);
    }

    [Fact]
    public async Task ValidJson_ReturnsTypedAnalysis()
    {
        SetupProvider("""{"sentiment":"negative","category":"রিফান্ড","priority":"high","summary":"গ্রাহক টাকা ফেরত চান"}""");

        var result = await new AnalyzeCommandHandler(_resolver)
            .Handle(new AnalyzeCommand("টাকা ফেরত চাই", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("negative", result.Value.Sentiment);
        Assert.Equal("high", result.Value.Priority);
        Assert.Equal("রিফান্ড", result.Value.Category);
    }

    [Fact]
    public async Task InvalidJson_ReturnsFailure()
    {
        SetupProvider("this is not json");

        var result = await new AnalyzeCommandHandler(_resolver)
            .Handle(new AnalyzeCommand("hi", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    [Fact]
    public async Task UnknownProvider_ReturnsValidationFailure()
    {
        _resolver.Resolve(Arg.Any<string?>()).Returns((IChatProvider?)null);

        var result = await new AnalyzeCommandHandler(_resolver)
            .Handle(new AnalyzeCommand("hi", "nope"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }
}
