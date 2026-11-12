import { configureStore } from '@reduxjs/toolkit'
import { baseApi } from '@/features/api/baseApi'
import authReducer from '@/features/auth/authSlice'
import uiReducer from '@/features/settings/uiSlice'

export const store = configureStore({
  reducer: {
    auth: authReducer,
    ui: uiReducer,
    [baseApi.reducerPath]: baseApi.reducer,
  },
  middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(baseApi.middleware),
})

// ui preferences persist + theme DOM-এ apply (single source of truth)। snapshot guard → শুধু বদলালে কাজ।
let prevUi = ''
store.subscribe(() => {
  const ui = store.getState().ui
  const snapshot = `${ui.theme}|${ui.language}|${ui.defaultProvider}`
  if (snapshot === prevUi) return
  prevUi = snapshot

  document.documentElement.classList.toggle('dark', ui.theme === 'dark')
  localStorage.setItem('theme', ui.theme)
  localStorage.setItem('lang', ui.language)
  localStorage.setItem('provider', ui.defaultProvider)
})

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch
