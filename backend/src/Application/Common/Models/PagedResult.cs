namespace AiPlatform.Application.Common.Models;

// generic offset-pagination ফল। যেকোনো list endpoint reuse করবে।
// ponytail: offset (page/size) সরল ও admin-list-এ যথেষ্ট; বড় dataset-এ keyset l-এ যাওয়া যাবে পরে।
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}
