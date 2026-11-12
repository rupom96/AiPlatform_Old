using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning;
using AiPlatform.Api.Common;
using AiPlatform.Api.Contracts;
using AiPlatform.Application.Features.Chat;
using AiPlatform.Application.Features.Conversations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/chat")]
public class ChatController(IMediator mediator) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Chat(ChatRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await mediator.Send(
            new ChatCommand(request.Message, request.Provider, request.Model, request.ConversationId, userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }

    // sidebar — current user-এর conversation list (newest-first)
    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListConversationsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }

    // এক conversation-এর summary + সাম্প্রতিক message
    [HttpGet("conversations/{conversationId}")]
    public async Task<IActionResult> Conversation(string conversationId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetConversationQuery(conversationId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }

    // SSE streaming — token আসামাত্র client-এ পাঠায়
    [HttpPost("stream")]
    public async Task ChatStream(ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("message is required", cancellationToken);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var stream = mediator.CreateStream(
                new ChatStreamCommand(request.Message, request.Provider, request.Model, request.ConversationId, userId), cancellationToken);

            await foreach (var token in stream)
            {
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { token })}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // client disconnect — স্বাভাবিক, চুপচাপ শেষ
        }
        catch (Exception)
        {
            await Response.WriteAsync(
                $"event: error\ndata: {JsonSerializer.Serialize(new { error = "Stream failed." })}\n\n",
                cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
