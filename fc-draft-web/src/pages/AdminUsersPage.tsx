import { CheckCircle2, ChevronLeft, ChevronRight, Mail, Pencil, RefreshCw, Search, Send, UserCheck, UserCog, UsersRound, UserX, X } from 'lucide-react'
import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { getApiError, usersApi } from '../services/api'
import type { ManagedUser, PagedUsers } from '../types/admin'
import type { UserRole } from '../types/auth'
import { Modal } from '../components/ui/Modal'
import { ErrorBanner, LoadingState, SuccessBanner } from '../components/ui/Feedback'

const emptyResult: PagedUsers = { items: [], page: 1, pageSize: 10, total: 0, totalPages: 1, invitedCount: 0, activatedCount: 0 }

function initialsFor(name: string) {
  return name.split(' ').map((part) => part[0]).join('').slice(0, 2).toUpperCase()
}

export function AdminUsersPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [result, setResult] = useState<PagedUsers>(emptyResult)
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const showForm = searchParams.get('invite') === '1'
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [sendingId, setSendingId] = useState('')
  const [statusId, setStatusId] = useState('')
  const [editCandidate, setEditCandidate] = useState<ManagedUser | null>(null)
  const [deactivateCandidate, setDeactivateCandidate] = useState<ManagedUser | null>(null)
  const [editName, setEditName] = useState('')
  const [editEmail, setEditEmail] = useState('')
  const [editRole, setEditRole] = useState<UserRole>('player')
  const [editTeamName, setEditTeamName] = useState('')
  const [editAvatar, setEditAvatar] = useState('')
  const [savingEdit, setSavingEdit] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const loadUsers = useCallback(async (requestedPage = page) => {
    setLoading(true)
    setError('')
    try {
      const next = await usersApi.list({ page: requestedPage, pageSize, search: query.trim() || undefined })
      setResult(next)
      setPage(next.page)
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setLoading(false) }
  }, [page, pageSize, query])

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadUsers() }, 250)
    return () => window.clearTimeout(timer)
  }, [loadUsers])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setError('')
    setNotice('')
    try {
      const user = await usersApi.create({ displayName: name.trim(), email: email.trim() })
      setName('')
      setEmail('')
      setSearchParams({}, { replace: true })
      setNotice(`Invitation sent to ${user.email}.`)
      setPage(1)
      await loadUsers(1)
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setSaving(false) }
  }

  const resendInvite = async (user: ManagedUser) => {
    setSendingId(user.id)
    setError('')
    setNotice('')
    try {
      const updated = await usersApi.sendInvite(user.id)
      setResult((current) => ({ ...current, items: current.items.map((item) => item.id === updated.id ? updated : item) }))
      setNotice(`Invite resent to ${updated.email}.`)
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setSendingId('') }
  }

  const toggleStatus = async (user: ManagedUser) => {
    const nextStatus = user.status === 'active' ? 'deactivated' : 'active'
    setStatusId(user.id)
    setError('')
    setNotice('')
    try {
      const updated = await usersApi.setStatus(user.id, nextStatus)
      setResult((current) => ({ ...current, items: current.items.map((item) => item.id === updated.id ? updated : item) }))
      setNotice(nextStatus === 'deactivated'
        ? `${updated.displayName} was deactivated and can no longer sign in or join drafts.`
        : `${updated.displayName} was reactivated.`)
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setStatusId('') }
  }

  /* Deactivation is destructive (blocks sign-in and drafts), so it always confirms first. */
  const confirmDeactivate = async () => {
    if (!deactivateCandidate) return
    const candidate = deactivateCandidate
    setDeactivateCandidate(null)
    await toggleStatus(candidate)
  }

  const openEdit = (user: ManagedUser) => {
    setEditCandidate(user)
    setEditName(user.displayName)
    setEditEmail(user.email)
    setEditRole(user.role)
    setEditTeamName(user.preferredTeamName ?? '')
    setEditAvatar(user.avatarUrl ?? '')
    setError('')
    setNotice('')
  }

  const submitEdit = async (event: FormEvent) => {
    event.preventDefault()
    if (!editCandidate) return
    setSavingEdit(true)
    setError('')
    setNotice('')
    try {
      const updated = await usersApi.update(editCandidate.id, {
        displayName: editName.trim(),
        email: editEmail.trim(),
        role: editRole,
        preferredTeamName: editTeamName.trim() || null,
        avatarUrl: editAvatar.trim() || null
      })
      setResult((current) => ({
        ...current,
        items: current.items.map((item) => (item.id === updated.id ? updated : item))
      }))
      setNotice(`${updated.displayName} was updated.`)
      setEditCandidate(null)
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setSavingEdit(false) }
  }

  const firstItem = result.total ? (result.page - 1) * result.pageSize + 1 : 0
  const lastItem = Math.min(result.page * result.pageSize, result.total)

  return (
    <div className="page users-page">
      <h1 className="sr-only">User management</h1>
      <section className="stat-grid user-summary">
        <article><span className="stat-icon primary"><UsersRound /></span><div><strong>{result.total}</strong><small>Matching accounts</small></div></article>
        <article><span className="stat-icon accent"><Mail /></span><div><strong>{result.invitedCount}</strong><small>Invites issued</small></div></article>
        <article><span className="stat-icon gold"><CheckCircle2 /></span><div><strong>{result.activatedCount}</strong><small>Activated</small></div></article>
      </section>

      {showForm && (
        <section className="panel create-user-panel" aria-labelledby="create-user-title">
          <div className="panel-heading"><div><span className="eyebrow">New private account</span><h2 id="create-user-title">Create and invite a user</h2></div><button className="icon-button" onClick={() => setSearchParams({}, { replace: true })} aria-label="Close new user form"><X /></button></div>
          <form className="user-form" onSubmit={submit}>
            <label className="field" htmlFor="new-user-name"><span className="field-label">Name</span><input id="new-user-name" required autoComplete="name" value={name} onChange={(event) => setName(event.target.value)} placeholder="Full name" /></label>
            <label className="field" htmlFor="new-user-email"><span className="field-label">Email address</span><input id="new-user-email" required type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="name@example.com" /></label>
            <button className="primary-button compact form-submit" disabled={saving || !name.trim() || !email.trim()}>{saving ? 'Creating…' : 'Create & send invite'} <Send /></button>
          </form>
          <p className="mailer-note"><Mail /> New accounts are players. Brevo sends a unique one-time password, and resending invalidates the previous password.</p>
        </section>
      )}

      {error && <ErrorBanner>{error}</ErrorBanner>}
      {notice && <SuccessBanner onDismiss={() => setNotice('')}>{notice}</SuccessBanner>}

      <section className="panel users-directory">
        <div className="directory-toolbar">
          <div><span className="eyebrow">Directory</span><h2>All users</h2></div>
          <div className="directory-actions">
            <label className="search-control"><Search aria-hidden="true" /><span className="sr-only">Search users</span><input value={query} onChange={(event) => { setQuery(event.target.value); setPage(1) }} placeholder="Search name or email" /></label>
          </div>
        </div>
        {loading ? <LoadingState>Loading users…</LoadingState> : result.items.length ? (
          <div className="table-scroll">
            <table className="users-table">
              <thead><tr><th scope="col">Name</th><th scope="col">Email</th><th scope="col">Role</th><th scope="col">Status</th><th scope="col">Invitation</th><th scope="col"><span className="sr-only">Actions</span></th></tr></thead>
              <tbody>{result.items.map((user) => (
                <tr key={user.id}>
                  <td data-label="Name"><span className="table-person">
                    {user.avatarUrl
                      ? <img className="user-avatar user-avatar-image" src={user.avatarUrl} alt="" width="36" height="36" loading="lazy" />
                      : <span className="user-avatar" aria-hidden="true">{initialsFor(user.displayName)}</span>}
                    <span className="table-person-detail"><strong>{user.displayName}</strong>{user.preferredTeamName && <small>{user.preferredTeamName}</small>}</span>
                  </span></td>
                  <td data-label="Email">{user.email}</td>
                  <td data-label="Role"><span className={`role-badge ${user.role}`}>{user.role}</span></td>
                  <td data-label="Status">{user.status === 'deactivated'
                    ? <span className="status-label deactivated"><span />Deactivated</span>
                    : <span className={`status-label ${user.mustChangePassword ? 'pending' : 'active'}`}><span />{user.mustChangePassword ? 'Pending activation' : 'Active'}</span>}
                  </td>
                  <td data-label="Invitation">{user.invitationSentAt
                    ? <span className="invite-cell"><strong>Sent</strong><small>{new Date(user.invitationSentAt).toLocaleDateString()}</small></span>
                    : <><span aria-hidden="true">–</span><span className="sr-only">No invitation sent</span></>}
                  </td>
                  <td data-label="Action"><div className="table-actions">
                    <button className="secondary-button" onClick={() => openEdit(user)} aria-label={`Edit ${user.displayName}`}><Pencil /> Edit</button>
                    {user.role === 'player' ? (
                      <>
                        <button className="secondary-button" disabled={sendingId === user.id || statusId === user.id} onClick={() => void resendInvite(user)}><Send /> {sendingId === user.id ? 'Sending…' : 'Send invite'}</button>
                        <button className="secondary-button" disabled={statusId === user.id} onClick={() => user.status === 'active' ? setDeactivateCandidate(user) : void toggleStatus(user)} aria-label={user.status === 'active' ? `Deactivate ${user.displayName}` : `Activate ${user.displayName}`}>{user.status === 'active' ? <UserX /> : <UserCheck />} {statusId === user.id ? 'Saving…' : user.status === 'active' ? 'Deactivate' : 'Activate'}</button>
                      </>
                    ) : <span className="protected-account"><CheckCircle2 aria-hidden="true" /> Protected</span>}
                  </div></td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        ) : <div className="empty-list"><Search /><strong>No matching users</strong><span>Try a different name or email address.</span></div>}

        <footer className="table-pagination">
          <label>Rows per page<select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1) }}><option value="10">10</option><option value="25">25</option><option value="50">50</option></select></label>
          <span>{firstItem}–{lastItem} of {result.total}</span>
          <div className="pagination-actions">
            <button className="icon-button" disabled={result.page <= 1} onClick={() => setPage((current) => current - 1)} aria-label="Previous page"><ChevronLeft /></button>
            <span>Page {result.page} of {result.totalPages}</span>
            <button className="icon-button" disabled={result.page >= result.totalPages} onClick={() => setPage((current) => current + 1)} aria-label="Next page"><ChevronRight /></button>
          </div>
        </footer>
      </section>
      {editCandidate && (
        <Modal
          onClose={() => { if (!savingEdit) setEditCandidate(null) }}
          labelledBy="edit-user-title"
          dialogClassName="confirm-dialog edit-dialog"
        >
          <div className="edit-dialog-heading">
            <span className="confirm-icon edit"><UserCog /></span>
            <button type="button" className="icon-button" disabled={savingEdit} onClick={() => setEditCandidate(null)} aria-label="Close edit user form"><X /></button>
          </div>
          <h2 id="edit-user-title">Edit user</h2>
          <p id="edit-user-description">Update the account details for <strong>{editCandidate.email}</strong>.</p>
          <form className="edit-user-form" onSubmit={submitEdit}>
            <label className="field" htmlFor="edit-display-name"><span className="field-label">Display name</span><input id="edit-display-name" required value={editName} onChange={(event) => setEditName(event.target.value)} placeholder="Full name" /></label>
            <label className="field" htmlFor="edit-email"><span className="field-label">Email address</span><input id="edit-email" required type="email" autoComplete="email" value={editEmail} onChange={(event) => setEditEmail(event.target.value)} placeholder="name@example.com" /></label>
            <label className="field" htmlFor="edit-role"><span className="field-label">Role</span><select id="edit-role" value={editRole} onChange={(event) => setEditRole(event.target.value as UserRole)}><option value="player">Player</option><option value="admin">Admin</option></select></label>
            <label className="field" htmlFor="edit-team-name"><span className="field-label">Preferred team name <em>(optional)</em></span><input id="edit-team-name" value={editTeamName} onChange={(event) => setEditTeamName(event.target.value)} placeholder="e.g. The Galácticos" /></label>
            <label className="field" htmlFor="edit-avatar"><span className="field-label">Avatar URL <em>(optional)</em></span><input id="edit-avatar" type="url" inputMode="url" value={editAvatar} onChange={(event) => setEditAvatar(event.target.value)} placeholder="https://…" /></label>
            <div className="confirm-actions">
              <button type="button" className="secondary-button" disabled={savingEdit} onClick={() => setEditCandidate(null)}>Cancel</button>
              <button type="submit" className="primary-button compact" disabled={savingEdit}>{savingEdit ? <><RefreshCw className="spin" /> Saving…</> : <>Save changes</>}</button>
            </div>
          </form>
        </Modal>
      )}
      {deactivateCandidate && (
        <Modal onClose={() => setDeactivateCandidate(null)} labelledBy="deactivate-title">
          <span className="confirm-icon"><UserX /></span>
          <h2 id="deactivate-title">Deactivate {deactivateCandidate.displayName}?</h2>
          <p>
            <strong>{deactivateCandidate.displayName}</strong> ({deactivateCandidate.email}) will no longer be able
            to sign in or join drafts. You can reactivate the account at any time.
          </p>
          <div className="confirm-actions">
            <button type="button" className="secondary-button" onClick={() => setDeactivateCandidate(null)}>Cancel</button>
            <button type="button" className="danger-button confirm-delete" onClick={() => void confirmDeactivate()}>
              <UserX /> Deactivate account
            </button>
          </div>
        </Modal>
      )}
    </div>
  )
}
