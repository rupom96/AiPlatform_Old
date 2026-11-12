using System.Security.Cryptography;
using System.Text;

namespace AiPlatform.Infrastructure.Agent;

// tool name → deterministic Qdrant point-id। re-sync-এ overwrite, duplicate জমে না।
public static class ToolPointId
{
    public static string For(string toolName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"tool:{toolName.Trim().ToLowerInvariant()}"));
        return new Guid(bytes).ToString();
    }
}
