namespace AiPlatform.Application.Common.Interfaces;

// নাম দিয়ে provider বাছে; null হলে config-এর default। অজানা নাম → null।
// model দিলে ওই provider ওই model-এ চলে (ollama model-picker); null = config-এর default model।
public interface IChatProviderResolver
{
    IChatProvider? Resolve(string? providerName, string? model = null);
}
