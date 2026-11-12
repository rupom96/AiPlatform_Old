namespace AiPlatform.Application.Common.Models;

// Vector search hit — UI-তে top-N score দেখাতে (tools বা docs collection)।
public record ScoredHit(string Label, double Score, string? Detail = null);
