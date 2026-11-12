using System.ComponentModel;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using ModelContextProtocol.Server;

namespace AiPlatform.Api.Mcp;

// app-এর ITool গুলো MCP protocol-এ expose করে (external MCP client — Claude Desktop, inspector — ব্যবহার করতে পারে)।
// logic duplicate নয় — একই ITool.ExecuteAsync-এ bridge করে (single source of truth)।
[McpServerToolType]
public static class AiPlatformMcpTools
{
    [McpServerTool, Description("Returns the current UTC date and time.")]
    public static Task<string> CurrentTime(IEnumerable<ITool> tools, CancellationToken cancellationToken) =>
        InvokeAsync(tools, "current_time", "{}", cancellationToken);

    [McpServerTool, Description("Searches the company knowledge base (return, delivery, warranty, payment, support policies).")]
    public static Task<string> SearchKnowledgeBase(
        IEnumerable<ITool> tools,
        [Description("The search query, e.g. a policy question.")] string query,
        CancellationToken cancellationToken) =>
        InvokeAsync(tools, "search_knowledge_base", JsonSerializer.Serialize(new { query }), cancellationToken);

    // নাম দিয়ে ITool বেছে, JSON args দিয়ে চালায় (agent যেভাবে চালায় সেভাবেই)।
    private static async Task<string> InvokeAsync(
        IEnumerable<ITool> tools, string name, string argumentsJson, CancellationToken cancellationToken)
    {
        var tool = tools.FirstOrDefault(t => t.Name == name);
        if (tool is null)
            return $"Tool '{name}' is not available.";

        using var args = JsonDocument.Parse(argumentsJson);
        return await tool.ExecuteAsync(args.RootElement, cancellationToken);
    }
}
