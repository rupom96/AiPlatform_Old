import {
  createApi,
  fetchBaseQuery,
  type BaseQueryFn,
  type FetchArgs,
  type FetchBaseQueryError,
} from '@reduxjs/toolkit/query/react'
import type { RootState } from '@/app/store'
import { logOut, setCredentials } from '@/features/auth/authSlice'

const rawBaseQuery = fetchBaseQuery({
  baseUrl: `${import.meta.env.VITE_API_BASE_URL}/api/v1`,
  credentials: 'include', // refresh cookie পাঠায়/নেয়
  prepareHeaders: (headers, { getState }) => {
    const token = (getState() as RootState).auth.accessToken
    if (token) headers.set('Authorization', `Bearer ${token}`)
    return headers
  },
})

// 401 এলে silent refresh → নতুন access token → আসল request retry।
const baseQueryWithReauth: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> = async (
  args,
  api,
  extraOptions,
) => {
  let result = await rawBaseQuery(args, api, extraOptions)

  if (result.error?.status === 401) {
    const refresh = await rawBaseQuery({ url: '/auth/refresh', method: 'POST' }, api, extraOptions)
    if (refresh.data) {
      const { accessToken } = refresh.data as { accessToken: string }
      api.dispatch(setCredentials({ accessToken }))
      result = await rawBaseQuery(args, api, extraOptions) // retry
    } else {
      api.dispatch(logOut())
    }
  }

  return result
}

export const baseApi = createApi({
  reducerPath: 'api',
  baseQuery: baseQueryWithReauth,
  tagTypes: ['Document', 'Conversation'],
  endpoints: () => ({}), // feature-এ inject হবে (Phase 4b+)
})
