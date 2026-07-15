import { CalendarClock, DraftingCompass, Plus, Radio, RefreshCw, UsersRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { draftRoomsApi, getApiError } from '../services/api'
import type { DraftRoom } from '../types/admin'

export function AdminDraftsPage() {
  const [rooms, setRooms] = useState<DraftRoom[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    draftRoomsApi.list()
      .then((next) => { if (active) setRooms(next) })
      .catch((requestError) => { if (active) setError(getApiError(requestError)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  const oneVOne = rooms.filter((room) => room.format === '1v1').length
  const twoVTwo = rooms.filter((room) => room.format === '2v2').length

  return (
    <div className="page">
      <section className="stat-grid">
        <article><span className="stat-icon primary"><DraftingCompass /></span><div><strong>{rooms.length}</strong><small>Total draft rooms</small></div></article>
        <article><span className="stat-icon accent"><UsersRound /></span><div><strong>{oneVOne}</strong><small>1v1 rooms</small></div></article>
        <article><span className="stat-icon gold"><Radio /></span><div><strong>{twoVTwo}</strong><small>2v2 rooms</small></div></article>
      </section>

      {error && <div className="form-error" role="alert">{error}</div>}
      <section className="panel admin-module-panel">
        <div className="directory-toolbar">
          <div><span className="eyebrow">Operations</span><h2>Draft rooms</h2></div>
          <Link className="primary-button compact" to="/drafts/new"><Plus /> Create room</Link>
        </div>
        {loading ? <div className="loading-state"><RefreshCw className="spin" /> Loading draft rooms…</div> : rooms.length ? (
          <div className="admin-card-list">
            {rooms.map((room) => <article className="admin-list-card" key={room.id}>
              <span className="admin-list-icon"><DraftingCompass /></span>
              <div><strong>{room.name}</strong><small>Hosted by {room.hostName}</small></div>
              <span className="format-badge">{room.format}</span>
              <code>{room.code}</code>
              <time dateTime={room.createdAt}><CalendarClock /> {new Date(room.createdAt).toLocaleString()}</time>
            </article>)}
          </div>
        ) : <div className="empty-list"><DraftingCompass /><strong>No draft rooms yet</strong><span>Create the first room to begin tracking operations.</span><Link className="secondary-button" to="/drafts/new">Create a room</Link></div>}
      </section>
    </div>
  )
}
