namespace AiPlatform.Application.Features.Analyzer;

// ⚠️ APP-SPECIFIC DEMO SCHEMA — AI-এর JSON → typed object। sentiment: positive|neutral|negative,
// priority: low|medium|high। নতুন project-এ নিজের analysis shape দিয়ে বদলাও।
public record MessageAnalysis(string Sentiment, string Category, string Priority, string Summary);
