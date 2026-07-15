import { ArrowRight, Check, LockKeyhole } from 'lucide-react'
import { FormEvent, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { BrandMark } from '../components/BrandMark'
import { PasswordField } from '../components/PasswordField'
import { authApi, getApiError } from '../services/api'
import { useAuthStore } from '../stores/authStore'

export function ChangePasswordPage() {
  const navigate = useNavigate()
  const user = useAuthStore((state) => state.user)
  const mustChangePassword = useAuthStore((state) => state.mustChangePassword)
  const setSession = useAuthStore((state) => state.setSession)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  if (!user) return <Navigate to="/login" replace />
  if (!mustChangePassword) return <Navigate to="/" replace />

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    setLoading(true)
    try {
      const session = await authApi.changePassword({ currentPassword, newPassword, confirmPassword })
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
        <span className="eyebrow">First sign-in security</span>
        <h1>Create your private password</h1>
        <p>The temporary password only opens this screen. Change it before entering the draft room.</p>
        {error && <div className="form-error" role="alert">{error}</div>}
        <PasswordField label="Temporary password" required value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} />
        <PasswordField label="New password" required value={newPassword} onChange={(e) => setNewPassword(e.target.value)} hint="10+ characters with uppercase, lowercase, number and symbol." />
        <PasswordField label="Confirm new password" required value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} />
        <button className="primary-button" type="submit" disabled={loading}>
          {loading ? 'Securing account…' : 'Save and continue'} <ArrowRight />
        </button>
        <div className="security-points"><span><Check /> Private</span><span><Check /> Encrypted</span><span><Check /> Revocable</span></div>
      </form>
    </main>
  )
}
