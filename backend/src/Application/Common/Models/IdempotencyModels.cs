namespace AiPlatform.Application.Common.Models;

// এক idempotency key-এর অবস্থা:
//   Started    — এই request slot দখল করল (প্রথমবার) → process করো।
//   InProgress — একই key-তে আরেকটা request চলছে (concurrent duplicate) → 409।
//   Completed  — আগেই শেষ হয়েছে → stored response ফেরত দাও (LLM/DB আবার হিট নয়)।
public enum IdempotencyState { Started, InProgress, Completed }

// cache করা response (duplicate-কে হুবহু ফেরত দিতে)।
public record StoredResponse(int StatusCode, string Body);

public record IdempotencyBegin(IdempotencyState State, StoredResponse? Stored);
