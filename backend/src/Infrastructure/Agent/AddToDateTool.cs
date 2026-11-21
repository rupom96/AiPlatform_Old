using System.Globalization;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Features.Agent;

namespace AiPlatform.Infrastructure.Agent;

// তারিখের হিসাব কোডে, model-এ নয়: ছোট model (reasoning ছাড়া) "২০২৬-১১-২১ + ২ মাস" ভুল করে (২০২৬-১২-২০, ২০২৪-০২-২০ ইত্যাদি
// দিয়েছে) — আর ভুল তারিখ আত্মবিশ্বাসে বলা "তথ্য নেই" বলার চেয়ে খারাপ। model শুধু ডকুমেন্ট থেকে নিয়ম ("নোটিশ দুই মাস") নেয়,
// তারিখ বের করে এই tool।
public class AddToDateTool(AgentSettings settings, TimeProvider? clock = null) : ITool
{
    // অযৌক্তিক মান (model-এর ভুলে) সীমায় ধরি — DateOnly-র সীমা ছাড়ালে exception না হয়ে পরিষ্কার বার্তা।
    private const int MaxMonths = 1200;
    private const int MaxDays = 36_500;

    public string Name => "add_to_date";

    public string Description =>
        "Adds months and/or days to a date (default: today) and returns the exact resulting date with its weekday. " +
        "ALWAYS use this for date calculations — for example the last working day after a notice period " +
        "('resign today, notice 2 months') or a due date. Never calculate dates yourself. " +
        "Use a negative number to go back in time.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            months = new { type = "integer", description = "Months to add (may be negative). Optional." },
            days = new { type = "integer", description = "Days to add (may be negative). Optional." },
            startDate = new { type = "string", description = "Start date as yyyy-MM-dd. Omit to start from today." },
        },
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var today = AgentClock.Today(settings, clock);

        if (!TryReadStart(arguments, today, out var start))
            return Task.FromResult("startDate must be a valid date in yyyy-MM-dd format.");
        if (!TryReadInt(arguments, "months", MaxMonths, out var months))
            return Task.FromResult($"months must be a whole number between -{MaxMonths} and {MaxMonths}.");
        if (!TryReadInt(arguments, "days", MaxDays, out var days))
            return Task.FromResult($"days must be a whole number between -{MaxDays} and {MaxDays}.");
        if (months == 0 && days == 0)
            return Task.FromResult("Provide months and/or days to add.");

        DateOnly result;
        try
        {
            result = start.AddMonths(months).AddDays(days);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Task.FromResult("The resulting date is out of the supported range.");
        }

        var parts = new List<string>();
        if (months != 0) parts.Add($"{months:+#;-#} month(s)");
        if (days != 0) parts.Add($"{days:+#;-#} day(s)");

        return Task.FromResult(
            $"{Iso(start)} {string.Join(" ", parts)} = {Iso(result)} ({result.DayOfWeek}). " +
            $"Today in {settings.TimeZone} is {Iso(today)}.");
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryReadStart(JsonElement args, DateOnly today, out DateOnly start)
    {
        start = today;
        if (!args.TryGetProperty("startDate", out var element) || element.ValueKind is JsonValueKind.Null)
            return true;
        if (element.ValueKind != JsonValueKind.String)
            return false;

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return true; // খালি string = দেওয়া হয়নি, আজ থেকে

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start);
    }

    // model কখনো 2 কখনো "2" পাঠায় — দুটোই নিই; ভগ্নাংশ/অযৌক্তিক মান প্রত্যাখ্যান।
    private static bool TryReadInt(JsonElement args, string name, int limit, out int value)
    {
        value = 0;
        if (!args.TryGetProperty(name, out var element) || element.ValueKind is JsonValueKind.Null)
            return true;

        var ok = element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
        return ok && Math.Abs(value) <= limit;
    }
}
