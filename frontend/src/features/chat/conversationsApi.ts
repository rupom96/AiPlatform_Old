import { baseApi } from '@/features/api/baseApi'

export interface ConversationSummary {
  conversationId: string
  title: string
  updatedAtUtc: string
}

export interface ConversationMessage {
  role: string
  content: string
}

export interface ConversationDetail {
  conversationId: string
  summary: string | null
  messages: ConversationMessage[]
}

// 6f memory-র উপর: sidebar list + এক conversation-এর detail (summary + সাম্প্রতিক message)।
export const conversationsApi = baseApi.injectEndpoints({
  endpoints: (builder) => ({
    listConversations: builder.query<ConversationSummary[], void>({
      query: () => '/chat/conversations',
      providesTags: ['Conversation'],
    }),
    getConversation: builder.query<ConversationDetail, string>({
      query: (id) => `/chat/conversations/${id}`,
    }),
  }),
})

export const { useListConversationsQuery, useLazyGetConversationQuery } = conversationsApi
