import { ArrowRight, Construction, Plus } from 'lucide-react'
import { Link } from 'react-router-dom'

export function PlaceholderPage({ eyebrow, title, description, action }: { eyebrow: string; title: string; description: string; action?: { label: string; to: string } }) {
  return (
    <div className="page">
      <section className="panel placeholder-panel"><span className="empty-orb"><Construction /></span><span className="eyebrow">{eyebrow}</span><h2>{title}</h2><p>{description}</p>{action && <Link className="primary-button compact" to={action.to}><Plus />{action.label}</Link>}<Link to="/">Return home <ArrowRight /></Link></section>
    </div>
  )
}
