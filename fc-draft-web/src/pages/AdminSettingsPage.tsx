import { AlertTriangle, CheckCircle2, Database, Link2, Mail, RefreshCw, Server, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { adminSettingsApi, getApiError } from '../services/api'
import type { AdminSettingsStatus } from '../types/admin'

export function AdminSettingsPage() {
  const [settings, setSettings] = useState<AdminSettingsStatus | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    adminSettingsApi.get()
      .then((next) => { if (active) setSettings(next) })
      .catch((requestError) => { if (active) setError(getApiError(requestError)) })
    return () => { active = false }
  }, [])

  return (
    <div className="page">
      <h1 className="sr-only">Platform settings</h1>
      {error && <div className="panel empty-list" role="alert"><Server /><strong>Settings unavailable</strong><span>{error}</span></div>}
      {!error && !settings ? <div className="panel loading-state" role="status"><RefreshCw className="spin" /> Loading platform settings…</div> : settings ? <>
        <section className={`settings-banner ${settings.emailConfigured ? 'ready' : 'warning'}`}>
          {settings.emailConfigured ? <CheckCircle2 /> : <AlertTriangle />}
          <div><strong>{settings.emailConfigured ? 'Invitation email is configured' : 'Invitation email needs configuration'}</strong><span>{settings.emailConfigured ? 'Brevo can deliver account invitations.' : 'Set Brevo:ApiKey and Brevo:SenderEmail in the API configuration.'}</span></div>
        </section>
        <section className="settings-grid">
          <article className="panel setting-card"><span className="setting-icon"><Server /></span><div><small>Environment</small><strong>{settings.environment}</strong><p>Current API hosting environment.</p></div></article>
          <article className="panel setting-card"><span className="setting-icon"><Database /></span><div><small>Storage</small><strong>{settings.storage}</strong><p>Users and rooms reset when the API restarts.</p></div></article>
          <article className="panel setting-card"><span className="setting-icon"><Mail /></span><div><small>Email sender</small><strong>{settings.senderName}</strong><p>{settings.senderEmail ?? 'Sender email not configured'}</p></div></article>
          <article className="panel setting-card"><span className="setting-icon"><Link2 /></span><div><small>Invitation login URL</small><strong>{settings.loginUrl}</strong><p>Destination included in invitation emails.</p></div></article>
          <article className="panel setting-card"><span className="setting-icon"><ShieldCheck /></span><div><small>Access model</small><strong>Private and role protected</strong><p>Administration routes require an administrator token.</p></div></article>
        </section>
      </> : null}
    </div>
  )
}
