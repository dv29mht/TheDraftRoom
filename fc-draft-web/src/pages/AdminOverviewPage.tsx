import { Activity, AlertTriangle, CheckCircle2, Info, Mail, RefreshCw, Trophy, UsersRound, Zap } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { overviewApi, getApiError } from '../services/api'
import type { AdminOverview } from '../types/admin'
import { draftStatusLabel } from '../utils/draftStatus'
import { ErrorBanner, LoadingState } from '../components/ui/Feedback'

const percent = (rate: number) => `${Math.round(rate * 100)}%`

/**
 * The admin Overview dashboard (§8.2): a read-only user, draft, and engagement summary plus an
 * alerts strip, served by GET /api/admin/overview. Every figure is derived server-side from the
 * existing stores/event trail, so it reads the same on both storage branches.
 */
export function AdminOverviewPage() {
  const [overview, setOverview] = useState<AdminOverview | null>(null)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState('')

  const load = useCallback(async (isRefresh = false) => {
    if (isRefresh) setRefreshing(true)
    setError('')
    try {
      setOverview(await overviewApi.get())
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    let active = true
    overviewApi.get()
      .then((data) => { if (active) setOverview(data) })
      .catch((requestError) => { if (active) setError(getApiError(requestError)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  if (loading) return <LoadingState>Loading the overview…</LoadingState>

  return (
    <div className="page admin-overview-page">
      <div className="page-heading">
        <div>
          <span className="eyebrow">Admin</span>
          <h1>Overview</h1>
        </div>
        <button className="secondary-button compact" onClick={() => load(true)} disabled={refreshing}>
          <RefreshCw aria-hidden="true" /> {refreshing ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {error && <ErrorBanner>{error}</ErrorBanner>}

      {overview && (
        <>
          <section className="stat-grid" aria-label="Accounts">
            <article><span className="stat-icon primary"><UsersRound /></span><div><strong>{overview.users.total}</strong><small>Accounts</small></div></article>
            <article><span className="stat-icon gold"><CheckCircle2 /></span><div><strong>{overview.users.activated}</strong><small>Activated</small></div></article>
            <article><span className="stat-icon accent"><Mail /></span><div><strong>{overview.users.awaitingActivation}</strong><small>Awaiting activation</small></div></article>
          </section>

          <section className="stat-grid" aria-label="Drafts">
            <article><span className="stat-icon primary"><Trophy /></span><div><strong>{overview.drafts.total}</strong><small>Drafts</small></div></article>
            <article><span className="stat-icon accent"><Activity /></span><div><strong>{overview.drafts.live}</strong><small>Live now</small></div></article>
            <article><span className="stat-icon gold"><CheckCircle2 /></span><div><strong>{overview.drafts.completed}</strong><small>Completed</small></div></article>
          </section>

          <div className="content-grid">
            <section className="panel" aria-labelledby="overview-engagement">
              <div className="panel-heading">
                <div><span className="eyebrow">Engagement</span><h2 id="overview-engagement">Conversion &amp; activity</h2></div>
              </div>
              <dl className="overview-metrics">
                <div><dt>Invite → activation</dt><dd>{overview.users.activated} of {overview.users.total} accounts</dd></div>
                <div><dt>Lobby → draft start</dt><dd>{percent(overview.engagement.lobbyToStartRate)} <small>({overview.engagement.started} of {overview.engagement.created})</small></dd></div>
                <div><dt>Draft completion</dt><dd>{percent(overview.engagement.completionRate)} <small>({overview.engagement.completed} of {overview.engagement.started})</small></dd></div>
                <div><dt>Picks accepted</dt><dd>{overview.engagement.picksAccepted}</dd></div>
                <div><dt>Timer auto-picks</dt><dd><Zap aria-hidden="true" className="inline-icon" /> {percent(overview.engagement.autoPickRate)} <small>({overview.engagement.autoPicks})</small></dd></div>
                <div><dt>Format split</dt><dd>{overview.drafts.oneVOne} × 1v1 · {overview.drafts.twoVTwo} × 2v2</dd></div>
              </dl>
            </section>

            <section className="panel" aria-labelledby="overview-alerts">
              <div className="panel-heading">
                <div><span className="eyebrow">Needs attention</span><h2 id="overview-alerts">Alerts</h2></div>
              </div>
              {overview.alerts.length === 0 ? (
                <p className="empty-list">No alerts — everything looks healthy.</p>
              ) : (
                <ul className="overview-alerts" aria-label="Alerts">
                  {overview.alerts.map((alert, index) => (
                    <li key={index} className={`overview-alert ${alert.severity}`}>
                      {alert.severity === 'warning'
                        ? <AlertTriangle aria-hidden="true" />
                        : <Info aria-hidden="true" />}
                      <span>{alert.message}</span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>

          <div className="content-grid">
            <section className="panel" aria-labelledby="overview-status">
              <div className="panel-heading">
                <div><span className="eyebrow">Live picture</span><h2 id="overview-status">Drafts by status</h2></div>
              </div>
              {Object.keys(overview.drafts.byStatus).length === 0 ? (
                <p className="empty-list">No drafts yet.</p>
              ) : (
                <ul className="overview-status-list" aria-label="Drafts by status">
                  {Object.entries(overview.drafts.byStatus).map(([status, count]) => (
                    <li key={status}><span>{draftStatusLabel(status)}</span><strong>{count}</strong></li>
                  ))}
                </ul>
              )}
            </section>

            <section className="panel" aria-labelledby="overview-email">
              <div className="panel-heading">
                <div><span className="eyebrow">Communications</span><h2 id="overview-email">Email delivery</h2></div>
              </div>
              <div className="overview-email-tallies">
                <span className="outbox-status outbox-pending">{overview.email.pending} pending</span>
                <span className="outbox-status outbox-sent"><CheckCircle2 aria-hidden="true" /> {overview.email.sent} sent</span>
                <span className="outbox-status outbox-failed"><AlertTriangle aria-hidden="true" /> {overview.email.failed} failed</span>
              </div>
            </section>
          </div>
        </>
      )}
    </div>
  )
}
