/* GameKit Admin — Cmd-K command palette */

function Palette({ onClose, go, openBan, openGDPR, openCreateAdmin, openLogin }) {
  const [q, setQ] = useState('');
  const [active, setActive] = useState(0);

  const items = useMemo(() => {
    const navItems = NAV.map(n => ({ kind: 'page', label: 'Go to ' + n.label, hint: n.route, icon: n.icon, run: () => go(n.id) }));
    const playerItems = GK.PLAYERS.slice(0, 8).map(p => ({
      kind: 'player', label: p.display, hint: `${p.provider}:${p.extId.slice(0,12)}…`,
      icon: <span className="icon" style={{display:'inline-flex'}}><Avatar name={p.display} /></span>,
      run: () => go('players')
    }));
    const actions = [
      { kind:'action', label:'Ban player…',         hint:'opens ban dialog', icon: I.ban,   run: () => openBan(GK.PLAYERS[0]) },
      { kind:'action', label:'GDPR delete player…', hint:'superadmin',       icon: I.trash, run: () => openGDPR(GK.PLAYERS[0]) },
      { kind:'action', label:'Create admin…',       hint:'superadmin',       icon: I.plus,  run: () => openCreateAdmin() },
      { kind:'action', label:'Show first-run / bootstrap screen', hint: '/admin/login', icon: I.shield, run: () => openLogin() },
      { kind:'action', label:'Toggle sidebar',      hint:'⌘\\',              icon: I.collapse, run: () => window.GK_toggleSidebar && window.GK_toggleSidebar() },
    ];
    const all = [...actions, ...navItems, ...playerItems];
    if (!q) return all;
    const lower = q.toLowerCase();
    return all.filter(it => it.label.toLowerCase().includes(lower) || (it.hint || '').toLowerCase().includes(lower));
  }, [q]);

  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') onClose();
      if (e.key === 'ArrowDown') { e.preventDefault(); setActive(a => Math.min(a + 1, items.length - 1)); }
      if (e.key === 'ArrowUp')   { e.preventDefault(); setActive(a => Math.max(a - 1, 0)); }
      if (e.key === 'Enter')     { e.preventDefault(); items[active]?.run?.(); onClose(); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [items, active, onClose]);

  // group items
  const grouped = useMemo(() => {
    const g = { action: [], page: [], player: [] };
    items.forEach((it, i) => g[it.kind].push({ ...it, idx: i }));
    return g;
  }, [items]);

  const sectionTitle = { action: 'Actions', page: 'Pages', player: 'Players' };

  return (
    <div className="palette-scrim" onClick={onClose}>
      <div className="palette" onClick={e => e.stopPropagation()}>
        <div className="palette-input">
          {I.search}
          <input autoFocus value={q} onChange={e => { setQ(e.target.value); setActive(0); }}
            placeholder="Type a command, page, or player name…"/>
          <Kbd>esc</Kbd>
        </div>
        <div className="palette-list">
          {['action','page','player'].map(k => grouped[k].length ? (
            <div key={k}>
              <div className="palette-section">{sectionTitle[k]}</div>
              {grouped[k].map(it => (
                <div key={it.idx} className={`palette-row${it.idx === active ? ' active' : ''}`}
                  onMouseEnter={() => setActive(it.idx)}
                  onClick={() => { it.run(); onClose(); }}>
                  <span className="icon" style={{display:'inline-flex'}}>{it.icon}</span>
                  <span>{it.label}</span>
                  <span className="hint">{it.hint}</span>
                </div>
              ))}
            </div>
          ) : null)}
          {items.length === 0 && (
            <div style={{padding: 24, textAlign:'center', color:'var(--fg-3)', fontSize: 13}}>No matches for “{q}”.</div>
          )}
        </div>
        <div className="palette-foot">
          <span><Kbd>↑</Kbd><Kbd>↓</Kbd> navigate</span>
          <span><Kbd>↵</Kbd> select</span>
          <span><Kbd>esc</Kbd> close</span>
          <span style={{marginLeft:'auto'}}>{items.length} result{items.length === 1 ? '' : 's'}</span>
        </div>
      </div>
    </div>
  );
}

window.Palette = Palette;
