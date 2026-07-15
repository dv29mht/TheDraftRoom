import { CheckCircle2, Database, ExternalLink, RefreshCw, ShieldCheck, Trophy } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'

type PlayerRecord = { id: number; overall: number; club: string; league: string; position: string }
type PlayerDataset = { version: string; source: string; players: PlayerRecord[] }

export function AdminPlayerDataPage() {
  const [dataset, setDataset] = useState<PlayerDataset | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    fetch('/data/fc26-players.json', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error('Player dataset could not be loaded.')
        return response.json() as Promise<PlayerDataset>
      })
      .then(setDataset)
      .catch((requestError: unknown) => {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') return
        setError(requestError instanceof Error ? requestError.message : 'Player dataset could not be loaded.')
      })
    return () => controller.abort()
  }, [])

  const summary = useMemo(() => {
    if (!dataset) return null
    return {
      clubs: new Set(dataset.players.map((player) => player.club)).size,
      leagues: new Set(dataset.players.map((player) => player.league)).size,
      eligible: dataset.players.filter((player) => player.overall >= 75).length,
      highest: Math.max(...dataset.players.map((player) => player.overall))
    }
  }, [dataset])

  return (
    <div className="page">
      {error && <div className="panel empty-list" role="alert"><Database /><strong>Player data unavailable</strong><span>{error}</span></div>}
      {!error && (!dataset || !summary) ? <div className="panel loading-state"><RefreshCw className="spin" /> Reading player dataset…</div> : dataset && summary ? <>
        <section className="stat-grid">
          <article><span className="stat-icon primary"><Database /></span><div><strong>{dataset.players.length.toLocaleString()}</strong><small>Imported players</small></div></article>
          <article><span className="stat-icon accent"><Trophy /></span><div><strong>{summary.clubs}</strong><small>Clubs represented</small></div></article>
          <article><span className="stat-icon gold"><ShieldCheck /></span><div><strong>{summary.leagues}</strong><small>Leagues represented</small></div></article>
        </section>
        <section className="admin-data-grid">
          <article className="panel dataset-status-card">
            <span className="eyebrow">Current source</span><h2>{dataset.version}</h2>
            <div className="dataset-health"><CheckCircle2 /><div><strong>Dataset ready</strong><small>{summary.eligible.toLocaleString()} players meet the 75+ rule · highest overall {summary.highest}</small></div></div>
            <a className="secondary-button" href={dataset.source} target="_blank" rel="noreferrer">View source <ExternalLink /></a>
          </article>
          <article className="panel data-rules-card">
            <span className="eyebrow">Validation</span><h2>Draft eligibility</h2>
            <ul><li><CheckCircle2 /> Men's base player pool</li><li><CheckCircle2 /> Overall rating 75 or higher</li><li><CheckCircle2 /> Alternate positions included</li><li><CheckCircle2 /> Roles and PlayStyles retained</li></ul>
          </article>
        </section>
      </> : null}
    </div>
  )
}
