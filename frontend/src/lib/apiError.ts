// Backend সবসময় RFC 7807 ProblemDetails দেয় — আসল কারণ থাকে `title`-এ
// (ApiControllerBase.ToProblem → `title: error.Description`)।
// আগে সব catch-এ শুধু "উত্তর আনা যায়নি" দেখাতাম, ফলে "provider tool-calling পারে না"-র মতো
// নির্দিষ্ট বার্তাও হারিয়ে যেত আর মনে হতো LLM-এর দোষ।
export function apiErrorMessage(error: unknown, fallback = 'উত্তর আনা যায়নি'): string {
  const data = (error as { data?: unknown })?.data

  if (typeof data === 'string' && data.trim()) return data

  if (data && typeof data === 'object') {
    const problem = data as { title?: string; detail?: string; errors?: Record<string, string[]> }

    // FluentValidation 400 — title generic ("One or more validation errors..."),
    // কাজের তথ্য errors dict-এ।
    const firstFieldError = problem.errors && Object.values(problem.errors).flat().find(Boolean)
    if (firstFieldError) return firstFieldError

    if (problem.detail?.trim()) return problem.detail
    if (problem.title?.trim()) return problem.title
  }

  return fallback
}
