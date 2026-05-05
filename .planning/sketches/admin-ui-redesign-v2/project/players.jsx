/* GameKit Admin — Players: master-detail layout */

function Players({ openBan, openUnban, openGDPR }) {
  const [query, setQuery] = useState('');
  const [activeId, setActiveId] = useState(GK.ACTIVE_PLAYER.id);
  const [filterBanned, setFilterBanned] = useState(false);
  const [filterProvider, setFilterProvider] = useState(null);

  const filtered = useMemo(() => {
    let arr = GK.PLAYERS;
    if (query) {
      const q = query.toLowerCase();
      arr = arr.filter(p => p.display.toLowerCase().includes(q) || p.id.includes(q) || p.extId.toLowerCase().includes(q));
    }
    if (filterBanned) arr = arr.filter(p => p.banned);
    if (filterProvider) arr = arr.filter(p => p.provider === filterProvider);
    return arr;
  }, [query, filterBanned, filterProvider]);

  const active = filtered.find(p => p.id === activeId) || filtered[0] || GK.PLAYERS[0];

  return (
    <>
      <div className="page-head">
        <div>
          <div className="crumbs"><span>GameKit</span><span className="sep">/</span><span>Players</span></div>
          <h1>Players</h1>
          <div className="sub">{filtered.length.toLocaleString()} of 482,184 indexed · keyset paginated</div>
        </div>
        <div className="actions">
          <button className="btn">{I.download}Export CSV</button>
        </div>
      </div>

      <div className="master-detail">
        <div className="master">
          <div className="master-head">
            <div className="input-affix">
              {I.search}
              <input className="input mono" placeholder="UUID, name, or steam:76561…" value={query} onChange={e => setQuery(e.target.value)}/>
              <Kbd>⌘K</Kbd>
              <span style={{padding: '0 8px'}}/>
            </div>
            <div className="row" style={{marginTop: 8, fontSize: 12, gap: 6, flexWrap: 'wrap'}}>
              <span className={`f-chip${filterBanned ? ' saved' : ''}`} onClick={() => setFilterBanned(v => !v)}>
                <span className="key">status:</span><span className="val">banned</span>
              </span>
              {['steam','epic','discord'].map(p => (
                <span key={p} className={`f-chip${filterProvider === p ? ' saved' : ''}`} onClick={() => setFilterProvider(filterProvider === p ? null : p)}>
                  <span className="key">provider:</span><span className="val">{p}</span>
                </span>
              ))}
              <span className="f-chip recent">
                <span className="key">recent:</span><span className="val">last seen 24h</span>
              </span>
            </div>
          </div>

          <div className="master-list">
            {filtered.map(p => (
              <div key={p.id} className={`master-row${p.id === active.id ? ' active' : ''}${p.banned ? ' banned' : ''}`} onClick={() => setActiveId(p.id)}>
                <Avatar name={p.display} />
                <div>
                  <div className="name">{p.display}</div>
                  <div className="meta">
                    <span>{p.provider}:{p.extId.slice(0, 10)}…</span>
                  </div>
                </div>
                <div className="col" style={{alignItems: 'flex-end', gap: 2}}>
                  {p.banned ? <Chip kind="banned">banned</Chip> : <span className="muted" style={{fontSize:12}}>{GK.fmtRel(p.lastSeen)}</span>}
                  <span className="dim mono" style={{fontSize:11}}>{p.country}</span>
                </div>
              </div>
            ))}
            <div style={{padding: 12, textAlign: 'center'}}>
              <button className="btn btn-sm">Load 50 more</button>
            </div>
          </div>
        </div>

        <div>
          <PlayerDetail player={active} openBan={openBan} openUnban={openUnban} openGDPR={openGDPR} />
        </div>
      </div>
    </>
  );
}

