namespace AiPlatform.Infrastructure.Ingestion;

// appsettings "IngestionQueue"। Redis Stream + consumer group config।
public class IngestionQueueSettings
{
    public const string SectionName = "IngestionQueue";

    public string StreamKey { get; init; } = "ingestion:stream";
    public string GroupName { get; init; } = "ingestion-workers";

    // এত ms ধরে unacked (crash/dead worker) message reclaim করা হবে।
    public long MinIdleMs { get; init; } = 60_000;

    // queue খালি হলে worker এত ms ঘুমিয়ে আবার দেখে।
    public int PollDelayMs { get; init; } = 1_000;
}
