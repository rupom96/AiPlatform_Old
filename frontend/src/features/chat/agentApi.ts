import { baseApi } from '@/features/api/baseApi'

// Agent — ERP tools + knowledge base। stream নয়: tool-এর পর দ্বিতীয় turn লাগে।
export interface ScoredHit {
  label: string
  score: number
  detail?: string | null
}

export interface AgentResponse {
  answer: string
  toolsUsed: string[]
  latencyMs: number
  toolHits?: ScoredHit[] | null
  documentHits?: ScoredHit[] | null
  /** শেষ LLM call যে provider/model দিল (fallback হলে অনুরোধ-করা provider থেকে আলাদা হতে পারে) */
  provider?: string | null
  model?: string | null
  /** ডকুমেন্ট খোঁজা আসলে যে পদ্ধতিতে হলো (বাছা পদ্ধতি ব্যর্থ হলে তা থেকে আলাদা); খোঁজা না হলে null */
  retrievalMethodUsed?: string | null
}
export interface AgentRequest {
  message: string
  provider?: string
  /** ollama model-picker — দিলে provider-এর default model override */
  model?: string
  /** ডকুমেন্ট খোঁজার পদ্ধতি (qdrant-hybrid | qdrant-rerank | qdrant-hybrid-rerank); না দিলে default (vector) */
  retrievalMethod?: string
}

export const agentApi = baseApi.injectEndpoints({
  endpoints: (builder) => ({
    agent: builder.mutation<AgentResponse, AgentRequest>({
      query: (body) => ({ url: '/agent', method: 'POST', body }),
    }),
  }),
})

export const { useAgentMutation } = agentApi
