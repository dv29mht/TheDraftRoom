import { ArrowRight, KeyRound, MailCheck } from 'lucide-react'
import { FormEvent, useState } from 'react'
import { Link } from 'react-router-dom'
import { BrandMark } from '../components/BrandMark'
import { authApi, getApiError } from '../services/api'

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    setLoading(true)
    try {
      await authApi.forgotPassword(email.trim())
      setSent(true)
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="password-page">
      <BrandMark />
      <div className="auth-card password-card">
        <span className="lock-orb"><KeyRound /></span>
        <span className="eyebrow">Account recovery</span>
        <h1>Reset your password</h1>
        {sent ? (
          <>
            <p className="reset-confirmation"><MailCheck aria-hidden="true" /> If an account exists for <strong>{email}</strong>, a reset link is on its way. The link is valid for one hour.</p>
            <Link className="primary-button" to="/login">Back to sign in <ArrowRight /></Link>
          </>
        ) : (
          <>
            <p>Enter your account email and we'll send a secure link to choose a new password.</p>
            {error && <div className="form-error" role="alert">{error}</div>}
            <form className="auth-inline-form" onSubmit={submit}>
              <label className="field" htmlFor="reset-email">
                <span className="field-label">Email address</span>
                <input id="reset-email" type="email" autoComplete="email" required value={email} onChange={(event) => setEmail(event.target.value)} placeholder="name@example.com" />
              </label>
              <button className="primary-button" type="submit" disabled={loading || !email.trim()}>
                {loading ? 'Sending link…' : 'Send reset link'} <ArrowRight />
              </button>
            </form>
            <Link className="auth-secondary-link" to="/login">Remembered it? Back to sign in</Link>
          </>
        )}
      </div>
    </main>
  )
}
