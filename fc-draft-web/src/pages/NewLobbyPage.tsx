import { ArrowLeft, ArrowRight, Check, Swords, UsersRound } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { draftRoomsApi, getApiError } from '../services/api'
import type { DraftRoom } from '../types/admin'

export function NewLobbyPage() {
  const [format, setFormat] = useState<'1v1' | '2v2'>('2v2')
  const [name, setName] = useState('Tuesday Night Draft')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [room, setRoom] = useState<DraftRoom | null>(null)

  const createRoom = async () => {
    setSaving(true)
    setError('')
    try { setRoom(await draftRoomsApi.create({ name, format })) }
    catch (requestError) { setError(getApiError(requestError)) }
    finally { setSaving(false) }
  }
  return (
    <div className="page narrow-page">
      <Link className="back-link" to="/drafts"><ArrowLeft /> Draft hub</Link>
      <section className="setup-card">
        <div className="step-label"><span>01</span><div><strong>Choose match format</strong><small>This sets lobby and team limits.</small></div></div>
        <div className="format-grid">
          <button className={format === '1v1' ? 'selected' : ''} onClick={() => setFormat('1v1')}><Swords /><strong>1v1</strong><small>2–10 solo players</small>{format === '1v1' && <Check />}</button>
          <button className={format === '2v2' ? 'selected' : ''} onClick={() => setFormat('2v2')}><UsersRound /><strong>2v2</strong><small>4–16 players · Seed 1 + Seed 2</small>{format === '2v2' && <Check />}</button>
        </div>
        <label className="field" htmlFor="lobby-name"><span className="field-label">Lobby name</span><input id="lobby-name" required value={name} onChange={(event) => setName(event.target.value)} /></label>
        <div className="rule-summary"><span><strong>{format === '1v1' ? '10' : '16'}</strong> player capacity</span><span><strong>120s</strong> per team</span><span><strong>75+</strong> rating floor</span></div>
        {error && <div className="form-error" role="alert">{error}</div>}
        {room ? <div className="room-created" role="status"><Check /><span><strong>Draft room created</strong>{room.name} · Code <b>{room.code}</b></span></div> : <button className="primary-button" type="button" disabled={saving || !name.trim()} onClick={() => void createRoom()}>{saving ? 'Creating room…' : 'Create room'} <ArrowRight /></button>}
        <p className="coming-soon-note">Creating a room now sends a live event to every connected admin.</p>
      </section>
    </div>
  )
}
