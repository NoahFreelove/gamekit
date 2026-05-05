/* GameKit Admin — Health, Matchmaking, Rankings, Admins, Login pages */

function Health() {
  return (
    <>
      <div className="page-head">
        <div>
          <div className="crumbs"><span>GameKit</span><span className="sep">/</span><span>Health</span></div>
          <h1>Service health</h1>
          <div className="sub">3 dependencies · auto-refresh 10s · synced from `IHealthCheckService`</div>
        </div>
        <div className="actions">
          <span className="row" style={{fontSize:12, color:'var(--fg-2)'}}><span className="live-dot"/> live</span>
          <button className="btn">{I.refresh}Refresh now</button>
        </div>
      </div>

      <div className="grid-12">
        <div className="span-4"><HealthTile name="Postgres" data={GK.HEALTH.postgres} sub="primary · pgbouncer · 14.10"/></div>
        <div className="span-4"><HealthTile name="Redis" data={GK.HEALTH.redis} sub="cluster · 7.2.4 · 3 nodes"/></div>
        <div className="span-4"><HealthTile name="Error rate" data={GK.HEALTH.errorRate} sub="rolling 5m · /admin & /api"/></div>

        <div className="card span-12">
          <div className="card-head"><h2>Recent health events</h2><span className="muted" style={{fontSize:12}}>last 6h</span></div>
          <div className="card-body flush">
            <table className="t">
              <thead><tr><th>Time</th><th>Component</th><th>Event</th><th>Detail</th><th>Resolved</th></tr></thead>
              <tbody>
                <tr><td className="muted">{GK.fmtRel(GK.ago(8))}</td><td><b>error rate</b></td><td><Chip kind="degraded">degraded</Chip></td><td className="muted">spike to 0.42% — exceptions in `/api/match/start`</td><td className="muted">—</td></tr>
                <tr><td className="muted">{GK.fmtRel(GK.ago(46))}</td><td><b>matchmaking</b></td><td><Chip kind="degraded">degraded</Chip></td><td className="muted">eu-west queue depth 268 (threshold 200)</td><td className="muted">—</td></tr>
                <tr><td className="muted">{GK.fmtRel(GK.ago(124))}</td><td><b>postgres</b></td><td><Chip kind="degraded">degraded</Chip></td><td className="muted">p99 latency 38ms (threshold 25ms)</td><td className="muted">{GK.fmtRel(GK.ago(118))}</td></tr>
                <tr><td className="muted">{GK.fmtRel(GK.ago(312))}</td><td><b>redis</b></td><td><Chip kind="down">down</Chip></td><td className="muted">primary failover to replica node-2</td><td className="muted">{GK.fmtRel(GK.ago(309))}</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </>
  );
}

function HealthTile({ name, data, sub }) {
  return (
    <div className="health-tile">
      <div className="top">
        <div>
          <div className="name">{name}</div>
          <div className="muted" style={{fontSize:11}}>{sub}</div>
        </div>
        <Chip kind={data.status}>{data.status}</Chip>
      </div>
      <div className="v">{data.value}<span className="unit">{data.unit}</span></div>
      <div className="strip" aria-label="last 30 minutes">
        {data.strip.map((s, i) => <span key={i} className={typeof s === 'string' ? s : ''}/>)}
      </div>
      <div className="footer"><span>30 min ago</span><span>now</span></div>
      <div className="muted" style={{fontSize:12, paddingTop: 4, borderTop:'1px solid var(--border)'}}>{data.detail}</div>
    </div>
  );
}

