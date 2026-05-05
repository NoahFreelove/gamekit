/* GameKit Admin — Audit log: two-column human + structured diff */

function Audit() {
  const [openIds, setOpenIds] = useState(new Set(['ev_91823']));
  const [filterAction, setFilterAction] = useState(null);
  const [filterActor, setFilterActor] = useState(null);

  const toggle = (id) => setOpenIds(s => { const n = new Set(s); n.has(id) ? n.delete(id) : n.add(id); return n; });

  let rows = GK.AUDIT;
  if (filterAction) rows = rows.filter(e => e.namespace === filterAction);
  if (filterActor) rows = rows.filter(e => e.actor === filterActor);

  return (
    <>
      <div className="page-head">
        <div>
          <div className="crumbs"><span>GameKit</span><span className="sep">/</span><span>Audit log</span></div>
          <h1>Audit log</h1>
          <div className="sub">Append-only · {rows.length.toLocaleString()} of 247,109 events · range: last 24h</div>
        </div>
        <div className="actions">
          <button className="btn">{I.download}Export NDJSON</button>
        </div>
      </div>

      <div className="card">
        <div className="card-head" style={{padding: '8px 12px'}}>
          <div className="row" style={{gap: 8, flexWrap:'wrap'}}>
            <div className="input-affix" style={{width: 280}}>
              {I.search}
              <input className="input" placeholder="Search reason or target…" />
            </div>
            <span className="dim" style={{fontSize:12}}>SAVED:</span>
            <span className="f-chip saved"><span className="key">namespace:</span><span className="val">players</span></span>
            <span className="f-chip saved"><span className="key">action:</span><span className="val">ban,unban</span></span>
            <span className="dim" style={{fontSize:12, marginLeft: 8}}>RECENT:</span>
            <span className="f-chip recent"><span className="key">actor:</span><span className="val">maria.alvarez</span></span>
            <span className="f-chip recent"><span className="key">range:</span><span className="val">last 24h</span></span>
          </div>
          <button className="btn btn-sm">{I.plus}Save current</button>
        </div>

        <div className="chip-rail">
          <span className="label">ACTIVE:</span>
          {['players','matchmaking','rankings','admins','auth'].map(ns => (
            <span key={ns} className={`f-chip${filterAction === ns ? ' saved' : ''}`} onClick={() => setFilterAction(filterAction === ns ? null : ns)}>
              <span className="key">namespace:</span><span className="val">{ns}</span>
            </span>
          ))}
          <span className="dim" style={{margin: '0 8px'}}>·</span>
          {['maria.alvarez','james.chen','priya.kapoor','system'].map(a => (
            <span key={a} className={`f-chip${filterActor === a ? ' saved' : ''}`} onClick={() => setFilterActor(filterActor === a ? null : a)}>
              <span className="key">actor:</span><span className="val">{a}</span>
            </span>
          ))}
        </div>

        <div className="card-body flush">
          {rows.map(ev => <AuditRow key={ev.id} ev={ev} open={openIds.has(ev.id)} onToggle={() => toggle(ev.id)} />)}
        </div>
        <div style={{padding: 12, textAlign:'center', borderTop: '1px solid var(--border)'}}>
          <button className="btn btn-sm">Load 50 more</button>
        </div>
      </div>
    </>
  );
}

function AuditRow({ ev, open, onToggle }) {
  const [verb, actor, action, target, extra] = ev.sentence;
  const glyphMap = { ban: 'ban', unban: 'unban', 'gdpr.delete': 'delete', 'admin.create': 'create', 'rank.adjust': 'rank', login: 'login', 'pool.scale': '' };
  const iconMap = { ban: I.ban, unban: I.unban, 'gdpr.delete': I.trash, 'admin.create': I.plus, 'rank.adjust': I.rank, login: I.shield, 'pool.scale': I.refresh };
  return (
    <div className={`audit-row${open ? ' expanded' : ''}`}>
      <div className="audit-left" onClick={onToggle} style={{cursor:'pointer'}}>
        <span className={`glyph ${glyphMap[verb] || ''}`}>{iconMap[verb] || I.info}</span>
        <div>
          <div className="sentence">
            <b>{actor}</b> {action} <b>{target}</b>{extra ? <> <span className="muted">— {extra}</span></> : null}
            <Chip kind="ghost" dot={false} className="role" style={{marginLeft: 8, color:'var(--fg-3)', borderColor:'var(--border)'}}>{ev.namespace}</Chip>
          </div>
          {ev.reason && <div className="reason">“{ev.reason}”</div>}
        </div>
        <span className="when" title={GK.fmtAbs(ev.when)}>{GK.fmtRel(ev.when)}</span>
      </div>
      <div className="audit-right">
        <span className="dim" style={{gridColumn:'1 / 2'}}>field</span>
        <span className="dim">before</span>
        <span className="dim">after</span>
        {ev.diff.map((d, i) => (
          <React.Fragment key={i}>
            <span className="field">{d.field}</span>
            <span>{d.before === null ? <span className="empty">∅</span> : <span className="before">{String(d.before)}</span>}</span>
            <span><span className="after">{d.after === null ? '∅' : String(d.after)}</span></span>
          </React.Fragment>
        ))}
        <div style={{gridColumn:'1 / 4', marginTop: 6, display:'flex', gap: 8, fontFamily:'var(--font-sans)'}}>
          <span className="tag-id">id: {ev.id}</span>
          <span className="dim">·</span>
          <a className="muted" style={{fontSize:11, fontFamily:'var(--font-sans)', cursor:'pointer'}}>view raw json →</a>
        </div>
      </div>
    </div>
  );
}

window.Audit = Audit;
