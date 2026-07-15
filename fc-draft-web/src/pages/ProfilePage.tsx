import { KeyRound, Mail, ShieldCheck, UserRound } from 'lucide-react'
import { useAuthStore } from '../stores/authStore'

export function ProfilePage() {
  const user = useAuthStore((state) => state.user)
  return (
    <div className="page">
      <section className="profile-grid">
        <article className="panel profile-card"><span className="profile-avatar">{user?.displayName.slice(0, 2).toUpperCase()}</span><h2>{user?.displayName}</h2><p>{user?.role}</p><span className="verified"><ShieldCheck /> Active account</span></article>
        <article className="panel details-card"><div><UserRound /><span><small>Display name</small><strong>{user?.displayName}</strong></span></div><div><Mail /><span><small>Email address</small><strong>{user?.email}</strong></span></div><div><KeyRound /><span><small>Password</small><strong>Changed during activation</strong></span></div></article>
      </section>
    </div>
  )
}
