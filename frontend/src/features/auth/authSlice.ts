import { createSlice, type PayloadAction } from '@reduxjs/toolkit'
import type { RootState } from '@/app/store'
import type { CurrentUser } from './authApi'

// access token শুধু memory-তে (Redux), localStorage-এ নয় → XSS-safe।
// bootstrapped = app load-এ প্রথম silent-refresh চেষ্টা শেষ হয়েছে কিনা।
interface AuthState {
  accessToken: string | null
  user: CurrentUser | null
  bootstrapped: boolean
}

const initialState: AuthState = { accessToken: null, user: null, bootstrapped: false }

const authSlice = createSlice({
  name: 'auth',
  initialState,
  reducers: {
    setCredentials: (state, action: PayloadAction<{ accessToken: string }>) => {
      state.accessToken = action.payload.accessToken
    },
    setUser: (state, action: PayloadAction<CurrentUser | null>) => {
      state.user = action.payload
    },
    setBootstrapped: (state) => {
      state.bootstrapped = true
    },
    logOut: (state) => {
      state.accessToken = null
      state.user = null
    },
  },
})

export const { setCredentials, setUser, setBootstrapped, logOut } = authSlice.actions
export default authSlice.reducer

export const selectIsAuthenticated = (s: RootState) => s.auth.accessToken !== null
export const selectBootstrapped = (s: RootState) => s.auth.bootstrapped
export const selectUser = (s: RootState) => s.auth.user
