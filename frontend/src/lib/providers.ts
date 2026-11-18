// LLM provider list — এক জায়গায় (আগে ৪ ফাইলে ডুপ্লিকেট ছিল)।
// backend appsettings "Llm:Providers"-এর সাথে মিল; নতুন provider এখানে যোগ করলেই সব dropdown-এ আসে।
// ollama = local (Ollama, key ছাড়া) — hosted↔local swap একই UI-তে।
// ক্রম = fallback chain-এর ক্রম (দ্রুততম আগে, local শেষে)।
// cerebras (402 payment required) ও mistral (429 rate limited) সাময়িকভাবে লুকানো —
// backend appsettings-এ দুটোই আছে, ফিরিয়ে আনার নিয়ম docs/hidden-features.md।
// export const PROVIDERS = ['cerebras', 'groq', 'gemini', 'mistral', 'ollama'] as const
export const PROVIDERS = ['groq', 'gemini', 'ollama', 'vllm'] as const

// Agent mode-এ tool-calling লাগে, যা শুধু OpenAI-compatible provider-রা পারে
// (backend: OpenAiCompatibleChatProvider : IToolCallingProvider)। GeminiChatProvider
// ওই interface লেখেনি, তাই gemini দিলে /agent কোনো LLM call ছাড়াই 400 ফেরত দেয়।
// এখানে বাদ দিলে ভুলটা করাই যায় না। gemini-তে tool-calling যোগ হলে এই তালিকা মুছে দিও।
export const TOOL_PROVIDERS: readonly string[] = PROVIDERS.filter((p) => p !== 'gemini')

// ollama দুই জায়গায় চলতে পারে: এই মেশিনে, বা GPU সার্ভারে। dropdown-এ একটাই "ollama"
// থাকে, পাশের toggle ঠিক করে কোনটা — সেটাই backend-এ provider নাম হিসেবে যায়।
// দুটোই appsettings "Llm:Providers"-এ আলাদা entry, শুধু BaseUrl আলাদা।
export const OLLAMA_LOCAL = 'ollama'
export const OLLAMA_SERVER = 'ollama-server'

// vLLM-ও একই ধাঁচের local engine: dropdown-এ "vllm", toggle দিলে "vllm-server"।
export const VLLM_LOCAL = 'vllm'
export const VLLM_SERVER = 'vllm-server'

// "লোকাল ইঞ্জিন" = ollama বা vllm — এদের জন্যই model-picker dropdown ও এই মেশিন/GPU সার্ভার toggle দেখায়।
export const isLocalEngine = (provider: string) => provider === OLLAMA_LOCAL || provider === VLLM_LOCAL

const SERVER_VARIANT: Record<string, string> = {
  [OLLAMA_LOCAL]: OLLAMA_SERVER,
  [VLLM_LOCAL]: VLLM_SERVER,
}

// dropdown-এর নাম + toggle → backend যে provider পাবে
export const effectiveProvider = (provider: string, useServer: boolean) =>
  useServer ? (SERVER_VARIANT[provider] ?? provider) : provider
