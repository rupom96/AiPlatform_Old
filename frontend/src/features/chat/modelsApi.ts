import { baseApi } from '@/features/api/baseApi'

// local engine-এর chat model তালিকা (GET /llm/models?provider=…) — model-picker dropdown।
// provider = "ollama" | "ollama-server" | "vllm" | "vllm-server" (এই মেশিন বনাম GPU সার্ভার); দুই host-এ আলাদা model
// থাকতে পারে, তাই cache-ও provider ধরে আলাদা (RTK Query নিজেই argument দিয়ে key বানায়)।
export const modelsApi = baseApi.injectEndpoints({
  endpoints: (builder) => ({
    localModels: builder.query<string[], string>({
      query: (provider) => `/llm/models?provider=${encodeURIComponent(provider)}`,
    }),
  }),
})

export const { useLocalModelsQuery } = modelsApi
