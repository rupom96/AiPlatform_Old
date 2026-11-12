namespace AiPlatform.Infrastructure.Llm;

// appsettings "Llm:Fallback"। এক provider ব্যর্থ হলে পরেরটায় যাওয়ার ক্রম।
public class FallbackSettings
{
    public const string SectionName = "Llm:Fallback";

    public bool Enabled { get; init; }

    // ক্রম গুরুত্বপূর্ণ — বাঁ থেকে ডানে চেষ্টা। সবার শেষে local রাখা উচিত (কখনো quota শেষ হয় না)।
    public List<string> Chain { get; init; } = [];
}