function PlayerDetail({ player, openBan, openUnban, openGDPR }) {
  const [tab, setTab] = useState('identities');

  return (
    <>
      {player.banned && (
        <div className="ban-banner" role="alert">
          <span className="icon">{I.warn}</span>
          <div>
            <div className="title">This player is currently banned</div>
            <div className="meta">
              <span>“{player.banReason}”</span>
              <span className="sep">·</span>
              <span>by <b>{player.bannedBy}</b></span>
              <span className="sep">·</span>
              <span>{GK.fmtRel(player.bannedAt)} ({GK.fmtAbs(player.bannedAt)})</span>
            </div>
          </div>
          <button className="btn" onClick={() => openUnban(player)}>{I.unban}Unban…</button>
        </div>
      )}

      <div className="player-head">
        <Avatar name={player.display} size="lg" />
        <div>
          <div className="ph-name">
            {player.display}
            {player.banned ? <Chip kind="banned">banned</Chip> : <Chip kind="healthy">active</Chip>}
            <Chip kind="accent" dot={false}>{player.tier}</Chip>
          </div>
          <div className="ph-meta">
            <CopyId value={player.id} />
            <span className="sep">·</span>
            <span><b>{player.provider}</b>:{player.extId}</span>
            <span className="sep">·</span>
            <span>{player.country}</span>
            <span className="sep">·</span>
            <span>joined {player.joined}</span>
            <span className="sep">·</span>
            <span>last seen {player.banned ? '—' : GK.fmtRel(player.lastSeen)}</span>
          </div>
        </div>
        <div className="col" style={{alignItems:'flex-end', gap: 8}}>
          <div className="row">
            {player.banned
              ? <button className="btn btn-primary" onClick={() => openUnban(player)}>{I.unban}Unban</button>
              : <button className="btn btn-danger" onClick={() => openBan(player)}>{I.ban}Ban…</button>
            }
            <button className="btn">{I.external}View matches</button>
          </div>
          <button className="btn btn-ghost" style={{color: 'var(--red)', fontSize:12}} onClick={() => openGDPR(player)}>
            {I.trash}GDPR delete… <span className="dim" style={{marginLeft: 6, fontSize: 11}}>superadmin</span>
          </button>
        </div>
      </div>

      <div className="tabs" role="tablist">
        {[
          { id:'identities', label:'Identities', count: GK.PLAYER_IDENTITIES.length },
          { id:'creds',      label:'Credentials', count: GK.PLAYER_CREDS.length },
          { id:'matches',    label:'Match history', count: player.matches },
          { id:'rank',       label:'Rank', count: null },
          { id:'audit',      label:'Audit', count: 14 },
        ].map(t => (
          <div key={t.id} className={`tab${tab === t.id ? ' active' : ''}`} onClick={() => setTab(t.id)} role="tab">
            {t.label}{t.count != null ? <span className="count">{t.count.toLocaleString()}</span> : null}
          </div>
        ))}
      </div>

      {tab === 'identities' && <IdentitiesTab />}
      {tab === 'creds' && <CredsTab />}
      {tab === 'matches' && <MatchesTab />}
      {tab === 'rank' && <RankTab player={player} />}
      {tab === 'audit' && <PlayerAuditTab player={player} />}
    </>
  );
}

