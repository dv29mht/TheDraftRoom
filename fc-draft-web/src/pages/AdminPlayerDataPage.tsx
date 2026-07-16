import { AlertTriangle, CheckCircle2, Database, DownloadCloud, Layers, RefreshCw, ShieldCheck, Trophy } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { datasetsApi, getApiError } from '../services/api'
import type { DatasetIssue, DatasetVersion } from '../types/admin'
import { SuccessBanner } from '../components/ui/Feedback'

export function AdminPlayerDataPage() {
  const [versions, setVersions] = useState<DatasetVersion[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [openVersion, setOpenVersion] = useState('')
  const [issues, setIssues] = useState<DatasetIssue[]>([])

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setVersions(await datasetsApi.list())
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { void load() }, [load])

  const active = versions.find((version) => version.status === 'Active')

  const importBundled = async () => {
    setBusy('import')
    setError('')
    setNotice('')
    try {
      const report = await datasetsApi.importBundled()
      setNotice(`Imported ${report.rowsImported} players into draft version “${report.label}”. ${report.errorCount} errors, ${report.warningCount} warnings.`)
      await load()
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setBusy('') }
  }

  const activate = async (version: DatasetVersion) => {
    setBusy(version.id)
    setError('')
    setNotice('')
    try {
      await datasetsApi.activate(version.id)
      setNotice(`“${version.label}” is now the active dataset.`)
      await load()
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setBusy('') }
  }

  const toggleIssues = async (version: DatasetVersion) => {
    if (openVersion === version.id) { setOpenVersion(''); return }
    setOpenVersion(version.id)
    setIssues([])
    try {
      const detail = await datasetsApi.get(version.id)
      setIssues(detail.issues)
    } catch (requestError) { setError(getApiError(requestError)) }
  }

  return (
    <div className="page">
      <h1 className="sr-only">Player data</h1>
      <section className="stat-grid">
        <article><span className="stat-icon primary"><Database /></span><div><strong>{active ? active.footballerCount.toLocaleString() : '—'}</strong><small>Active players</small></div></article>
        <article><span className="stat-icon accent"><Trophy /></span><div><strong>{active ? active.clubCount.toLocaleString() : '—'}</strong><small>Clubs represented</small></div></article>
        <article><span className="stat-icon gold"><Layers /></span><div><strong>{versions.length}</strong><small>Dataset versions</small></div></article>
      </section>

      {error && <div className="form-error" role="alert">{error}</div>}
      {notice && <SuccessBanner onDismiss={() => setNotice('')}>{notice}</SuccessBanner>}

      <section className="panel">
        <div className="directory-toolbar">
          <div><span className="eyebrow">Dataset</span><h2>Versions</h2></div>
          <button className="primary-button compact" onClick={() => void importBundled()} disabled={busy === 'import'}>
            {busy === 'import' ? <><RefreshCw className="spin" /> Importing…</> : <><DownloadCloud /> Import bundled FC 26 dataset</>}
          </button>
        </div>
        {loading ? <div className="loading-state" role="status"><RefreshCw className="spin" /> Loading dataset versions…</div> : versions.length ? (
          <div className="table-scroll">
            <table className="users-table">
              <thead><tr><th scope="col">Version</th><th scope="col">Status</th><th scope="col">Players</th><th scope="col">Clubs</th><th scope="col">Validation</th><th scope="col"><span className="sr-only">Actions</span></th></tr></thead>
              <tbody>{versions.map((version) => (
                <tr key={version.id}>
                  <td data-label="Version"><strong>{version.label}</strong></td>
                  <td data-label="Status"><span className={`status-label ${version.status === 'Active' ? 'active' : version.status === 'Draft' ? 'pending' : 'deactivated'}`}><span />{version.status}</span></td>
                  <td data-label="Players">{version.footballerCount.toLocaleString()}</td>
                  <td data-label="Clubs">{version.clubCount.toLocaleString()}</td>
                  <td data-label="Validation">
                    {version.errorCount + version.warningCount === 0
                      ? <span className="dataset-clean"><ShieldCheck /> Clean</span>
                      : <button className="link-button" aria-expanded={openVersion === version.id} aria-controls="import-issues-panel" onClick={() => void toggleIssues(version)}><AlertTriangle /> {version.errorCount} errors · {version.warningCount} warnings</button>}
                  </td>
                  <td data-label="Action"><div className="table-actions">
                    {version.status !== 'Active' && version.errorCount === 0 && (
                      <button className="secondary-button" onClick={() => void activate(version)} disabled={busy === version.id}>{busy === version.id ? 'Activating…' : 'Activate'}</button>
                    )}
                  </div></td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        ) : <div className="empty-list"><Database /><strong>No dataset versions yet</strong><span>Import the bundled FC 26 dataset to get started.</span></div>}
      </section>

      {openVersion && issues.length > 0 && (
        <section className="panel" id="import-issues-panel" aria-label="Import issues">
          <div className="panel-heading"><div><span className="eyebrow">Validation</span><h2>Import issues</h2></div></div>
          <ul className="issue-list">
            {issues.map((issue, index) => (
              <li key={index} className={issue.severity === 'Error' ? 'issue-error' : 'issue-warning'}>
                <span className="issue-badge">{issue.severity}</span>
                <span>Row {issue.row}{issue.externalId ? ` · #${issue.externalId}` : ''}{issue.field ? ` · ${issue.field}` : ''}: {issue.message}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="panel data-rules-card">
        <span className="eyebrow">Attribution &amp; rights</span><h2>Data source</h2>
        <ul>
          <li><CheckCircle2 /> EA public FC 26 ratings feed is authoritative (men's base / Kick Off only)</li>
          <li><CheckCircle2 /> Overall 75+ enforced for draft pools; alternate positions, roles and PlayStyles retained</li>
          <li><AlertTriangle /> Club five-star ratings are not in the source feed and are curated separately (roster config)</li>
          <li><AlertTriangle /> Licensed media (crests, photos) is deferred until rights are confirmed</li>
        </ul>
      </section>
    </div>
  )
}
