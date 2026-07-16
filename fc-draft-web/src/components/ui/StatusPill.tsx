import { draftStatusLabel } from '../../utils/draftStatus'

/** Draft status pill: colour + always a readable label, never colour alone. */
export function StatusPill({ status }: { status: string }) {
  return <span className={`status-pill status-${status.toLowerCase()}`}>{draftStatusLabel(status)}</span>
}
