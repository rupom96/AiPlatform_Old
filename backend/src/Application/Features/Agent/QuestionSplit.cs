namespace AiPlatform.Application.Features.Agent;

// এক বার্তা ভেঙে পাওয়া আলাদা প্রশ্ন। Skipped = সীমার বাইরে পড়ায় বাদ যাওয়া প্রশ্নের সংখ্যা (ব্যবহারকারীকে জানাতে)।
public record QuestionSplit(IReadOnlyList<string> Questions, int Skipped);
