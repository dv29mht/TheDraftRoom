import { Suspense, lazy, type ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/AppShell'
import { AppStatusBanners } from './components/AppStatusBanners'
import { RequireAdmin, RequireAuth } from './components/RouteGuards'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { DashboardPage } from './pages/DashboardPage'
import { DraftsHubPage } from './pages/DraftsHubPage'
import { ForgotPasswordPage } from './pages/ForgotPasswordPage'
import { LobbyPage } from './pages/LobbyPage'
import { LoginPage } from './pages/LoginPage'
import { ResetPasswordPage } from './pages/ResetPasswordPage'
import { NewLobbyPage } from './pages/NewLobbyPage'
import { ProfilePage } from './pages/ProfilePage'

// PR-22 (§14 initial-load budget): the sign-in → hub → lobby/draft-room journey stays in the entry
// chunk; the archive, the dataset explorer, and the whole admin console load on demand. Each lazy
// route suspends only inside the shell's outlet, so navigation chrome never unmounts.
const ResultsPage = lazy(() => import('./pages/ResultsPage').then((m) => ({ default: m.ResultsPage })))
const TeamsArchivePage = lazy(() => import('./pages/TeamsArchivePage').then((m) => ({ default: m.TeamsArchivePage })))
const PlayerExplorerPage = lazy(() => import('./pages/PlayerExplorerPage').then((m) => ({ default: m.PlayerExplorerPage })))
const AdminOverviewPage = lazy(() => import('./pages/AdminOverviewPage').then((m) => ({ default: m.AdminOverviewPage })))
const AdminUsersPage = lazy(() => import('./pages/AdminUsersPage').then((m) => ({ default: m.AdminUsersPage })))
const AdminDraftsPage = lazy(() => import('./pages/AdminDraftsPage').then((m) => ({ default: m.AdminDraftsPage })))
const AdminPlayerDataPage = lazy(() => import('./pages/AdminPlayerDataPage').then((m) => ({ default: m.AdminPlayerDataPage })))
const AdminTemplatesPage = lazy(() => import('./pages/AdminTemplatesPage').then((m) => ({ default: m.AdminTemplatesPage })))
const AdminCommunicationsPage = lazy(() => import('./pages/AdminCommunicationsPage').then((m) => ({ default: m.AdminCommunicationsPage })))
const AdminAuditLogPage = lazy(() => import('./pages/AdminAuditLogPage').then((m) => ({ default: m.AdminAuditLogPage })))
const AdminSettingsPage = lazy(() => import('./pages/AdminSettingsPage').then((m) => ({ default: m.AdminSettingsPage })))

function Deferred({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={<div className="loading-state" role="status">Loading…</div>}>
      {children}
    </Suspense>
  )
}

export default function App() {
  return (
    <>
      {/* Above the router (PR-22): offline + update-ready states cover every journey. */}
      <AppStatusBanners />
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
            <Route path="drafts/:draftId/results" element={<Deferred><ResultsPage /></Deferred>} />
            <Route path="teams" element={<Deferred><TeamsArchivePage /></Deferred>} />
            <Route path="players" element={<Deferred><PlayerExplorerPage /></Deferred>} />
            <Route path="profile" element={<ProfilePage />} />
            <Route element={<RequireAdmin />}>
              <Route path="admin" element={<Navigate to="/" replace />} />
              <Route path="admin/overview" element={<Deferred><AdminOverviewPage /></Deferred>} />
              <Route path="admin/users" element={<Deferred><AdminUsersPage /></Deferred>} />
              <Route path="admin/drafts" element={<Deferred><AdminDraftsPage /></Deferred>} />
              <Route path="admin/player-data" element={<Deferred><AdminPlayerDataPage /></Deferred>} />
              <Route path="admin/templates" element={<Deferred><AdminTemplatesPage /></Deferred>} />
              <Route path="admin/communications" element={<Deferred><AdminCommunicationsPage /></Deferred>} />
              <Route path="admin/audit-log" element={<Deferred><AdminAuditLogPage /></Deferred>} />
              <Route path="admin/settings" element={<Deferred><AdminSettingsPage /></Deferred>} />
            </Route>
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </>
  )
}
