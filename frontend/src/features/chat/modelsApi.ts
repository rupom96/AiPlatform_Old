import { baseApi } from '@/features/api/baseApi'

// ollama-তে installed chat model তালিকা (GET /llm/models?provider=…) — model-picker dropdown।
// provider = "ollama" (এই মেশিন) বা "ollama-server" (GPU সার্ভার); দুই host-এ আলাদা model
// থাকতে পারে, তাই cache-ও provider ধরে আলাদা (RTK Query নিজেই argument দিয়ে key বানায়)।
export const modelsApi = baseApi.injectEndpoints({
  endpoints: (builder) => ({
    localModels: builder.query<string[], string>({
      query: (provider) => `/llm/models?provider=${encodeURIComponent(provider)}`,
    }),
  }),
})

export const { useLocalModelsQuery } = modelsApi
