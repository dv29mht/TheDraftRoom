import { ArrowRight, LoaderCircle, Radio, ShieldCheck, Sparkles } from 'lucide-react'
import { FormEvent, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { BrandMark } from '../components/BrandMark'
import { PasswordField } from '../components/PasswordField'
import { authApi, getApiError } from '../services/api'
import { useAuthStore } from '../stores/authStore'

export function LoginPage() {
  const navigate = useNavigate()
  const existingUser = useAuthStore((state) => state.user)
  const setSession = useAuthStore((state) => state.setSession)
  const [email, setEmail] = useState('mdevansh@gmail.com')
  const [password, setPassword] = useState('Dv@241429')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  if (existingUser) return <Navigate to="/" replace />

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    setLoading(true)
    try {
      const session = await authApi.login(email, password)
      setSession(session)
      navigate(session.mustChangePassword ? '/change-password' : '/')
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="auth-page">
      <section className="auth-story">
        <BrandMark />
        <div className="auth-tunnel" aria-hidden="true"><span /><span /><span /></div>
        <div className="story-copy">
          <span className="eyebrow"><Radio /> Live tournament drafting</span>
          <h1>Build the squad.<br/><em>Own the room.</em></h1>
          <p>Seed fair teams, spin the order, protect your star and draft every position live.</p>
        </div>
        <div className="match-strip">
          <span><strong>10</strong><small>1v1 players</small></span>
          <span><strong>16</strong><small>2v2 players</small></span>
          <span><strong>120</strong><small>sec per pick</small></span>
        </div>
      </section>
      <section className="auth-panel" aria-labelledby="login-heading">
        <form className="auth-card" onSubmit={submit} aria-busy={loading}>
          <span className="eyebrow"><Sparkles /> Welcome back</span>
          <h2 id="login-heading">Enter the draft room</h2>
          <p>Use the credentials shared by your tournament admin.</p>
          {error && <div className="form-error" role="alert">{error}</div>}
          <label className="field" htmlFor="email">
            <span className="field-label">Email address</span>
            <input id="email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <PasswordField label="Password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? <><LoaderCircle className="button-spinner" aria-hidden="true" /> Entering the room…</> : <>Enter draft room <ArrowRight /></>}
          </button>
          <div className="demo-note"><ShieldCheck /><span><strong>Development access</strong>mdevansh@gmail.com / Dv@241429</span></div>
        </form>
      </section>
    </main>
  )
}
