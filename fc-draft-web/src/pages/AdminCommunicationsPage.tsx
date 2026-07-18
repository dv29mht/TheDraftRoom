import { Megaphone, RefreshCw, Send, Users } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { announcementsApi, draftsApi, getApiError, isApiConflict } from '../services/api'
import type { Announcement, AnnouncementAudience, AnnouncementPreviewResponse } from '../types/admin'
import type { DraftSummary } from '../types/draft'
import { ErrorBanner, LoadingState, SuccessBanner } from '../components/ui/Feedback'
import { Modal } from '../components/ui/Modal'
import { useAnnouncer } from '../hooks/useAnnouncer'

const SUBJECT_MAX = 160
const BODY_MAX = 2000

/**
 * The admin Communications module (PR-21, §9.8): compose an announcement, choose its audience,
 * PREVIEW it (subject, sender, resolved audience count, opt-out split), and send only after an
 * explicit confirmation. Sends go through the durable outbox — throttled, campaign-stamped, and
 * respectful of the §9.9 opt-out — and past campaigns list live queued/sent/failed tallies.
 */
export function AdminCommunicationsPage() {
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [audience, setAudience] = useState<AnnouncementAudience>('all')
  const [draftId, setDraftId] = useState('')
  const [drafts, setDrafts] = useState<DraftSummary[]>([])
  const [preview, setPreview] = useState<AnnouncementPreviewResponse | null>(null)
  const [previewing, setPreviewing] = useState(false)
  const [sending, setSending] = useState(false)
  const [announcements, setAnnouncements] = useState<Announcement[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const { announce, announcer } = useAnnouncer()

  const loadHistory = useCallback(async () => {
    setAnnouncements(await announcementsApi.list())
  }, [])

  useEffect(() => {
    let active = true
    Promise.all([draftsApi.list(), announcementsApi.list()])
      .then(([allDrafts, sent]) => {
        if (!active) return
        setDrafts(allDrafts)
        setAnnouncements(sent)
      })
      .catch((requestError) => { if (active) setError(getApiError(requestError)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  const composeReady = subject.trim().length > 0 && body.trim().length > 0
    && (audience === 'all' || draftId !== '')

  const openPreview = async () => {
    setError('')
    setNotice('')
    setPreviewing(true)
    try {
      const next = await announcementsApi.preview({
        subject: subject.trim(),
        body: body.trim(),
        audience,
        draftId: audience === 'draft' ? draftId : null,
      })
      setPreview(next)
      announce(`Preview ready: ${next.preview.recipientCount} recipients.`)
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setPreviewing(false)
    }
  }

  const confirmSend = async () => {
    if (!preview) return
    setSending(true)
    setError('')
    try {
      const sent = await announcementsApi.send({
        subject: preview.preview.subject,
        body: preview.preview.body,
        audience: preview.preview.audience,
        draftId: preview.preview.draftId,
        confirmedRecipientCount: preview.preview.recipientCount,
      })
      setPreview(null)
      setSubject('')
      setBody('')
      setAudience('all')
      setDraftId('')
      setNotice(`Announcement sent to ${sent.audienceLabel.toLowerCase()} — ${sent.recipientCount} in-app notices, ${sent.emailCount} emails queued.`)
      announce('Announcement sent.')
      await loadHistory()
    } catch (requestError) {
      setPreview(null)
      setError(isApiConflict(requestError)
        ? 'The audience changed since your preview — someone joined or left. Preview again to review the new audience before sending.'
        : getApiError(requestError))
    } finally {
      setSending(false)
    }
  }

  return (
    <div className="page communications-page">
      <h1 className="sr-only">Communications</h1>
      {announcer}

      {error && <ErrorBanner>{error}</ErrorBanner>}
      {notice && <SuccessBanner onDismiss={() => setNotice('')}>{notice}</SuccessBanner>}

      <div className="comms-grid">
      <div className="comms-col">
      <section className="panel admin-module-panel" aria-labelledby="compose-announcement-title">
        <div className="directory-toolbar">
          <div><span className="eyebrow">Brevo email centre</span><h2 id="compose-announcement-title">Send an announcement</h2></div>
          {/* §12.4: templated announcements are deferred — visible and disabled, never absent. */}
          <button className="secondary-button" type="button" disabled title="Coming soon">
            Use a Brevo template · Coming soon
          </button>
        </div>
        <form className="announcement-form" onSubmit={(event) => { event.preventDefault(); void openPreview() }}>
          <label className="field" htmlFor="announcement-subject">
            <span className="field-label">Subject</span>
            <input
              id="announcement-subject"
              required
              maxLength={SUBJECT_MAX}
              value={subject}
              onChange={(event) => setSubject(event.target.value)}
              placeholder="e.g. FC 26 dataset refreshed"
            />
          </label>
          <label className="field" htmlFor="announcement-body">
            <span className="field-label">Message <em>({body.length}/{BODY_MAX})</em></span>
            <textarea
              id="announcement-body"
              required
              maxLength={BODY_MAX}
              rows={5}
              value={body}
              onChange={(event) => setBody(event.target.value)}
              placeholder="What every player should know…"
            />
          </label>
          <fieldset className="field audience-fieldset">
            <legend className="field-label">Audience</legend>
            <label className="radio-option">
              <input
                type="radio"
                name="announcement-audience"
                checked={audience === 'all'}
                onChange={() => setAudience('all')}
              />
              <span>All active players</span>
            </label>
            <label className="radio-option">
              <input
                type="radio"
                name="announcement-audience"
                checked={audience === 'draft'}
                onChange={() => setAudience('draft')}
              />
              <span>Participants of a draft</span>
            </label>
            {audience === 'draft' && (
              <label className="field" htmlFor="announcement-draft">
                <span className="field-label">Draft</span>
                <select
                  id="announcement-draft"
                  required
                  value={draftId}
                  onChange={(event) => setDraftId(event.target.value)}
                >
                  <option value="" disabled>Choose a draft…</option>
                  {drafts.map((draft) => (
                    <option key={draft.id} value={draft.id}>{draft.name} · {draft.code}</option>
                  ))}
                </select>
              </label>
            )}
          </fieldset>
          <p className="field-hint">
            Announcements are optional emails: players who opted out still get the in-app notice, never the email.
          </p>
          <div className="form-actions">
            <button className="primary-button compact" type="submit" disabled={!composeReady || previewing}>
              {previewing ? <RefreshCw className="spin" /> : <Megaphone />} {previewing ? 'Resolving audience…' : 'Preview announcement'}
            </button>
          </div>
        </form>
      </section>
      </div>

      <div className="comms-col">
      <section className="panel admin-module-panel" aria-labelledby="sent-announcements-title">
        <div className="directory-toolbar">
          <div><span className="eyebrow">Campaigns</span><h2 id="sent-announcements-title">Sent announcements</h2></div>
        </div>
        {loading ? <LoadingState>Loading announcements…</LoadingState> : announcements.length ? (
          <ul className="announcement-history">
            {announcements.map((item) => (
              <li key={item.id} className="announcement-history-item">
                <div className="announcement-history-heading">
                  <strong>{item.subject}</strong>
                  <time dateTime={item.requestedAt}>{new Date(item.requestedAt).toLocaleString()}</time>
                </div>
                <p className="announcement-history-meta">
                  <Users aria-hidden="true" /> {item.audienceLabel} · {item.recipientCount} recipients ·
                  sent by {item.requestedByEmail}
                  {item.optedOutCount > 0 && ` · ${item.optedOutCount} opted out of email`}
                </p>
                <p className="delivery-tallies">
                  {item.emailsPending > 0 && <span className="outbox-status outbox-pending">{item.emailsPending} queued</span>}
                  <span className="outbox-status outbox-sent">{item.emailsSent} sent</span>
                  {item.emailsFailed > 0 && <span className="outbox-status outbox-failed">{item.emailsFailed} failed</span>}
                  {item.emailCount === 0 && <span className="outbox-status outbox-pending">no emails (in-app only)</span>}
                </p>
              </li>
            ))}
          </ul>
        ) : <div className="empty-list"><Megaphone /><strong>No announcements yet</strong><span>Campaigns you send appear here with live delivery status.</span></div>}
      </section>
      </div>
      </div>

      {preview && (
        <Modal onClose={() => !sending && setPreview(null)} labelledBy="announcement-preview-title" dialogClassName="confirm-dialog announcement-preview-dialog">
          <span className="confirm-icon"><Send /></span>
          <h2 id="announcement-preview-title">Review before sending</h2>
          <dl className="announcement-preview-meta">
            <div><dt>From</dt><dd>{preview.senderName}{preview.senderEmail ? ` <${preview.senderEmail}>` : ''}</dd></div>
            <div><dt>Audience</dt><dd>{preview.preview.audienceLabel}</dd></div>
            <div><dt>Recipients</dt><dd>{preview.preview.recipientCount} in-app · {preview.preview.emailRecipientCount} by email{preview.preview.optedOutCount > 0 ? ` · ${preview.preview.optedOutCount} opted out` : ''}</dd></div>
            <div><dt>Subject</dt><dd>{preview.preview.subject}</dd></div>
          </dl>
          <p className="announcement-preview-body">{preview.preview.body}</p>
          {!preview.emailConfigured && (
            <p className="field-hint" role="note">
              Email sending is not configured in this environment — recipients will still get the in-app notice.
            </p>
          )}
          {preview.preview.recipientCount === 0 && (
            <p className="field-hint" role="note">This audience has no active recipients, so there is nothing to send.</p>
          )}
          <div className="confirm-actions">
            <button className="secondary-button" type="button" disabled={sending} onClick={() => setPreview(null)}>
              Back to editing
            </button>
            <button
              className="primary-button"
              type="button"
              disabled={sending || preview.preview.recipientCount === 0}
              onClick={() => void confirmSend()}
            >
              {sending ? <RefreshCw className="spin" /> : <Send />} {sending ? 'Sending…' : `Confirm & send to ${preview.preview.recipientCount}`}
            </button>
          </div>
        </Modal>
      )}
    </div>
  )
}
