import { createSlice, type PayloadAction } from '@reduxjs/toolkit'
import type { RootState } from '@/app/store'
import type { Lang } from '@/i18n/strings'

export type Theme = 'light' | 'dark'

// client-only preferences — localStorage-এ persist (store.subscribe দেখো)।
interface UiState {
  language: Lang
  theme: Theme
  defaultProvider: string
}

const initialState: UiState = {
  language: (localStorage.getItem('lang') as Lang) || 'bn',
  // theme main.tsx আগেই DOM-এ apply করেছে (no-flash) — সেখান থেকেই পড়ি।
  theme: document.documentElement.classList.contains('dark') ? 'dark' : 'light',
  defaultProvider: localStorage.getItem('provider') || 'groq',
}

const uiSlice = createSlice({
  name: 'ui',
  initialState,
  reducers: {
    setLanguage: (state, action: PayloadAction<Lang>) => {
      state.language = action.payload
    },
    setTheme: (state, action: PayloadAction<Theme>) => {
      state.theme = action.payload
    },
    setDefaultProvider: (state, action: PayloadAction<string>) => {
      state.defaultProvider = action.payload
    },
  },
})

export const { setLanguage, setTheme, setDefaultProvider } = uiSlice.actions
export default uiSlice.reducer

export const selectLanguage = (s: RootState) => s.ui.language
export const selectTheme = (s: RootState) => s.ui.theme
export const selectDefaultProvider = (s: RootState) => s.ui.defaultProvider
