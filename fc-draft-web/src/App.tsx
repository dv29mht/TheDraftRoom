import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/AppShell'
import { RequireAdmin, RequireAuth } from './components/RouteGuards'
import { AdminUsersPage } from './pages/AdminUsersPage'
import { AdminDraftsPage } from './pages/AdminDraftsPage'
import { AdminPlayerDataPage } from './pages/AdminPlayerDataPage'
import { AdminSettingsPage } from './pages/AdminSettingsPage'
import { AdminTemplatesPage } from './pages/AdminTemplatesPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { DashboardPage } from './pages/DashboardPage'
import { ForgotPasswordPage } from './pages/ForgotPasswordPage'
import { LoginPage } from './pages/LoginPage'
import { ResetPasswordPage } from './pages/ResetPasswordPage'
import { NewLobbyPage } from './pages/NewLobbyPage'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { PlayerExplorerPage } from './pages/PlayerExplorerPage'
import { ProfilePage } from './pages/ProfilePage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/change-password" element={<ChangePasswordPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppShell />}>
          <Route index element={<DashboardPage />} />
          <Route path="drafts" element={<PlaceholderPage eyebrow="Draft hub" title="Your tournament drafts" description="Active, upcoming and completed rooms will live here." action={{ label: 'Create lobby', to: '/drafts/new' }} />} />
          <Route path="drafts/new" element={<NewLobbyPage />} />
          <Route path="teams" element={<PlaceholderPage eyebrow="Squad archive" title="Completed teams" description="Review protected players, formations and full pick histories." />} />
          <Route path="players" element={<PlayerExplorerPage />} />
          <Route path="profile" element={<ProfilePage />} />
          <Route element={<RequireAdmin />}>
            <Route path="admin" element={<Navigate to="/" replace />} />
            <Route path="admin/users" element={<AdminUsersPage />} />
            <Route path="admin/drafts" element={<AdminDraftsPage />} />
            <Route path="admin/player-data" element={<AdminPlayerDataPage />} />
            <Route path="admin/templates" element={<AdminTemplatesPage />} />
            <Route path="admin/settings" element={<AdminSettingsPage />} />
          </Route>
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
