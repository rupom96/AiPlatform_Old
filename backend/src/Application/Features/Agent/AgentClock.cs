namespace AiPlatform.Application.Features.Agent;

// agent-এর "আজ": কনফিগ করা টাইমজোনের তারিখ (default Asia/Dhaka), UTC নয়।
// clock ইনজেক্ট করা যায় বলে test-এ নির্দিষ্ট সময় বসানো যায়।
public static class AgentClock
{
    public static DateOnly Today(AgentSettings settings, TimeProvider? clock = null)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Resolve(settings.TimeZone)).DateTime);
    }

    // ভুল/অজানা নাম হলে UTC — agent চালু থাকে, শুধু তারিখ UTC-র। (নাম লিখতে ভুল হলে startup ভাঙানো অতিরিক্ত।)
    private static TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
