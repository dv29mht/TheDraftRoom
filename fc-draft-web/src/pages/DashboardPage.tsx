import { ArrowRight, Clock3, Database, Megaphone, Radio, ShieldCheck, Sparkles, Trophy, UserRoundCog, UsersRound } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'

export function DashboardPage() {
  const user = useAuthStore((state) => state.user)
  return (
    <div className="page dashboard-page">
      <section className="hero-card">
        <div className="hero-glow" />
        <div className="hero-content">
          <span className="status-badge"><span /> Ready to host, {user?.displayName.split(' ')[0]}</span>
          <h1>Spin the order.<br/><em>Protect your difference maker.</em></h1>
          <p>Create a live 1v1 or 2v2 room, seed the field and bring everyone into the same draft.</p>
          <Link className="light-button" to="/drafts/new">Set up a new lobby <ArrowRight /></Link>
        </div>
        <div className="formation-art" aria-hidden="true">
          <span className="pitch-ring" />
          <span className="player-node striker">ST<small>75+</small></span>
          <span className="player-node left">LW<small>NEXT</small></span>
          <span className="player-node right">RW<small>THIRD</small></span>
        </div>
      </section>

      <section className="stat-grid">
        <article><span className="stat-icon primary"><Trophy /></span><div><strong>0</strong><small>Drafts completed</small></div><span className="trend">New season</span></article>
        <article><span className="stat-icon accent"><UsersRound /></span><div><strong>16</strong><small>Max 2v2 lobby</small></div><span className="trend">8 teams</span></article>
        <article><span className="stat-icon gold"><Clock3 /></span><div><strong>120s</strong><small>Each team pick</small></div><span className="trend">Live clock</span></article>
      </section>

      <section className="content-grid">
        <article className="panel empty-panel"><div className="panel-heading"><div><span className="eyebrow">Active room</span><h2>No draft in progress</h2></div><Radio /></div><div className="empty-orb"><Sparkles /></div><p>When a host starts the wheel, live picks and the 120-second clock will appear here.</p><Link to="/drafts">View draft hub <ArrowRight /></Link></article>
        <article className="panel rules-panel"><div className="panel-heading"><div><span className="eyebrow">MVP rules</span><h2>Built for Kick Off</h2></div><ShieldCheck /></div><ul><li><span>01</span>Men's base players only</li><li><span>02</span>Overall rating 75+</li><li><span>03</span>Alternate positions count</li><li><span>04</span>Roles and PlayStyles visible</li></ul></article>
      </section>

      {user?.role === 'admin' && (
        <section className="admin-home-section" aria-labelledby="admin-tools-heading">
          <div className="section-heading">
            <div><span className="eyebrow">Administration</span><h2 id="admin-tools-heading">Manage the same draft room</h2></div>
            <p>Your admin permissions add management tools to this account—there is no separate console or profile.</p>
          </div>
          <div className="admin-action-grid">
            <Link className="panel" to="/admin/users"><UserRoundCog /><div><h3>Manage users</h3><p>Create accounts and control access.</p></div><ArrowRight /></Link>
            <Link className="panel" to="/admin/player-data"><Database /><div><h3>Player dataset</h3><p>Prepare and validate the FC 26 data.</p></div><ArrowRight /></Link>
            <Link className="panel" to="/admin/communications"><Megaphone /><div><h3>Communications</h3><p>Send announcements to your players.</p></div><ArrowRight /></Link>
          </div>
        </section>
      )}
    </div>
  )
}
