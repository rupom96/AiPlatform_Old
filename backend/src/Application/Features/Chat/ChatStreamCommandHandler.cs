using System.Runtime.CompilerServices;
using System.Text;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using MediatR;

namespace AiPlatform.Application.Features.Chat;

public class ChatStreamCommandHandler(
    IChatProviderResolver resolver,
    IPromptGuard promptGuard,
    IConversationMemory conversationMemory)
    : IStreamRequestHandler<ChatStreamCommand, string>
{
    public async IAsyncEnumerable<string> Handle(
        ChatStreamCommand request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // guardrails — injection block, PII redact (streaming path)
        var message = request.Message;
        if (promptGuard.IsEnabled)
        {
            var guard = promptGuard.Check(message);
            if (guard.Blocked)
                throw new InvalidOperationException(guard.Reason);
            message = guard.SafeInput;
        }

        var provider = resolver.Resolve(request.Provider, request.Model);
        if (provider is null)
        {
            var requested = string.IsNullOrWhiteSpace(request.Provider) ? "default" : request.Provider;
            throw new InvalidOperationException($"No configured LLM provider available for '{requested}'.");
        }

        var useMemory = conversationMemory.IsActive(request.ConversationId, request.UserId);

        // memory থাকলে summary + history LLM-এ পাঠাই (chat handler-এর মতোই)।
        var messages = useMemory
            ? await conversationMemory.BuildMessagesAsync(request.UserId!, request.ConversationId!, message, cancellationToken)
            : [new ChatMessage("user", message)];

        // streamed token জমাই → শেষে history persist করতে (assistant reply)।
        var assistant = new StringBuilder();
        await foreach (var token in provider.StreamAsync(messages, cancellationToken))
        {
            assistant.Append(token);
            yield return token;
        }

        // full response পেলে turn save (client disconnect হলে এখানে পৌঁছায় না — partial save হয় না)।
        if (useMemory)
            await conversationMemory.PersistTurnAsync(
                request.UserId!, request.ConversationId!, message, assistant.ToString(), provider, cancellationToken);
    }
}
