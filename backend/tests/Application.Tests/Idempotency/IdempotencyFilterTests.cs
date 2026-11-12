using AiPlatform.Api.Filters;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace AiPlatform.Application.Tests.Idempotency;

public class IdempotencyFilterTests
{
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();

    public IdempotencyFilterTests() => _store.IsEnabled.Returns(true);

    private static ActionExecutingContext Executing(HttpContext http)
    {
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());
    }

    private static HttpContext HttpWith(string? idempotencyKey)
    {
        var http = new DefaultHttpContext();
        if (idempotencyKey is not null)
            http.Request.Headers["Idempotency-Key"] = idempotencyKey;
        return http;
    }

    private static ActionExecutionDelegate Next(ActionExecutingContext ctx, IActionResult result, Action? onCalled = null) =>
        () =>
        {
            onCalled?.Invoke();
            var executed = new ActionExecutedContext(ctx, [], controller: new object()) { Result = result };
            return Task.FromResult(executed);
        };

    [Fact]
    public async Task NoHeader_RunsActionNormally()
    {
        var ctx = Executing(HttpWith(null));
        var nextCalled = false;

        await new IdempotencyFilter(_store).OnActionExecutionAsync(ctx, Next(ctx, new OkResult(), () => nextCalled = true));

        Assert.True(nextCalled);
        await _store.DidNotReceive().BeginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Completed_ReturnsStoredResponse_WithoutRunningAction()
    {
        _store.BeginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyBegin(IdempotencyState.Completed, new StoredResponse(202, "{\"documentId\":\"abc\"}")));

        var ctx = Executing(HttpWith("key-1"));
        var nextCalled = false;

        await new IdempotencyFilter(_store).OnActionExecutionAsync(ctx, Next(ctx, new OkResult(), () => nextCalled = true));

        Assert.False(nextCalled);
        var content = Assert.IsType<ContentResult>(ctx.Result);
        Assert.Equal(202, content.StatusCode);
        Assert.Contains("abc", content.Content);
    }

    [Fact]
    public async Task InProgress_Returns409()
    {
        _store.BeginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyBegin(IdempotencyState.InProgress, null));

        var ctx = Executing(HttpWith("key-1"));

        await new IdempotencyFilter(_store).OnActionExecutionAsync(ctx, Next(ctx, new OkResult()));

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
    }

    [Fact]
    public async Task Started_Success_CachesResponse()
    {
        _store.BeginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyBegin(IdempotencyState.Started, null));

        var ctx = Executing(HttpWith("key-1"));
        var success = new AcceptedResult(string.Empty, new { documentId = "abc" });

        await new IdempotencyFilter(_store).OnActionExecutionAsync(ctx, Next(ctx, success));

        await _store.Received().CompleteAsync("idem:anon:key-1", Arg.Is<StoredResponse>(r => r.StatusCode == 202), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Started_Failure_AbandonsSlot()
    {
        _store.BeginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyBegin(IdempotencyState.Started, null));

        var ctx = Executing(HttpWith("key-1"));
        var failure = new ObjectResult(new ProblemDetails()) { StatusCode = 400 };

        await new IdempotencyFilter(_store).OnActionExecutionAsync(ctx, Next(ctx, failure));

        await _store.Received().AbandonAsync("idem:anon:key-1", Arg.Any<CancellationToken>());
        await _store.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<StoredResponse>(), Arg.Any<CancellationToken>());
    }
}
