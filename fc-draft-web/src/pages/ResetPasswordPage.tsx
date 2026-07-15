import { ArrowRight, Check, LockKeyhole } from 'lucide-react'
import { FormEvent, useState } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { BrandMark } from '../components/BrandMark'
import { PasswordField } from '../components/PasswordField'
import { authApi, getApiError } from '../services/api'
import { useAuthStore } from '../stores/authStore'

export function ResetPasswordPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''
  const user = useAuthStore((state) => state.user)
  const setSession = useAuthStore((state) => state.setSession)
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  if (user) return <Navigate to="/" replace />

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    setLoading(true)
    try {
      const session = await authApi.resetPassword({ token, newPassword, confirmPassword })
      setSession(session)
      navigate('/')
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="password-page">
      <BrandMark />
      <form className="auth-card password-card" onSubmit={submit}>
        <span className="lock-orb"><LockKeyhole /></span>
        <span className="eyebrow">Account recovery</span>
        <h1>Choose a new password</h1>
        {token ? (
          <>
            <p>Set a new private password for your account. You'll be signed in straight away.</p>
            {error && <div className="form-error" role="alert">{error}</div>}
            <PasswordField label="New password" autoComplete="new-password" required value={newPassword} onChange={(event) => setNewPassword(event.target.value)} hint="10+ characters with uppercase, lowercase, number and symbol." />
            <PasswordField label="Confirm new password" autoComplete="new-password" required value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} />
            <button className="primary-button" type="submit" disabled={loading || !newPassword || !confirmPassword}>
              {loading ? 'Saving…' : 'Save and sign in'} <ArrowRight />
            </button>
            <div className="security-points"><span><Check /> Private</span><span><Check /> Encrypted</span><span><Check /> Revocable</span></div>
          </>
        ) : (
          <>
            <p className="form-error" role="alert">This reset link is missing its token. Request a new link to continue.</p>
            <Link className="primary-button" to="/forgot-password">Request a new link <ArrowRight /></Link>
          </>
        )}
      </form>
    </main>
  )
}
