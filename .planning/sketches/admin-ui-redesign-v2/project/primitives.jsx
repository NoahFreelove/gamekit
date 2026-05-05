/* GameKit Admin — small primitives: icons, copy-uuid, sparkline, etc. */

const { useState, useEffect, useRef, useMemo, useCallback } = React;

// ----- icons (single-line, 16x16, currentColor) -----
const I = {
  search:   <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><circle cx="7" cy="7" r="4.5"/><path d="m11 11 3 3"/></svg>,
  command:  <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"><path d="M5 5h6v6H5z M5 5a2 2 0 1 1-2 2h2v-2zM11 5a2 2 0 1 0 2 2h-2v-2zM5 11a2 2 0 1 0-2-2h2v2zM11 11a2 2 0 1 1 2-2h-2v2z"/></svg>,
  copy:     <svg className="copy-icon icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5"><rect x="5" y="5" width="8" height="8" rx="1.5"/><path d="M3 11V4a1 1 0 0 1 1-1h7"/></svg>,
  check:    <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="m3 8 3.5 3.5L13 5"/></svg>,
  ban:      <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="8" cy="8" r="6"/><path d="m4 4 8 8"/></svg>,
  unban:    <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><path d="M3 8.5 6 11.5 13 4.5"/></svg>,
  trash:    <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><path d="M3 4h10M6 4V3a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v1M5 4l.5 9a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1L11 4"/></svg>,
  warn:     <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" strokeLinecap="round"><path d="M8 2 1.5 13.5h13L8 2z"/><path d="M8 6.5v3M8 11.5v.5"/></svg>,
  info:     <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><circle cx="8" cy="8" r="6"/><path d="M8 7v4M8 5v.5"/></svg>,
  plus:     <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><path d="M8 3v10M3 8h10"/></svg>,
  filter:   <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><path d="M2 3h12l-4.5 6v4l-3 1.5V9z"/></svg>,
  refresh:  <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><path d="M3 8a5 5 0 0 1 8.5-3.5L13 6M13 3v3h-3M13 8a5 5 0 0 1-8.5 3.5L3 10M3 13v-3h3"/></svg>,
  chevron:  <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><path d="m6 4 4 4-4 4"/></svg>,
  chevronD: <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><path d="m4 6 4 4 4-4"/></svg>,
  download: <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><path d="M8 2v8m0 0 3-3M8 10 5 7M3 12v1a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1v-1"/></svg>,
  external: <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><path d="M9 3h4v4M13 3 7 9M11 9v3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h3"/></svg>,
  // sidebar nav
  home:     <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round"><path d="M2.5 7.5 8 3l5.5 4.5V13a1 1 0 0 1-1 1H3.5a1 1 0 0 1-1-1z"/></svg>,
  users:    <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="6" cy="6" r="2.5"/><path d="M2 13c.5-2 2-3 4-3s3.5 1 4 3"/><circle cx="11" cy="5" r="2"/><path d="M10 10c2 0 3.5 1 4 3"/></svg>,
  log:      <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"><rect x="2.5" y="2.5" width="11" height="11" rx="1.5"/><path d="M5 6h6M5 8.5h6M5 11h4"/></svg>,
  pulse:    <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"><path d="M2 8h2.5l1.5-4 3 8 1.5-4H14"/></svg>,
  match:    <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"><path d="M3 8a5 5 0 0 1 5-5 5 5 0 0 1 5 5M3 8a5 5 0 0 0 5 5 5 5 0 0 0 5-5"/><circle cx="8" cy="8" r="1.5"/></svg>,
  rank:     <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round"><path d="M4 14V8M8 14V4M12 14v-8"/></svg>,
  shield:   <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round"><path d="M8 2 3 4v4c0 3 2.5 5 5 6 2.5-1 5-3 5-6V4z"/></svg>,
  cog:      <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6"><circle cx="8" cy="8" r="2"/><path d="M8 1v2M8 13v2M1 8h2M13 8h2M3 3l1.5 1.5M11.5 11.5 13 13M3 13l1.5-1.5M11.5 4.5 13 3"/></svg>,
  collapse: <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><path d="M6 4 2 8l4 4M14 4l-4 4 4 4"/></svg>,
  expand:   <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round"><path d="m2 4 4 4-4 4M14 4l-4 4 4 4"/></svg>,
  steam:    <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.4"><circle cx="6" cy="6" r="2.5"/><circle cx="10.5" cy="10" r="1.8"/><path d="M6 8.5 9 9.6"/></svg>,
  epic:     <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.4"><rect x="3" y="2.5" width="10" height="11" rx="1"/><path d="M6 6h4M6 8h3M6 10h2"/></svg>,
  discord:  <svg className="icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round"><path d="M4 4c2-1 6-1 8 0l1 8c-1 .8-2.4 1.4-3.5 1.5L9 12c-.7.2-1.3.2-2 0l-.5 1.5C5.4 13.4 4 12.8 3 12z"/><circle cx="6.3" cy="8.5" r="0.8"/><circle cx="9.7" cy="8.5" r="0.8"/></svg>,
  drag:     <svg className="icon" viewBox="0 0 16 16" fill="currentColor"><circle cx="6" cy="4" r="1"/><circle cx="10" cy="4" r="1"/><circle cx="6" cy="8" r="1"/><circle cx="10" cy="8" r="1"/><circle cx="6" cy="12" r="1"/><circle cx="10" cy="12" r="1"/></svg>,
};

