import { ArrowLeft, ArrowRight, Check, Search, Swords, UserPlus, UsersRound, X } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { draftsApi, getApiError } from '../services/api'
import type { RosterTemplateSummary } from '../types/admin'
import type { DraftFormat, InvitableUser } from '../types/draft'

export function NewLobbyPage() {
  const navigate = useNavigate()
  const [format, setFormat] = useState<DraftFormat>('2v2')
  const [name, setName] = useState('Tuesday Night Draft')
  const [templates, setTemplates] = useState<RosterTemplateSummary[]>([])
  const [templateId, setTemplateId] = useState<string>('')
  const [candidates, setCandidates] = useState<InvitableUser[]>([])
  const [invited, setInvited] = useState<InvitableUser[]>([])
  const [search, setSearch] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    void Promise.all([draftsApi.rosterTemplates(), draftsApi.invitableUsers()])
      .then(([templateList, users]) => {
        if (!active) return
        setTemplates(templateList)
        setTemplateId(templateList.find((template) => template.isActive)?.id ?? templateList[0]?.id ?? '')
        setCandidates(users)
      })
      .catch((requestError) => { if (active) setError(getApiError(requestError)) })
    return () => { active = false }
  }, [])

  const invitedIds = useMemo(() => new Set(invited.map((user) => user.id)), [invited])
  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()
    return candidates
      .filter((user) => !invitedIds.has(user.id))
      .filter((user) => !term || user.displayName.toLowerCase().includes(term) || user.email.toLowerCase().includes(term))
      .slice(0, 8)
  }, [candidates, invitedIds, search])

  const maxCapacity = format === '1v1' ? 10 : 16
  const overCapacity = invited.length + 1 > maxCapacity

  const create = async () => {
    setSaving(true)
    setError('')
    try {
      const detail = await draftsApi.create({
        name: name.trim(),
        format,
        rosterTemplateId: templateId || null,
        inviteUserIds: invited.map((user) => user.id)
      })
      navigate(`/drafts/${detail.summary.id}`)
    } catch (requestError) {
      setError(getApiError(requestError))
      setSaving(false)
    }
  }

  return (
    <div className="page narrow-page">
      <Link className="back-link" to="/drafts"><ArrowLeft /> Draft hub</Link>
      <section className="setup-card">
        <div className="step-label"><span>01</span><div><strong>Choose match format</strong><small>This sets lobby and team limits.</small></div></div>
        <div className="format-grid">
          <button type="button" className={format === '1v1' ? 'selected' : ''} onClick={() => setFormat('1v1')}><Swords /><strong>1v1</strong><small>2–10 solo players</small>{format === '1v1' && <Check />}</button>
          <button type="button" className={format === '2v2' ? 'selected' : ''} onClick={() => setFormat('2v2')}><UsersRound /><strong>2v2</strong><small>4–16 players · Seed 1 + Seed 2</small>{format === '2v2' && <Check />}</button>
        </div>

        <div className="step-label"><span>02</span><div><strong>Name and roster template</strong><small>The template freezes into the draft at start.</small></div></div>
        <label className="field" htmlFor="lobby-name"><span className="field-label">Lobby name</span><input id="lobby-name" required value={name} onChange={(event) => setName(event.target.value)} /></label>
        <label className="field" htmlFor="lobby-template"><span className="field-label">Roster template</span>
          <select id="lobby-template" className="select-input" value={templateId} onChange={(event) => setTemplateId(event.target.value)}>
            {templates.length === 0 && <option value="">No templates available</option>}
            {templates.map((template) => <option key={template.id} value={template.id}>{template.name}{template.isActive ? ' · active' : ''} ({template.slotCount} slots)</option>)}
          </select>
        </label>

        <div className="step-label"><span>03</span><div><strong>Invite participants</strong><small>You join automatically as host. Invite up to {maxCapacity - 1} more.</small></div></div>
        {invited.length > 0 && (
          <ul className="invite-chip-list" aria-label="Invited participants">
            {invited.map((user) => (
              <li key={user.id} className="invite-chip">
                <span>{user.displayName}</span>
                <button type="button" aria-label={`Remove ${user.displayName}`} onClick={() => setInvited((current) => current.filter((invitee) => invitee.id !== user.id))}><X /></button>
              </li>
            ))}
          </ul>
        )}
        <div className="invite-search">
          <Search aria-hidden="true" />
          <input type="search" placeholder="Search players to invite" value={search} onChange={(event) => setSearch(event.target.value)} aria-label="Search players to invite" />
        </div>
        <ul className="invite-candidate-list" aria-label="People you can invite">
          {filtered.length === 0 && <li className="invite-empty">{candidates.length === 0 ? 'No other active players to invite yet.' : 'No matches.'}</li>}
          {filtered.map((user) => (
            <li key={user.id}>
              <div><strong>{user.displayName}</strong><small>{user.email}</small></div>
              <button type="button" className="ghost-button" disabled={overCapacity} onClick={() => setInvited((current) => [...current, user])}><UserPlus /> Invite</button>
            </li>
          ))}
        </ul>

        <div className="rule-summary">
          <span><strong>{invited.length + 1}</strong> in lobby (with you)</span>
          <span><strong>{maxCapacity}</strong> capacity</span>
          <span><strong>120s</strong> per team</span>
        </div>

        {overCapacity && <div className="form-error" role="alert">A {format} lobby holds at most {maxCapacity} participants including you.</div>}
        {error && <div className="form-error" role="alert">{error}</div>}
        <button className="primary-button" type="button" disabled={saving || !name.trim() || overCapacity} onClick={() => void create()}>
          {saving ? 'Creating lobby…' : 'Create lobby'} <ArrowRight />
        </button>
        <p className="coming-soon-note">You can invite more players, confirm attendance, and lock the lobby from the next screen.</p>
      </section>
    </div>
  )
}
