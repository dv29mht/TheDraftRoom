import { KeyRound, LogOut, Mail, ShieldCheck, UserRound } from 'lucide-react'
import { FormEvent, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PasswordField } from '../components/PasswordField'
import { authApi, getApiError } from '../services/api'
import { useAuthStore } from '../stores/authStore'
import { SuccessBanner } from '../components/ui/Feedback'

export function ProfilePage() {
  const navigate = useNavigate()
  const user = useAuthStore((state) => state.user)
  const setSession = useAuthStore((state) => state.setSession)
  const logout = useAuthStore((state) => state.logout)

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [saving, setSaving] = useState(false)
  const [revoking, setRevoking] = useState(false)

  const changePassword = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    setNotice('')
    setSaving(true)
    try {
      const session = await authApi.changePassword({ currentPassword, newPassword, confirmPassword })
      setSession(session)
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setNotice('Your password was changed. Other sessions have been signed out.')
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setSaving(false)
    }
  }

  const signOutEverywhere = async () => {
    setError('')
    setRevoking(true)
    try {
      await authApi.logoutAll()
      logout()
      navigate('/login')
    } catch (requestError) {
      setError(getApiError(requestError))
      setRevoking(false)
    }
  }

  return (
    <div className="page profile-page">
      <section className="profile-layout">
        {/* A single identity card (PR: consolidated from the former avatar + details cards). */}
        <article className="panel profile-identity-card">
          <span className="profile-avatar" aria-hidden="true">{user?.displayName.slice(0, 2).toUpperCase()}</span>
          <h1>{user?.displayName}</h1>
          <p>{user?.role}</p>
          <span className="verified"><ShieldCheck /> Active account</span>
          <div className="profile-identity-details">
            <div><UserRound aria-hidden="true" /><span><small>Display name</small><strong>{user?.displayName}</strong></span></div>
            <div><Mail aria-hidden="true" /><span><small>Email address</small><strong>{user?.email}</strong></span></div>
            <div><KeyRound aria-hidden="true" /><span><small>Password</small><strong>Private &amp; encrypted</strong></span></div>
          </div>
        </article>

        <section className="panel security-panel" aria-labelledby="security-title">
          <div className="panel-heading"><div><span className="eyebrow">Security</span><h2 id="security-title">Password &amp; sessions</h2></div></div>
          {error && <div className="form-error" role="alert">{error}</div>}
          {notice && <SuccessBanner onDismiss={() => setNotice('')}>{notice}</SuccessBanner>}
          <form className="security-form" onSubmit={changePassword}>
            <PasswordField label="Current password" autoComplete="current-password" required value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} />
            <PasswordField label="New password" autoComplete="new-password" required value={newPassword} onChange={(event) => setNewPassword(event.target.value)} hint="10+ characters with uppercase, lowercase, number and symbol." />
            <PasswordField label="Confirm new password" autoComplete="new-password" required value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} />
            <button className="primary-button compact" type="submit" disabled={saving || !currentPassword || !newPassword || !confirmPassword}>{saving ? 'Updating…' : 'Change password'}</button>
          </form>
          <div className="security-signout">
            <div>
              <strong>Sign out everywhere</strong>
              <p>Revoke every active session on all devices. You'll need to sign in again.</p>
            </div>
            <button type="button" className="secondary-button" onClick={() => void signOutEverywhere()} disabled={revoking}><LogOut /> {revoking ? 'Signing out…' : 'Sign out everywhere'}</button>
          </div>
        </section>
      </section>
    </div>
  )
}
