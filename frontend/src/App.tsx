import { Routes, Route, Navigate } from 'react-router-dom'
import { AppBootstrap } from '@/features/auth/AppBootstrap'
import { ProtectedRoute } from '@/routes/ProtectedRoute'
import { PublicRoute } from '@/routes/PublicRoute'
import { AppShell } from '@/components/AppShell'
import AuthPage from '@/features/auth/AuthPage'
import { ForgotPasswordPage } from '@/features/auth/ForgotPasswordPage'
import { ResetPasswordPage } from '@/features/auth/ResetPasswordPage'
import { HomePage } from '@/features/home/HomePage'
import { ChatPage } from '@/features/chat/ChatPage'
// import { DashboardPage } from '@/features/dashboard/DashboardPage'   // hidden — docs/hidden-features.md
import { SettingsPage } from '@/features/settings/SettingsPage'

export default function App() {
  return (
    <AppBootstrap>
      <Routes>
        {/* logged-in হলে এগুলো / তে redirect */}
        <Route element={<PublicRoute />}>
          <Route path="/login" element={<AuthPage initialMode="login" />} />
          <Route path="/register" element={<AuthPage initialMode="register" />} />
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
          <Route path="/reset-password" element={<ResetPasswordPage />} />
        </Route>

        {/* logged-in না হলে /login এ redirect */}
        <Route element={<ProtectedRoute />}>
          <Route element={<AppShell />}>
            <Route path="/" element={<HomePage />} />
            <Route path="/chat" element={<ChatPage />} />
            <Route path="/rag" element={<Navigate to="/chat" replace />} />
            <Route path="/documents" element={<Navigate to="/chat" replace />} />
            {/* <Route path="/dashboard" element={<DashboardPage />} /> */}   {/* hidden — docs/hidden-features.md */}
            <Route path="/settings" element={<SettingsPage />} />
          </Route>
        </Route>

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AppBootstrap>
  )
}
