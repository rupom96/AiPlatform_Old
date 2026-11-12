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
}
export interface AgentRequest {
  message: string
  provider?: string
  /** ollama model-picker — দিলে provider-এর default model override */
  model?: string
}

export const agentApi = baseApi.injectEndpoints({
  endpoints: (builder) => ({
    agent: builder.mutation<AgentResponse, AgentRequest>({
      query: (body) => ({ url: '/agent', method: 'POST', body }),
    }),
  }),
})

export const { useAgentMutation } = agentApi
