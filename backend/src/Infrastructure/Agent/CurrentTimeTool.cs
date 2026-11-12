using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;

namespace AiPlatform.Infrastructure.Agent;

// সহজ deterministic tool — বর্তমান UTC time। agent-এর tool-calling demo।
public class CurrentTimeTool : ITool
{
    public string Name => "current_time";
    public string Description => "Returns the current UTC date and time. Use when the user asks about the current time or date.";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken) =>
        Task.FromResult($"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
}
