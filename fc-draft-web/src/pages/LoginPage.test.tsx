import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { LoginPage } from './LoginPage'
import { authApi } from '../services/api'
import { useAuthStore } from '../stores/authStore'

const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }))

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>()
  return { ...actual, useNavigate: () => navigate }
})

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return { ...actual, authApi: { ...actual.authApi, login: vi.fn() } }
})

const loginMock = authApi.login as unknown as Mock

function renderLogin() {
  return render(
    <MemoryRouter>
      <LoginPage />
    </MemoryRouter>,
  )
}

async function fillCredentials(email: string, password: string) {
  await userEvent.type(screen.getByLabelText(/email address/i), email)
  await userEvent.type(screen.getByLabelText('Password'), password)
}

beforeEach(() => {
  navigate.mockReset()
  loginMock.mockReset()
  useAuthStore.setState({ user: null, accessToken: null, mustChangePassword: false })
  localStorage.clear()
})

describe('LoginPage', () => {
  it('renders the sign-in form with empty credential fields', () => {
    renderLogin()
    expect(screen.getByRole('heading', { name: /enter the draft room/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/email address/i)).toHaveValue('')
  })

  it('signs in, stores the session and routes to the dashboard', async () => {
    loginMock.mockResolvedValue({
      accessToken: 'token',
      expiresAt: '2026-07-14T12:00:00Z',
      mustChangePassword: false,
      user: { id: '1', displayName: 'Admin', email: 'mdevansh@gmail.com', role: 'admin' },
    })

    renderLogin()
    await fillCredentials('mdevansh@gmail.com', 'DraftAdmin@2026')
    await userEvent.click(screen.getByRole('button', { name: /enter draft room/i }))

    await waitFor(() => expect(loginMock).toHaveBeenCalledWith('mdevansh@gmail.com', 'DraftAdmin@2026'))
    expect(useAuthStore.getState().user?.email).toBe('mdevansh@gmail.com')
    expect(navigate).toHaveBeenCalledWith('/')
  })

  it('routes to the password change screen when a change is pending', async () => {
    loginMock.mockResolvedValue({
      accessToken: 'token',
      expiresAt: '2026-07-14T12:00:00Z',
      mustChangePassword: true,
      user: { id: '2', displayName: 'Rookie', email: 'rookie@draftroom.dev', role: 'player' },
    })

    renderLogin()
    await fillCredentials('rookie@draftroom.dev', 'Temp@123456')
    await userEvent.click(screen.getByRole('button', { name: /enter draft room/i }))

    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/change-password'))
  })

  it('shows an error message when the credentials are rejected', async () => {
    loginMock.mockRejectedValue(new Error('bad credentials'))

    renderLogin()
    await fillCredentials('someone@example.com', 'WrongPass@1')
    await userEvent.click(screen.getByRole('button', { name: /enter draft room/i }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(navigate).not.toHaveBeenCalled()
  })

  it('offers a sign-out switch instead of the form when already signed in', () => {
    useAuthStore.setState({
      user: { id: '1', displayName: 'Admin', email: 'mdevansh@gmail.com', role: 'admin' },
      accessToken: 'token',
      mustChangePassword: false,
    })

    renderLogin()
    expect(screen.getByRole('heading', { name: /already signed in/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /sign out to use a different account/i })).toBeInTheDocument()
    expect(screen.queryByLabelText(/email address/i)).not.toBeInTheDocument()
  })
})
