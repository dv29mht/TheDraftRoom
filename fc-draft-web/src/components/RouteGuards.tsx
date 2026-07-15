import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'

export function RequireAuth() {
  const user = useAuthStore((state) => state.user)
  const mustChangePassword = useAuthStore((state) => state.mustChangePassword)
  const location = useLocation()
  if (!user) return <Navigate to="/login" replace state={{ from: location }} />
  if (mustChangePassword) return <Navigate to="/change-password" replace />
  return <Outlet />
}

export function RequireAdmin() {
  const user = useAuthStore((state) => state.user)
  return user?.role === 'admin' ? <Outlet /> : <Navigate to="/" replace />
}