function IdentitiesTab() {
  return (
    <div className="card">
      <div className="card-head">
        <h2>Linked identities</h2>
        <button className="btn btn-sm">{I.plus}Link identity</button>
      </div>
      <div className="card-body flush">
        <table className="t">
          <thead><tr><th style={{width:32}}></th><th>Provider</th><th>External ID</th><th>Verified</th><th>Linked</th><th></th></tr></thead>
          <tbody>
            {GK.PLAYER_IDENTITIES.map(id => (
              <tr key={id.provider}>
                <td><ProviderIcon p={id.provider} /></td>
                <td><b>{id.provider}</b>{id.primary && <Chip kind="accent" dot={false} className="role" style={{marginLeft:8}}>PRIMARY</Chip>}</td>
                <td className="mono"><CopyId value={id.extId} max={16} /></td>
                <td>{id.verified ? <Chip kind="healthy">verified</Chip> : <Chip kind="degraded">unverified</Chip>}</td>
                <td className="muted">{GK.fmtRel(id.linkedAt)}</td>
                <td className="actions">
                  <div className="row-actions"><button className="btn btn-sm btn-ghost">Unlink</button></div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function CredsTab() {
  return (
    <div className="card">
      <div className="card-head">
        <h2>Credentials</h2>
        <button className="btn btn-sm" disabled style={{opacity:0.5}}>Reset password (player must initiate)</button>
      </div>
      <div className="card-body flush">
        <table className="t">
          <thead><tr><th>Kind</th><th>Detail</th><th>Added</th><th>Last used</th></tr></thead>
          <tbody>
            {GK.PLAYER_CREDS.map(c => (
              <tr key={c.kind}>
                <td><b>{c.kind}</b></td>
                <td className="muted">{c.label || c.strength || '—'}</td>
                <td className="muted">{GK.fmtRel(c.addedAt)}</td>
                <td className="muted">{c.lastUsed ? GK.fmtRel(c.lastUsed) : 'never'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function MatchesTab() {
  return (
    <div className="card">
      <div className="card-head">
        <h2>Recent matches</h2>
        <span className="muted" style={{fontSize:12}}>showing 6 of 612</span>
      </div>
      <div className="card-body flush">
        <table className="t">
          <thead><tr>
            <th>Match ID</th><th>Mode</th><th>Pool</th><th>Result</th>
            <th className="num">Score</th><th className="num">MMR Δ</th><th className="num">Duration</th><th>Ended</th>
          </tr></thead>
          <tbody>
            {GK.PLAYER_MATCHES.map(m => (
              <tr key={m.id}>
                <td className="mono"><CopyId value={m.id} max={12} /></td>
                <td>{m.mode}</td>
                <td className="mono muted">{m.pool}</td>
                <td>{m.result === 'Win' ? <Chip kind="healthy">Win</Chip> : <Chip kind="banned" dot={false}>Loss</Chip>}</td>
                <td className="num mono">{m.score}</td>
                <td className="num" style={{color: m.mmrΔ > 0 ? 'var(--green)' : m.mmrΔ < 0 ? 'var(--red)' : 'var(--fg-2)', fontWeight: 600}}>
                  {m.mmrΔ > 0 ? '+' : ''}{m.mmrΔ}
                </td>
                <td className="num muted">{m.dur}</td>
                <td className="muted">{GK.fmtRel(m.ended)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div style={{padding: 12, textAlign:'center', borderTop: '1px solid var(--border)'}}>
          <button className="btn btn-sm">Load 50 more</button>
        </div>
      </div>
    </div>
  );
}

function RankTab({ player }) {
  return (
    <div className="grid-12">
      <div className="card span-6">
        <div className="card-head"><h2>Current rank</h2></div>
        <div className="card-body">
          <dl className="kv">
            <dt>Tier</dt><dd><Chip kind="accent" dot={false}>{player.tier}</Chip></dd>
            <dt>MMR</dt><dd className="mono"><b>{player.mmr}</b> <span className="muted">(σ 84)</span></dd>
            <dt>Global rank</dt><dd className="mono">#{player.rank.toLocaleString()}</dd>
            <dt>Decay</dt><dd className="muted">none · last ranked match {GK.fmtRel(GK.PLAYER_MATCHES[0].ended)}</dd>
            <dt>Placements</dt><dd className="muted">10/10 complete</dd>
          </dl>
        </div>
      </div>
      <div className="card span-6">
        <div className="card-head"><h2>Manual adjustment <span className="muted" style={{fontSize:11, fontWeight:400}}>· superadmin only</span></h2></div>
        <div className="card-body col" style={{gap: 12}}>
          <label className="field">New MMR <input className="input mono" defaultValue={player.mmr}/></label>
          <label className="field">Reason (required, audited)<textarea className="textarea" rows="3" placeholder="e.g. smurf detection, account misranked after 4 placements…"/></label>
          <div className="row" style={{justifyContent:'flex-end', gap: 8}}>
            <button className="btn">Cancel</button>
            <button className="btn btn-primary">Apply adjustment</button>
          </div>
        </div>
      </div>
    </div>
  );
}

function PlayerAuditTab({ player }) {
  const events = GK.AUDIT.filter(e => e.target === player.display || e.targetId === player.id).slice(0, 4);
  return (
    <div className="card">
      <div className="card-head"><h2>Events on this player</h2></div>
      <div className="card-body flush">
        {events.length ? events.map(ev => <FeedItem key={ev.id} ev={ev}/>)
          : <div className="empty"><div className="glyph">{I.log}</div><h3>Nothing in the audit log yet</h3><p>Actions taken on this player by admins or the system will appear here.</p></div>}
      </div>
    </div>
  );
}

window.Players = Players;
