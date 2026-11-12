using System.Text.Json;

namespace AiPlatform.Application.Common.Interfaces;

// agent যে tool call করতে পারে। নতুন capability = নতুন ITool impl (agent code বদলায় না)।
public interface ITool
{
    string Name { get; }
    string Description { get; }

    // OpenAI function "parameters" = JSON-schema object (anonymous object হলেই চলে)।
    object ParametersSchema { get; }

    // LLM-এর দেওয়া arguments (JSON) দিয়ে চালায়, string result ফেরায় (LLM-কে ফেরত যায়)।
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
