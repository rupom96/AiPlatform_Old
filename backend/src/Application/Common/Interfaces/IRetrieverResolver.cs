namespace AiPlatform.Application.Common.Interfaces;

// method নাম (keyword/embedding/qdrant) দিয়ে retriever বাছে; অজানা হলে null।
public interface IRetrieverResolver
{
    IRetriever? Resolve(string? method);
}