function Matchmaking() {
  return (
    <>
      <div className="page-head">
        <div>
          <div className="crumbs"><span>GameKit</span><span className="sep">/</span><span>Matchmaking</span></div>
          <h1>Matchmaking queues</h1>
          <div className="sub"><b>GameKit.Matchmaking</b> v3.4.0 · 6 pools registered · auto-refresh 10s</div>
        </div>
        <div className="actions">
          <span className="row" style={{fontSize:12, color:'var(--fg-2)'}}><span className="live-dot"/> live</span>
          <button className="btn">{I.refresh}Refresh now</button>
        </div>
      </div>

      <div className="grid-12">
        <div className="card span-12">
          <div className="card-head">
            <h2>Queue depth · all pools</h2>
            <div className="row"><span className="muted" style={{fontSize:12}}>total queued: <b style={{color:'var(--fg)'}}>470</b></span></div>
          </div>
          <div className="card-body flush">
            <table className="t">
              <thead><tr>
                <th>Pool</th><th className="num">Queued</th><th className="num">p50 wait</th><th className="num">p99 wait</th>
                <th className="num">Workers</th><th className="num">Matches/min</th><th>Status</th><th></th>
              </tr></thead>
              <tbody>
                {GK.QUEUES.map(q => (
                  <tr key={q.pool}>
                    <td className="mono"><b>{q.pool}</b></td>
                    <td className="num"><b>{q.depth}</b></td>
                    <td className="num muted">{q.wait}</td>
                    <td className="num muted">{Math.round(parseInt(q.wait) * 2.4)}s</td>
                    <td className="num muted">{q.workers}</td>
                    <td className="num muted">{Math.round(q.depth / 8)}</td>
                    <td><Chip kind={q.status}>{q.status}</Chip></td>
                    <td className="actions"><div className="row-actions"><button className="btn btn-sm">Inspect</button></div></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </>
  );
}

function Rankings() {
  return (
    <>
      <div className="page-head">
        <div>
          <div className="crumbs"><span>GameKit</span><span className="sep">/</span><span>Rankings</span><span className="sep">/</span><span>Adjust</span></div>
          <h1>Manual rank adjustment</h1>
          <div className="sub"><b>GameKit.Rankings</b> v3.4.0 · superadmin only · every adjustment is audited</div>
        </div>
      </div>

      <div className="grid-12">
        <div className="card span-7">
          <div className="card-head"><h2>New adjustment</h2></div>
          <div className="card-body col" style={{gap: 16}}>
            <label className="field">Player <span className="hint">— search by display name or UUID</span>
              <div className="input-affix">{I.search}<input className="input" placeholder="e.g. TacticalKettle"/></div>
            </label>
            <div className="row" style={{gap: 12}}>
              <label className="field" style={{flex:1}}>Field
                <select className="select"><option>MMR</option><option>Tier</option><option>Global rank</option></select>
              </label>
              <label className="field" style={{flex:1}}>New value <input className="input mono" placeholder="1480"/></label>
            </div>
            <label className="field">Reason <span className="hint">— required, 10–512 chars, written to audit log</span>
              <textarea className="textarea" rows="4" placeholder="Smurf detection — account misranked after 4 placements. Adjusting from 1102 to 1480 to match peer cluster."/></label>
            <div className="row" style={{justifyContent:'flex-end', gap: 8}}>
              <button className="btn">Cancel</button>
              <button className="btn btn-primary">Apply adjustment</button>
            </div>
          </div>
        </div>
        <div className="card span-5">
          <div className="card-head"><h2>Recent adjustments</h2><a className="muted" style={{fontSize:12}}>View in audit →</a></div>
          <div className="card-body flush">
            {GK.AUDIT.filter(e => e.namespace === 'rankings').map(ev => <FeedItem key={ev.id} ev={ev}/>)}
            <div style={{padding: 24, textAlign:'center'}} className="muted">
              <span style={{fontSize:12}}>1 adjustment in last 24h</span>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}

function Admins({ onCreate, onDelete }) {
  return (
    <>
      <div className="page-head">
        <div>
          <div className="crumbs"><span>GameKit</span><span className="sep">/</span><span>Admin accounts</span></div>
          <h1>Admin accounts</h1>
          <div className="sub">{GK.ADMINS.length} accounts · 2 superadmins · superadmin-only management</div>
        </div>
        <div className="actions">
          <button className="btn btn-primary" onClick={onCreate}>{I.plus}Create admin</button>
        </div>
      </div>

      <div className="card">
        <div className="card-body flush">
          <table className="t">
            <thead><tr>
              <th>Username</th><th>Role</th><th>Email</th><th>2FA</th><th>Last seen</th><th></th>
            </tr></thead>
            <tbody>
              {GK.ADMINS.map(a => (
                <tr key={a.id}>
                  <td><div className="row" style={{gap: 8}}><Avatar name={a.user}/><b>{a.user}</b></div></td>
                  <td>{a.role === 'superadmin' ? <Chip kind="accent" dot={false} className="role">SUPER</Chip> : <Chip kind="ghost" dot={false} className="role">ADMIN</Chip>}</td>
                  <td className="mono">{a.email}</td>
                  <td>{a.twoFA ? <Chip kind="healthy">enabled</Chip> : <Chip kind="degraded">missing</Chip>}</td>
                  <td className="muted">{GK.fmtRel(a.lastSeen)}</td>
                  <td className="actions">
                    <div className="row-actions">
                      <button className="btn btn-sm">Reset 2FA</button>
                      <button className="btn btn-sm btn-danger" onClick={() => onDelete(a)}>{I.trash}Delete</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
}

function LoginBootstrap() {
  return (
    <div style={{minHeight: '100vh', display:'grid', placeItems:'center', background:'var(--bg)', padding: 32}}>
      <div style={{width: 520}}>
        <div className="row" style={{gap:10, marginBottom: 24}}>
          <span className="brand-mark" style={{width: 32, height: 32, fontSize: 13, borderRadius: 6}}>GK</span>
          <span style={{fontWeight: 600, fontSize: 16}}>GameKit Admin</span>
          <span className="brand-env" style={{marginLeft:'auto'}}>v3.4.1 · prod-eu</span>
        </div>

        <div className="card">
          <div className="card-head" style={{borderColor:'var(--amber-border)', background:'var(--amber-bg)'}}>
            <h2 style={{color:'#78350F'}}><span style={{color:'var(--amber)', display:'inline-flex'}}>{I.warn}</span> First-run setup required</h2>
          </div>
          <div className="card-body col" style={{gap: 16}}>
            <p style={{margin:0, fontSize:13, color:'var(--fg-2)', lineHeight: 1.55}}>
              No admin accounts exist yet. For security, the first admin <b style={{color:'var(--fg)'}}>cannot</b> be created from the web UI — you must bootstrap it from the host machine using the GameKit CLI.
            </p>

            <div>
              <div style={{fontSize:11, color:'var(--fg-3)', textTransform:'uppercase', letterSpacing:'0.06em', marginBottom: 6}}>Run on the server</div>
              <pre style={{margin:0, padding:'12px 14px', background:'var(--fg)', color:'#E2E8F0', borderRadius: 6, fontFamily:'var(--font-mono)', fontSize:12, lineHeight: 1.7, overflowX:'auto'}}>
{`$ dotnet gamekit admin create \\
    --username maria.alvarez \\
    --email maria.alvarez@studio.dev \\
    --role superadmin
✓ created admin maria.alvarez (a_001)
  initial password: H8c-2nQ-7f9-pKv (change on first login)`}
              </pre>
              <button className="btn btn-sm" style={{marginTop: 8}}>{I.copy}Copy command</button>
            </div>

            <div className="alert">
              <span className="icon">{I.info}</span>
              <div>
                <h3>Why CLI-only?</h3>
                <span>An attacker who reaches an unconfigured instance must not be able to claim the first admin slot. The CLI proves OS-level access to the box.</span>
              </div>
            </div>

            <div className="row" style={{justifyContent:'space-between', paddingTop: 12, borderTop:'1px solid var(--border)'}}>
              <span className="muted" style={{fontSize: 12}}>This page polls every 5s and will redirect when an admin is detected.</span>
              <span className="row" style={{fontSize:12, color:'var(--fg-2)'}}><span className="live-dot"/> waiting…</span>
            </div>
          </div>
        </div>

        <div className="muted" style={{textAlign:'center', fontSize:11, marginTop: 24}}>
          GameKit · GPL-3.0 · self-hosted · docs.gamekit.local
        </div>
      </div>
    </div>
  );
}

window.Health = Health;
window.Matchmaking = Matchmaking;
window.Rankings = Rankings;
window.Admins = Admins;
window.LoginBootstrap = LoginBootstrap;
