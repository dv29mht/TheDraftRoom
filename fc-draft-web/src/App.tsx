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
import { DraftsHubPage } from './pages/DraftsHubPage'
import { ForgotPasswordPage } from './pages/ForgotPasswordPage'
import { LobbyPage } from './pages/LobbyPage'
import { LoginPage } from './pages/LoginPage'
import { ResetPasswordPage } from './pages/ResetPasswordPage'
import { NewLobbyPage } from './pages/NewLobbyPage'
import { PlayerExplorerPage } from './pages/PlayerExplorerPage'
import { ProfilePage } from './pages/ProfilePage'
import { ResultsPage } from './pages/ResultsPage'
import { TeamsArchivePage } from './pages/TeamsArchivePage'

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
          <Route path="drafts" element={<DraftsHubPage />} />
          <Route path="drafts/new" element={<NewLobbyPage />} />
          <Route path="drafts/:draftId" element={<LobbyPage />} />
          <Route path="drafts/:draftId/results" element={<ResultsPage />} />
          <Route path="teams" element={<TeamsArchivePage />} />
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
