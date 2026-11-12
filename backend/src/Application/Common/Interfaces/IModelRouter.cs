namespace AiPlatform.Application.Common.Interfaces;

// query complexity দেখে cheap বা strong provider auto-select করে (cost optimize)।
// caller নিজে provider দিলে routing bypass হয় — এটা শুধু "auto" case-এর জন্য।
public interface IModelRouter
{
    bool IsEnabled { get; }

    // routed provider name ফিরায় (config-এর cheap/strong)।
    string Route(string message);
}
