using System.Globalization;

namespace AiPlatform.Infrastructure.Agent;

// টাকার অঙ্ক tool-ই সাজিয়ে দেয়, কাঁচা সংখ্যা নয়।
//
// কারণ (2026-11-05, groq): get_sales ফেরত দিয়েছিল 2028479.00, কিন্তু উত্তরে লেখা হলো
// "১৯,৯৯,৯৯৯" — মডেল নিজে থেকে "প্রায় ২০ লাখ" ভেবে গোল করতে গিয়ে ভুল সংখ্যা বসিয়েছে।
// ইউনিট (৩৯) ঠিকই ছিল, অর্থাৎ ডেটা ঠিক এসেছিল; ভুলটা কেবল লেখার সময়।
// এ ধরনের ভুল ২% দূরে থাকে বলে চোখেও পড়ে না — ভুল ডেটা আনার চেয়েও বিপজ্জনক।
//
// অঙ্ক আগেই সাজানো থাকলে হুবহু তুলে দেওয়া ছাড়া উপায় থাকে না। সাথে system prompt-এও
// নিয়মটা লেখা আছে (RunAgentCommandHandler) — দুটো একসাথে।
internal static class ToolFormat
{
    // InvariantCulture জরুরি — process-এর culture বদলালে separator বদলে যেত,
    // আর একই প্রশ্নে একেক দিন একেক রকম সংখ্যা দেখাত।
    public static string Money(decimal amount)
        => amount == decimal.Truncate(amount)
            ? amount.ToString("#,##0", CultureInfo.InvariantCulture) + " BDT"
            : amount.ToString("#,##0.00", CultureInfo.InvariantCulture) + " BDT";
}
