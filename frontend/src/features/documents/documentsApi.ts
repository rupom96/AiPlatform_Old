import { baseApi } from '@/features/api/baseApi'

export interface DocumentStatus {
  id: string
  fileName: string
  status: 'Pending' | 'Processing' | 'Completed' | 'Failed'
  totalChunks: number
  processedChunks: number
  error: string | null
}

export interface PagedDocuments {
  items: DocumentStatus[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export const documentsApi = baseApi.injectEndpoints({
  endpoints: (builder) => ({
    uploadDocument: builder.mutation<{ documentId: string; status: string }, FormData>({
      query: (body) => ({ url: '/documents', method: 'POST', body }),
      invalidatesTags: ['Document'],
    }),
    documentStatus: builder.query<DocumentStatus, string>({
      query: (id) => `/documents/${id}`,
    }),
    listDocuments: builder.query<PagedDocuments, void>({
      query: () => '/documents?page=1&pageSize=50',
      providesTags: ['Document'],
    }),
    // backend SQL soft-delete + Qdrant থেকে ঐ document-এর chunk — দুটোই করে।
    deleteDocument: builder.mutation<void, string>({
      query: (id) => ({ url: `/documents/${id}`, method: 'DELETE' }),
      invalidatesTags: ['Document'],
    }),
  }),
})

export const {
  useUploadDocumentMutation,
  useDocumentStatusQuery,
  useListDocumentsQuery,
  useDeleteDocumentMutation,
} = documentsApi
