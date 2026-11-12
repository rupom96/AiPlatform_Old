import { baseApi } from '@/features/api/baseApi'

export interface UsageByModel {
  provider: string
  model: string
  requests: number
  inputTokens: number
  outputTokens: number
  costUsd: number
}

export interface UsageSummary {
  totalCostUsd: number
  totalRequests: number
  totalInputTokens: number
  totalOutputTokens: number
  byModel: UsageByModel[]
}

export interface EvalCaseResult {
  id: string
  passed: boolean
  answer: string
  failReason: string | null
}

export interface EvalReport {
  total: number
  passed: number
  passRate: number
  results: EvalCaseResult[]
}

// admin dashboard — LLM cost/token summary (6a) + evals runner (6g)।
export const dashboardApi = baseApi.injectEndpoints({
  endpoints: (builder) => ({
    usageSummary: builder.query<UsageSummary, void>({
      query: () => '/usage',
    }),
    runEvals: builder.mutation<EvalReport, string | undefined>({
      query: (provider) => ({
        url: `/evals/run${provider ? `?provider=${provider}` : ''}`,
        method: 'POST',
      }),
    }),
  }),
})

export const { useUsageSummaryQuery, useRunEvalsMutation } = dashboardApi
