/* GameKit Admin — Dialogs (Ban, Unban, GDPR delete, Create/Delete admin) */

function Modal({ children, onClose }) {
  useEffect(() => {
    const onKey = (e) => { if (e.key === 'Escape') onClose && onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  return <div className="scrim" onClick={onClose}><div className="modal" onClick={e => e.stopPropagation()}>{children}</div></div>;
}

function BanDialog({ player, onClose, onConfirm }) {
  const [reason, setReason] = useState('');
  const [duration, setDuration] = useState('permanent');
  const valid = reason.length >= 3 && reason.length <= 512;
  return (
    <Modal onClose={onClose}>
      <div className="modal-head">
        <span className="icon-wrap danger">{I.ban}</span>
        <div>
          <h3>Ban <span className="confirm-target">{player.display}</span>?</h3>
          <div className="sub">The player will be signed out of all sessions and blocked from matchmaking.</div>
        </div>
      </div>
      <div className="modal-body">
        <label className="field">Duration
          <div className="btn-group" style={{marginTop:4}}>
            {['24h','7d','30d','permanent'].map(d => (
              <button key={d} className={`btn${duration===d?' on':''}`} onClick={() => setDuration(d)}>{d}</button>
            ))}
          </div>
        </label>
        <label className="field">Reason <span className="hint">required · 3–512 chars · visible in audit log</span>
          <textarea
            className={`textarea${reason && !valid ? ' invalid' : ''}`}
            rows="4" autoFocus value={reason}
            onChange={e => setReason(e.target.value)}
            placeholder="e.g. Confirmed wallhack via anti-cheat report #4421."/>
          <span className="hint">{reason.length}/512</span>
        </label>
      </div>
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>Cancel</button>
        <button className="btn btn-danger solid" disabled={!valid} style={!valid?{opacity:0.5,pointerEvents:'none'}:undefined}
          onClick={() => onConfirm({ reason, duration })}>{I.ban}Ban {duration === 'permanent' ? 'permanently' : `for ${duration}`}</button>
      </div>
    </Modal>
  );
}

function UnbanDialog({ player, onClose, onConfirm }) {
  const [reason, setReason] = useState('');
  return (
    <Modal onClose={onClose}>
      <div className="modal-head">
        <span className="icon-wrap" style={{background:'var(--green-bg)', color:'var(--green)'}}>{I.unban}</span>
        <div>
          <h3>Unban <span className="confirm-target">{player.display}</span>?</h3>
          <div className="sub">The player will regain access to matchmaking and ranked play.</div>
        </div>
      </div>
      <div className="modal-body">
        <div className="alert" style={{margin:'0 0 12px'}}>
          <span className="icon">{I.info}</span>
          <div>
            <h3>Original ban</h3>
            <span>“{player.banReason}” — by <b>{player.bannedBy}</b>, {GK.fmtRel(player.bannedAt)}</span>
          </div>
        </div>
        <label className="field">Reason <span className="hint">optional · audited</span>
          <textarea className="textarea" rows="3" value={reason} onChange={e => setReason(e.target.value)} placeholder="e.g. Appeal granted, false positive."/>
        </label>
      </div>
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>Cancel</button>
        <button className="btn btn-primary" onClick={() => onConfirm({ reason })}>{I.unban}Unban player</button>
      </div>
    </Modal>
  );
}

function GdprDialog({ player, onClose, onConfirm }) {
  const [typed, setTyped] = useState('');
  const ok = typed === player.display;
  return (
    <Modal onClose={onClose}>
      <div className="modal-head">
        <span className="icon-wrap danger">{I.trash}</span>
        <div>
          <h3>GDPR delete <span className="confirm-target">{player.display}</span></h3>
          <div className="sub">This permanently anonymizes the player record. <b>Cannot be undone.</b></div>
        </div>
      </div>
      <div className="modal-body">
        <div className="alert red" style={{margin:'0 0 12px'}}>
          <span className="icon">{I.warn}</span>
          <div>
            <h3>What this does</h3>
            <span>Display name → "[redacted]" · email + identities purged · match history kept (anonymized) · audit entries retain admin actor for 7 years per policy.</span>
          </div>
        </div>
        <label className="field">Type <span className="confirm-target">{player.display}</span> to confirm
          <input className={`input mono${typed && !ok ? ' invalid' : ''}`} autoFocus value={typed} onChange={e => setTyped(e.target.value)} placeholder={player.display}/>
        </label>
      </div>
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>Cancel</button>
        <button className="btn btn-danger solid" disabled={!ok} style={!ok?{opacity:0.5,pointerEvents:'none'}:undefined}
          onClick={() => onConfirm()}>{I.trash}Permanently delete</button>
      </div>
    </Modal>
  );
}

function CreateAdminDialog({ onClose, onConfirm }) {
  const [user, setUser] = useState('');
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('admin');
  const ok = user.length >= 3 && /\S+@\S+\.\S+/.test(email);
  return (
    <Modal onClose={onClose}>
      <div className="modal-head">
        <span className="icon-wrap" style={{background:'var(--accent-50)', color:'var(--accent)'}}>{I.plus}</span>
        <div>
          <h3>Create admin account</h3>
          <div className="sub">Initial password is shown once on creation; the user must change it on first login.</div>
        </div>
      </div>
      <div className="modal-body col" style={{gap: 12}}>
        <label className="field">Username <span className="hint">— lowercase, dot-separated</span>
          <input className="input mono" autoFocus value={user} onChange={e => setUser(e.target.value)} placeholder="firstname.lastname"/>
        </label>
        <label className="field">Email
          <input className="input mono" value={email} onChange={e => setEmail(e.target.value)} placeholder="firstname.lastname@studio.dev"/>
        </label>
        <label className="field">Role
          <div className="btn-group" style={{marginTop:4}}>
            <button className={`btn${role==='admin'?' on':''}`} onClick={() => setRole('admin')}>Admin</button>
            <button className={`btn${role==='superadmin'?' on':''}`} onClick={() => setRole('superadmin')}>Superadmin</button>
          </div>
        </label>
      </div>
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>Cancel</button>
        <button className="btn btn-primary" disabled={!ok} style={!ok?{opacity:0.5,pointerEvents:'none'}:undefined} onClick={() => onConfirm({user,email,role})}>Create account</button>
      </div>
    </Modal>
  );
}

function DeleteAdminDialog({ admin, onClose, onConfirm }) {
  return (
    <Modal onClose={onClose}>
      <div className="modal-head">
        <span className="icon-wrap danger">{I.trash}</span>
        <div>
          <h3>Delete admin <span className="confirm-target">{admin.user}</span>?</h3>
          <div className="sub">Their sessions are revoked immediately. Audit history attributed to them is preserved.</div>
        </div>
      </div>
      <div className="modal-body">
        <dl className="kv" style={{fontSize:13}}>
          <dt>Role</dt><dd>{admin.role}</dd>
          <dt>Email</dt><dd className="mono">{admin.email}</dd>
          <dt>Last seen</dt><dd className="muted">{GK.fmtRel(admin.lastSeen)}</dd>
        </dl>
      </div>
      <div className="modal-foot">
        <button className="btn" onClick={onClose}>Cancel</button>
        <button className="btn btn-danger solid" onClick={onConfirm}>{I.trash}Delete admin</button>
      </div>
    </Modal>
  );
}

window.BanDialog = BanDialog;
window.UnbanDialog = UnbanDialog;
window.GdprDialog = GdprDialog;
window.CreateAdminDialog = CreateAdminDialog;
window.DeleteAdminDialog = DeleteAdminDialog;
