using AiPlatform.Application.Common.Behaviors;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using AiPlatform.Domain.Entities;
using MediatR;
using NSubstitute;

namespace AiPlatform.Application.Tests.Audit;

public class AuditBehaviorTests
{
    private readonly IAuditLog _auditLog = Substitute.For<IAuditLog>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    // auditable command
    private record AuditableCommand : IRequest<Result>, IAuditableRequest
    {
        public string AuditAction => "Test.Action";
    }

    // non-auditable command
    private record PlainCommand : IRequest<Result>;

    private AuditBehavior<TRequest, Result> Behavior<TRequest>() where TRequest : notnull =>
        new(_auditLog, _currentUser);

    [Fact]
    public async Task Auditable_Success_WritesEntry_WithSucceededTrue()
    {
        var tenant = Guid.NewGuid();
        _currentUser.TenantId.Returns(tenant);
        _currentUser.UserId.Returns("user-1");

        await Behavior<AuditableCommand>().Handle(
            new AuditableCommand(), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        await _auditLog.Received().WriteAsync(
            Arg.Is<AuditEntry>(e => e.Action == "Test.Action" && e.Succeeded && e.TenantId == tenant && e.UserId == "user-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Auditable_BusinessFailure_WritesEntry_WithSucceededFalse()
    {
        await Behavior<AuditableCommand>().Handle(
            new AuditableCommand(),
            _ => Task.FromResult(Result.Failure(Error.NotFound("x", "y"))),
            CancellationToken.None);

        await _auditLog.Received().WriteAsync(Arg.Is<AuditEntry>(e => !e.Succeeded), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonAuditable_DoesNotWrite()
    {
        await Behavior<PlainCommand>().Handle(
            new PlainCommand(), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        await _auditLog.DidNotReceive().WriteAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }
}
