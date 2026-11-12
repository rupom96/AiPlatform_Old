using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Rag;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Rag;

public class RagQueryCommandHandlerTests
{
    private readonly IRetrieverResolver _retrieverResolver = Substitute.For<IRetrieverResolver>();
    private readonly IChatProviderResolver _chatResolver = Substitute.For<IChatProviderResolver>();
    private readonly ILlmUsageRecorder _usage = Substitute.For<ILlmUsageRecorder>();

    private RagQueryCommandHandler CreateSut() => new(_retrieverResolver, _chatResolver, _usage);

    private static IRetriever RetrieverReturning(params RetrievedChunk[] chunks)
    {
        var retriever = Substitute.For<IRetriever>();
        retriever.RetrieveAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RetrievedChunk>)chunks);
        return retriever;
    }

    [Fact]
    public async Task UnknownMethod_ReturnsValidationFailure()
    {
        _retrieverResolver.Resolve(Arg.Any<string?>()).Returns((IRetriever?)null);

        var result = await CreateSut().Handle(new RagQueryCommand("q", "magic", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task NoChunksFound_ReturnsGroundedNotFoundAnswer()
    {
        var retriever = RetrieverReturning();
        _retrieverResolver.Resolve(Arg.Any<string?>()).Returns(retriever);

        var result = await CreateSut().Handle(new RagQueryCommand("q", "keyword", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Sources);
        Assert.Contains("দুঃখিত", result.Value.Answer);   // grounded "not found", hallucinate নয়
    }

    [Fact]
    public async Task ChunksFound_ButUnknownProvider_ReturnsValidationFailure()
    {
        var retriever = RetrieverReturning(new RetrievedChunk("ctx", 1.0, "kb"));
        _retrieverResolver.Resolve(Arg.Any<string?>()).Returns(retriever);
        _chatResolver.Resolve(Arg.Any<string?>()).Returns((IChatProvider?)null);

        var result = await CreateSut().Handle(new RagQueryCommand("q", "keyword", "nope"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task ChunksFound_ReturnsAnswerWithSources()
    {
        var chunk = new RetrievedChunk("প্রিমিয়াম ৯৯৯ টাকা", 0.77, "premium.txt");
        var retriever = RetrieverReturning(chunk);
        _retrieverResolver.Resolve(Arg.Any<string?>()).Returns(retriever);

        var chat = Substitute.For<IChatProvider>();
        chat.CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResult("দাম ৯৯৯ টাকা [1]", "groq", "model"));
        _chatResolver.Resolve(Arg.Any<string?>()).Returns(chat);

        var result = await CreateSut().Handle(new RagQueryCommand("দাম কত?", "keyword", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("৯৯৯", result.Value.Answer);
        Assert.Single(result.Value.Sources);
        Assert.Equal("premium.txt", result.Value.Sources[0].Source);
    }
}
