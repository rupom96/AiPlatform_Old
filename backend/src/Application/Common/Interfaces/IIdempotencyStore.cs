using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// mutating request-এর at-most-once effect নিশ্চিত করে (Redis slot)। md 02 — idempotency on side-effects।
public interface IIdempotencyStore
{
    bool IsEnabled { get; }

    // slot দখলের চেষ্টা: Started হলে caller process করবে; নইলে InProgress/Completed।
    Task<IdempotencyBegin> BeginAsync(string key, CancellationToken cancellationToken);

    // সফল হলে response cache (পরের duplicate এটাই পাবে)।
    Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken);

    // ব্যর্থ হলে slot ছেড়ে দাও → client একই key দিয়ে retry করতে পারবে।
    Task AbandonAsync(string key, CancellationToken cancellationToken);
}
