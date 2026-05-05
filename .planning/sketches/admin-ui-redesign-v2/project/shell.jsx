/* GameKit Admin — Top nav + sidebar shell */

function TopNav({ onSidebar, onPalette, onShowDialog }) {
  return (
    <div className="topnav" role="banner">
      <div className="brand">
        <button className="btn btn-ghost btn-icon" onClick={onSidebar} aria-label="Toggle sidebar" style={{marginRight: -4}}>
          {I.collapse}
        </button>
        <span className="brand-mark">GK</span>
        <span className="brand-name">GameKit Admin</span>
      </div>

      <div className="topnav-search" onClick={onPalette} role="search">
        {I.search}
        <span style={{flex: 1}}>Search players, audit events, admins…</span>
        <Kbd>⌘</Kbd><Kbd>K</Kbd>
      </div>

      <span className="brand-env">v3.4.1 · prod-eu</span>

      <div className="topnav-actions">
        <button className="btn btn-ghost btn-icon" aria-label="Documentation" title="Open docs">{I.external}</button>
        <button className="user-chip" onClick={() => onShowDialog && onShowDialog('user')}>
          <Avatar name="Maria Alvarez" />
          <span>maria.alvarez</span>
          <Chip kind="accent" dot={false} className="role">SUPER</Chip>
        </button>
      </div>
    </div>
  );
}

const NAV = [
  { id: 'dashboard',   label: 'Dashboard',     icon: I.home,   route: '/admin' },
  { id: 'players',     label: 'Players',       icon: I.users,  route: '/admin/players' },
  { id: 'audit',       label: 'Audit log',     icon: I.log,    route: '/admin/audit', badge: 247 },
  { id: 'health',      label: 'Health',        icon: I.pulse,  route: '/admin/health', badgeChip: 'degraded' },
  { id: 'matchmaking', label: 'Matchmaking',   icon: I.match,  route: '/admin/matchmaking' },
  { id: 'rankings',    label: 'Rank adjust',   icon: I.rank,   route: '/admin/rankings/adjust' },
  { id: 'admins',      label: 'Admin accounts',icon: I.shield, route: '/admin/admins' },
];

function Sidebar({ active, setActive }) {
  return (
    <aside className="sidebar" aria-label="Primary">
      <div className="group-label">Operations</div>
      {NAV.slice(0, 5).map(n => (
        <button key={n.id} className={`nav-item${active === n.id ? ' active' : ''}`} onClick={() => setActive(n.id)}>
          {n.icon}
          <span className="label">{n.label}</span>
          {n.badge ? <span className="badge">{n.badge}</span> : null}
          {n.badgeChip ? <Chip kind={n.badgeChip} dot>{n.badgeChip === 'degraded' ? '!' : ''}</Chip> : null}
        </button>
      ))}

      <div className="group-label">Superadmin</div>
      {NAV.slice(5).map(n => (
        <button key={n.id} className={`nav-item${active === n.id ? ' active' : ''}`} onClick={() => setActive(n.id)}>
          {n.icon}
          <span className="label">{n.label}</span>
        </button>
      ))}

      <div className="sidebar-footer">
        <span className="live-dot" aria-hidden/>
        <span className="sidebar-footer-text">All systems nominal · synced 4s ago</span>
      </div>
    </aside>
  );
}

window.TopNav = TopNav;
window.Sidebar = Sidebar;
window.NAV = NAV;
