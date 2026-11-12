namespace AiPlatform.Infrastructure.Conversation;

// appsettings "Conversation"। history TTL + কত message-এর পর summarize।
public class ConversationSettings
{
    public const string SectionName = "Conversation";

    public bool Enabled { get; init; }

    // এর বেশি message জমলে পুরনোগুলো summarize হয়ে trim হবে (turn = user+assistant, তাই even রাখো)।
    public int MaxRecentMessages { get; init; } = 10;

    // conversation Redis-এ কতক্ষণ বাঁচবে (শেষ activity থেকে)।
    public int TtlHours { get; init; } = 24;
}
