namespace AiPlatform.Infrastructure.Idempotency;

// appsettings "Idempotency"। key কতক্ষণ মনে রাখবে।
public class IdempotencySettings
{
    public const string SectionName = "Idempotency";

    public bool Enabled { get; init; } = true;
    public int TtlHours { get; init; } = 24;
}
