import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it } from 'vitest'
import type { ReactElement } from 'react'
import { RequireAdmin, RequireAuth } from './RouteGuards'
import { useAuthStore } from '../stores/authStore'
import type { UserRole } from '../types/auth'

function signIn(role: UserRole, mustChangePassword = false) {
  useAuthStore.setState({
    user: { id: '1', displayName: 'Test User', email: 'test@draftroom.dev', role },
    accessToken: 'token',
    mustChangePassword,
  })
}

function renderGuard(guard: ReactElement, initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route element={guard}>
          <Route path="/protected" element={<div>PROTECTED CONTENT</div>} />
          <Route path="/admin-area" element={<div>ADMIN CONTENT</div>} />
        </Route>
        <Route path="/" element={<div>HOME</div>} />
        <Route path="/login" element={<div>LOGIN SCREEN</div>} />
        <Route path="/change-password" element={<div>CHANGE PASSWORD SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  useAuthStore.setState({ user: null, accessToken: null, mustChangePassword: false })
})

describe('RequireAuth', () => {
  it('redirects an anonymous visitor to the login screen', () => {
    renderGuard(<RequireAuth />, '/protected')
    expect(screen.getByText('LOGIN SCREEN')).toBeInTheDocument()
    expect(screen.queryByText('PROTECTED CONTENT')).not.toBeInTheDocument()
  })

  it('forces a pending password change before any protected route', () => {
    signIn('player', true)
    renderGuard(<RequireAuth />, '/protected')
    expect(screen.getByText('CHANGE PASSWORD SCREEN')).toBeInTheDocument()
  })

  it('renders the protected route for an authenticated user', () => {
    signIn('player')
    renderGuard(<RequireAuth />, '/protected')
    expect(screen.getByText('PROTECTED CONTENT')).toBeInTheDocument()
  })
})

describe('RequireAdmin', () => {
  it('sends a player back to the dashboard', () => {
    signIn('player')
    renderGuard(<RequireAdmin />, '/admin-area')
    expect(screen.getByText('HOME')).toBeInTheDocument()
    expect(screen.queryByText('ADMIN CONTENT')).not.toBeInTheDocument()
  })

  it('lets an admin through', () => {
    signIn('admin')
    renderGuard(<RequireAdmin />, '/admin-area')
    expect(screen.getByText('ADMIN CONTENT')).toBeInTheDocument()
  })
})