window.I = I;

// ----- copy-to-clipboard UUID -----
function CopyId({ value, full, max = 8 }) {
  const [copied, setCopied] = useState(false);
  const display = full ? value : (value.length > max + 4 ? value.slice(0, max) + '…' + value.slice(-4) : value);
  return (
    <span className={`uuid${copied ? ' copied' : ''}`} title={value} onClick={(e) => {
      e.stopPropagation();
      try { navigator.clipboard.writeText(value); } catch {}
      setCopied(true);
      window.GK_pushSnack && window.GK_pushSnack({ msg: `Copied ${value.slice(0,8)}…` });
      setTimeout(() => setCopied(false), 1100);
    }}>
      <span>{display}</span>
      {copied ? I.check : I.copy}
    </span>
  );
}

// ----- sparkline -----
function Spark({ data, color, fill, height = 32, width = 160 }) {
  const max = Math.max(...data), min = Math.min(...data);
  const range = max - min || 1;
  const stepX = width / (data.length - 1);
  const points = data.map((v, i) => `${(i*stepX).toFixed(1)},${(height - 2 - ((v-min)/range)*(height-4)).toFixed(1)}`).join(' ');
  const fillPts = `0,${height} ${points} ${width},${height}`;
  return (
    <svg className="spark" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" width="100%" height={height}>
      {fill && <polygon points={fillPts} fill={fill} />}
      <polyline points={points} fill="none" stroke={color || 'var(--accent)'} strokeWidth="1.5" strokeLinejoin="round" strokeLinecap="round" />
    </svg>
  );
}

// ----- avatar (mono initials in slate square) -----
function Avatar({ name, size = 'md', dim }) {
  const initials = name.split(/[^A-Za-z0-9]/).filter(Boolean).slice(0, 2).map(s => s[0].toUpperCase()).join('') || name.slice(0,2).toUpperCase();
  return <span className={`avatar${size === 'lg' ? ' lg' : ''}`} style={dim ? { opacity: 0.5 } : undefined}>{initials}</span>;
}

// ----- chip -----
function Chip({ kind, dot = true, children, className = '' }) {
  return <span className={`chip ${kind || ''} ${className}`}>{dot && kind !== 'role' && <span className="dot"/>}{children}</span>;
}

// ----- provider icon -----
function ProviderIcon({ p }) {
  const map = { steam: I.steam, epic: I.epic, discord: I.discord };
  return map[p] || I.users;
}

// ----- kbd combiner -----
function Kbd({ children }) { return <span className="kbd">{children}</span>; }

window.CopyId = CopyId;
window.Spark = Spark;
window.Avatar = Avatar;
window.Chip = Chip;
window.ProviderIcon = ProviderIcon;
window.Kbd = Kbd;
