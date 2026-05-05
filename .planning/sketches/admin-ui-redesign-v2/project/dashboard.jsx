/* GameKit Admin — Dashboard (4-card grid: Health, Queue, Audit, Quick stats) */

function Dashboard({ go }) {
  const [tick, setTick] = useState(0);
  useEffect(() => { const t = setInterval(() => setTick(x => x + 1), 10000); return () => clearInterval(t); }, []);

  return (
    <>
      <div className="page-head">
        <div>
          <div className="crumbs"><span>GameKit</span><span className="sep">/</span><span>Dashboard</span></div>
          <h1>Operations</h1>
          <div className="sub">prod-eu · build v3.4.1 · uptime 14d 6h · 4 admins online</div>
        </div>
        <div className="actions">
          <span className="row" style={{fontSize: 12, color: 'var(--fg-2)'}}>
            <span className="live-dot" aria-hidden/> auto-refresh 10s · last {tick * 10}s ago
          </span>
          <button className="btn"><span style={{display:'inline-flex'}}>{I.refresh}</span>Refresh now</button>
          <button className="btn btn-primary" onClick={() => go('audit')}>Open audit log</button>
        </div>
      </div>

      <div className="grid-12">
        {/* Health card */}
        <div className="card span-8">
          <div className="card-head">
            <h2><span className="live-dot" aria-hidden/> Service health</h2>
            <a className="muted" onClick={() => go('health')} style={{cursor:'pointer', fontSize:12}}>Detail →</a>
          </div>
          <div className="card-body" style={{display:'grid', gridTemplateColumns:'repeat(3, 1fr)', gap: 16}}>
            <HealthMini name="Postgres" data={GK.HEALTH.postgres} />
            <HealthMini name="Redis"    data={GK.HEALTH.redis} />
            <HealthMini name="Error rate" data={GK.HEALTH.errorRate} />
          </div>
        </div>

        {/* Quick stats card */}
        <div className="card span-4">
          <div className="card-head">
            <h2>At a glance</h2>
            <span className="muted" style={{fontSize:12}}>last 24h</span>
          </div>
          <div className="card-body" style={{display:'grid', gridTemplateColumns:'1fr 1fr', gap: 20, rowGap: 24}}>
            <Stat v="12,418" label="Sessions today" delta="+8.2%" up />
            <Stat v="247" label="New players" delta="+11" up />
            <Stat v="3" label="Bans (24h)" delta="2 by you" />
            <Stat v="6 / 1" label="Admins · super" delta="all 2FA enabled" />
            <Stat v="328" label="Banned (total)" delta="+1 today" />
            <Stat v="42m" label="Avg session" delta="−2m" down />
          </div>
        </div>

        {/* Queue depth card */}
        <div className="card span-5">
          <div className="card-head">
            <h2>Matchmaking queue</h2>
            <a className="muted" onClick={() => go('matchmaking')} style={{cursor:'pointer', fontSize:12}}>Detail →</a>
          </div>
          <div className="card-body flush">
            <div style={{padding:'12px 16px', borderBottom:'1px solid var(--border)', display:'flex', alignItems:'baseline', gap:12}}>
              <span style={{fontSize:28, fontWeight:600, fontVariantNumeric:'tabular-nums'}}>470</span>
              <span className="muted" style={{fontSize:13}}>players queued · 6 pools</span>
              <span style={{marginLeft:'auto'}}><Chip kind="degraded">eu-west backed up</Chip></span>
            </div>
            <table className="t" style={{borderRadius:0}}>
              <thead><tr>
                <th>Pool</th><th className="num">Depth</th><th className="num">Wait</th><th className="num">Workers</th><th>Status</th>
              </tr></thead>
              <tbody>
                {GK.QUEUES.map(q => (
                  <tr key={q.pool}>
                    <td className="mono">{q.pool}</td>
                    <td className="num"><b>{q.depth}</b></td>
                    <td className="num muted">{q.wait}</td>
                    <td className="num muted">{q.workers}</td>
                    <td><Chip kind={q.status}>{q.status}</Chip></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Audit feed card */}
        <div className="card span-7">
          <div className="card-head">
            <h2>Recent activity</h2>
            <div className="row" style={{fontSize:12}}>
              <span className="muted">last 30 min · </span>
              <a className="muted" onClick={() => go('audit')} style={{cursor:'pointer'}}>View all →</a>
            </div>
          </div>
          <div className="card-body flush">
            <div className="feed">
              {GK.AUDIT.slice(0, 6).map(ev => <FeedItem key={ev.id} ev={ev} />)}
            </div>
          </div>
        </div>
      </div>
    </>
  );
}

function HealthMini({ name, data }) {
  return (
    <div>
      <div className="row" style={{justifyContent:'space-between', marginBottom: 6}}>
        <span style={{fontWeight:600, fontSize:13}}>{name}</span>
        <Chip kind={data.status}>{data.status}</Chip>
      </div>
      <div style={{fontSize:24, fontWeight:600, letterSpacing:'-0.01em', fontVariantNumeric:'tabular-nums'}}>
        {data.value}<span className="muted" style={{fontSize:12, fontWeight:400, marginLeft: 4}}>{data.unit}</span>
      </div>
      <div className="muted" style={{fontSize:11, marginTop: 2}}>{data.detail}</div>
      <div style={{marginTop: 10}}>
        <Spark
          data={name === 'Redis' ? GK.SPARK_QPS.map(v => v*0.04+0.6) : name === 'Postgres' ? GK.SPARK_LAT : [0.1,0.1,0.12,0.1,0.11,0.13,0.1,0.09,0.11,0.12,0.1,0.13,0.15,0.18,0.2,0.22,0.2,0.18,0.21,0.24,0.28,0.32,0.36,0.42,0.45]}
          color={data.status === 'healthy' ? 'var(--green)' : data.status === 'degraded' ? 'var(--amber)' : 'var(--red)'}
          fill={data.status === 'healthy' ? 'rgba(22,163,74,0.08)' : data.status === 'degraded' ? 'rgba(217,119,6,0.10)' : 'rgba(220,38,38,0.10)'}
          height={36}
        />
      </div>
    </div>
  );
}

function Stat({ v, label, delta, up, down }) {
  return (
    <div className="metric">
      <span className="lbl">{label}</span>
      <span className="v">{v}</span>
      {delta && <span className={`delta${up ? ' up' : ''}${down ? ' down' : ''}`}>{delta}</span>}
    </div>
  );
}

function FeedItem({ ev, onClick }) {
  const [verb, actor, action, target, extra] = ev.sentence;
  const glyphMap = { ban: 'ban', unban: 'unban', 'gdpr.delete': 'delete', 'admin.create': 'create', 'rank.adjust': 'rank', login: 'login', 'pool.scale': '' };
  const iconMap = { ban: I.ban, unban: I.unban, 'gdpr.delete': I.trash, 'admin.create': I.plus, 'rank.adjust': I.rank, login: I.shield, 'pool.scale': I.refresh };
  return (
    <div className="feed-item" onClick={onClick}>
      <span className={`glyph ${glyphMap[verb] || ''}`}>{iconMap[verb] || I.info}</span>
      <div>
        <div className="sentence">
          <b>{actor}</b> {action} <b>{target}</b>{extra ? <> <span className="muted">— {extra}</span></> : null}
        </div>
        {ev.reason && <div className="reason">“{ev.reason}”</div>}
      </div>
      <span className="when" title={GK.fmtAbs(ev.when)}>{GK.fmtRel(ev.when)}</span>
    </div>
  );
}

window.Dashboard = Dashboard;
window.FeedItem = FeedItem;
