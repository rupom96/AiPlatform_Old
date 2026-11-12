namespace AiPlatform.Domain.Entities.Erp;

// এক product-এ একাধিক ধরনের warranty থাকতে পারে (Service ২৪ মাস + Parts ১২ মাস)।
// DB-তে string হিসেবে জমা — query পড়া যায়, আর LLM-ও মান দেখে অর্থ বোঝে।
public enum WarrantyType
{
    Service,       // ত্রুটি সারিয়ে দেওয়া
    Replacement,   // বদলে নতুন unit
    Parts,         // যন্ত্রাংশ
    Labour,        // শুধু মজুরি, যন্ত্রাংশের দাম নয়
    Lifetime       // আজীবন — DurationMonths null
}
