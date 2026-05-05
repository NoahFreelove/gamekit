/* GameKit Admin — top-level App + Tweaks wiring */

function App() {
  const [tweaks, setTweak] = useTweaks(window.TWEAK_DEFAULTS);
  const [page, setPage] = useState('dashboard');
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [dialog, setDialog] = useState(null); // { type, payload }
  const [snacks, setSnacks] = useState([]);

  // expose snack pusher + sidebar toggle for primitives
  useEffect(() => {
    window.GK_pushSnack = (s) => {
      const id = Math.random();
      setSnacks(arr => [...arr, { id, ...s }]);
      if (!s.error) setTimeout(() => setSnacks(arr => arr.filter(x => x.id !== id)), 2400);
    };
    window.GK_toggleSidebar = () => setTweak('sidebar', tweaks.sidebar === 'expanded' ? 'collapsed' : 'expanded');
  }, [tweaks.sidebar]);

  // global keyboard
  useEffect(() => {
    const onKey = (e) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setPaletteOpen(v => !v);
      }
      if ((e.metaKey || e.ctrlKey) && e.key === '\\') {
        e.preventDefault();
        setTweak('sidebar', tweaks.sidebar === 'expanded' ? 'collapsed' : 'expanded');
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [tweaks.sidebar]);

  const openBan        = (player) => setDialog({ type: 'ban', payload: player });
  const openUnban      = (player) => setDialog({ type: 'unban', payload: player });
  const openGDPR       = (player) => setDialog({ type: 'gdpr', payload: player });
  const openCreate     = () => setDialog({ type: 'createAdmin' });
  const openDeleteAdm  = (admin) => setDialog({ type: 'deleteAdmin', payload: admin });
  const openLogin      = () => setPage('login');
  const close          = () => setDialog(null);

  const go = (id) => { setPage(id); setPaletteOpen(false); };

  if (page === 'login') {
    return <>
      <LoginBootstrap />
      <button className="btn" style={{position:'fixed', top:16, right:16, zIndex:80}} onClick={() => setPage('dashboard')}>← Back to console</button>
      <TweaksWiring tweaks={tweaks} setTweak={setTweak}/>
    </>;
  }

  return (
    <div className="shell" data-accent={tweaks.accent} data-density={tweaks.density} data-sidebar={tweaks.sidebar} data-ban-loud={tweaks.banLoudness}>
      <TopNav onSidebar={() => setTweak('sidebar', tweaks.sidebar === 'expanded' ? 'collapsed' : 'expanded')}
              onPalette={() => setPaletteOpen(true)}
              onShowDialog={openLogin} />
      <div className="body">
        <Sidebar active={page} setActive={go} />
        <main className="main" role="main">
          {page === 'dashboard'   && <Dashboard go={go} />}
          {page === 'players'     && <Players openBan={openBan} openUnban={openUnban} openGDPR={openGDPR} />}
          {page === 'audit'       && <Audit />}
          {page === 'health'      && <Health />}
          {page === 'matchmaking' && <Matchmaking />}
          {page === 'rankings'    && <Rankings />}
          {page === 'admins'      && <Admins onCreate={openCreate} onDelete={openDeleteAdm} />}
        </main>
      </div>

      {paletteOpen && <Palette onClose={() => setPaletteOpen(false)} go={go}
                               openBan={openBan} openGDPR={openGDPR}
                               openCreateAdmin={openCreate} openLogin={openLogin} />}

      {dialog?.type === 'ban'         && <BanDialog player={dialog.payload} onClose={close}
                                                     onConfirm={({reason, duration}) => { close(); window.GK_pushSnack({msg:`Banned ${dialog.payload.display} (${duration})`}); }} />}
      {dialog?.type === 'unban'       && <UnbanDialog player={dialog.payload} onClose={close}
                                                       onConfirm={() => { close(); window.GK_pushSnack({msg:`Unbanned ${dialog.payload.display}`}); }} />}
      {dialog?.type === 'gdpr'        && <GdprDialog player={dialog.payload} onClose={close}
                                                      onConfirm={() => { close(); window.GK_pushSnack({msg:`GDPR-deleted ${dialog.payload.display}`, error:true}); }} />}
      {dialog?.type === 'createAdmin' && <CreateAdminDialog onClose={close}
                                                             onConfirm={({user}) => { close(); window.GK_pushSnack({msg:`Created admin ${user} — initial password copied to clipboard`}); }} />}
      {dialog?.type === 'deleteAdmin' && <DeleteAdminDialog admin={dialog.payload} onClose={close}
                                                             onConfirm={() => { close(); window.GK_pushSnack({msg:`Deleted admin ${dialog.payload.user}`, error:true}); }} />}

      <div className="snackbar">
        {snacks.map(s => (
          <div key={s.id} className={`snack${s.error ? ' error' : ''}`}>
            {s.error ? I.warn : I.check}
            <span style={{flex:1}}>{s.msg}</span>
            <span className="x" onClick={() => setSnacks(arr => arr.filter(x => x.id !== s.id))}>✕</span>
          </div>
        ))}
      </div>

      <TweaksWiring tweaks={tweaks} setTweak={setTweak}/>
    </div>
  );
}

function TweaksWiring({ tweaks, setTweak }) {
  return (
    <TweaksPanel title="Tweaks">
      <TweakSection title="Accent">
        <TweakRadio value={tweaks.accent} onChange={v => setTweak('accent', v)}
          options={[
            { value:'violet', label:'Violet' },
            { value:'indigo', label:'Indigo' },
            { value:'teal',   label:'Teal' },
            { value:'orange', label:'Orange' },
            { value:'slate',  label:'Slate' },
          ]}/>
      </TweakSection>
      <TweakSection title="Layout">
        <TweakRadio label="Density" value={tweaks.density} onChange={v => setTweak('density', v)}
          options={[{value:'comfortable', label:'Comfortable'},{value:'compact', label:'Compact'}]}/>
        <TweakRadio label="Sidebar" value={tweaks.sidebar} onChange={v => setTweak('sidebar', v)}
          options={[{value:'expanded', label:'Expanded'},{value:'collapsed', label:'Collapsed'}]}/>
      </TweakSection>
      <TweakSection title="Banned-player loudness">
        <TweakRadio value={tweaks.banLoudness} onChange={v => setTweak('banLoudness', v)}
          options={[{value:'subtle',label:'Subtle'},{value:'medium',label:'Medium'},{value:'loud',label:'Loud'}]}/>
      </TweakSection>
      <TweakSection title="Dashboard direction">
        <TweakSelect value={tweaks.dashboardDirection} onChange={v => setTweak('dashboardDirection', v)}
          options={[
            { value:'D', label:'D — Spec default (4-card grid)' },
            { value:'A', label:'A — Operator inbox (preview)' },
            { value:'B', label:'B — Status board (preview)' },
            { value:'C', label:'C — One number first (preview)' },
          ]}/>
      </TweakSection>
    </TweaksPanel>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App/>);
