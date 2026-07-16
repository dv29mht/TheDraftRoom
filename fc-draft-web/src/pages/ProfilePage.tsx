import { BellOff, CheckCircle2, KeyRound, LogOut, Mail, ShieldCheck, UserRound } from 'lucide-react'
import { FormEvent, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PasswordField } from '../components/PasswordField'
import { authApi, getApiError, meApi } from '../services/api'
import { useAuthStore } from '../stores/authStore'

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
  const [optOut, setOptOut] = useState<boolean | null>(null) // null until loaded
  const [savingPreference, setSavingPreference] = useState(false)

  useEffect(() => {
    let active = true
    meApi.emailPreferences()
      .then((preferences) => { if (active) setOptOut(preferences.optionalEmailOptOut) })
      .catch(() => { /* the toggle stays hidden if preferences cannot load */ })
    return () => { active = false }
  }, [])

  const toggleOptionalEmails = async (nextOptOut: boolean) => {
    setError('')
    setSavingPreference(true)
    try {
      const preferences = await meApi.setEmailPreferences({ optionalEmailOptOut: nextOptOut })
      setOptOut(preferences.optionalEmailOptOut)
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setSavingPreference(false)
    }
  }

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
    <div className="page">
      <section className="profile-grid">
        <article className="panel profile-card"><span className="profile-avatar">{user?.displayName.slice(0, 2).toUpperCase()}</span><h2>{user?.displayName}</h2><p>{user?.role}</p><span className="verified"><ShieldCheck /> Active account</span></article>
        <article className="panel details-card"><div><UserRound /><span><small>Display name</small><strong>{user?.displayName}</strong></span></div><div><Mail /><span><small>Email address</small><strong>{user?.email}</strong></span></div><div><KeyRound /><span><small>Password</small><strong>Private &amp; encrypted</strong></span></div></article>
      </section>

      <section className="panel security-panel" aria-labelledby="security-title">
        <div className="panel-heading"><div><span className="eyebrow">Security</span><h2 id="security-title">Password &amp; sessions</h2></div></div>
        {error && <div className="form-error" role="alert">{error}</div>}
        {notice && <div className="success-banner" role="status"><CheckCircle2 /> {notice}</div>}
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

      {/* §9.9 (PR-20): the opt-out covers OPTIONAL announcement-style emails only (e.g. draft
          reminders); invitations, cancellations, results, and security emails remain mandatory. */}
      {optOut != null && (
        <section className="panel security-panel" aria-labelledby="email-preferences-title">
          <div className="panel-heading"><div><span className="eyebrow">Email preferences</span><h2 id="email-preferences-title">Optional announcements</h2></div></div>
          <div className="security-signout">
            <div>
              <strong>{optOut ? 'Optional emails are off' : 'Optional emails are on'}</strong>
              <p>Covers nudges like draft reminders. Invitations, cancellations, results, and security emails always arrive.</p>
            </div>
            <button
              type="button"
              className={`secondary-button${optOut ? ' is-on' : ''}`}
              aria-pressed={optOut}
              disabled={savingPreference}
              onClick={() => void toggleOptionalEmails(!optOut)}
            >
              <BellOff /> {savingPreference ? 'Saving…' : optOut ? 'Opted out' : 'Opt out'}
            </button>
          </div>
        </section>
      )}
    </div>
  )
}
