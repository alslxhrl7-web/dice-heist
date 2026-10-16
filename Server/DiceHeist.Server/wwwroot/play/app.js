// 다이스 하이스트 멀티 브라우저 화면. 판정은 전부 서버가 하고, 여기서는 받은 상태를 그리고 버튼을 서버로 보낸다.
'use strict';

const $ = id => document.getElementById(id);
const PIPS = {1:[4],2:[0,8],3:[0,4,8],4:[0,2,6,8],5:[0,2,4,6,8],6:[0,2,3,5,6,8]};
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt = n => Number(n).toLocaleString('ko-KR');

let token = null;
try { token = localStorage.getItem('dh.token'); } catch {}
let profile = null, rooms = [], state = null, conn = null, devGrant = false;
let held = [false,false,false,false,false], heldKey = '', clockOffset = 0;
// 고른 레이즈 크기(이번 판 누적). 판이 바뀌면 기본 크기로 돌아간다
let raiseTo = 0, raiseKey = '';

// ---------- 서버 호출 ----------

async function api(path, body) {
  const res = await fetch(path, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (res.status === 401) { logout(); throw new Error('다시 로그인해 주세요.'); }
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || '요청이 실패했습니다.');
  return data;
}

async function invoke(method, ...args) {
  try {
    const err = await conn.invoke(method, ...args);
    if (err) showError(err);
    return !err;
  } catch (e) { showError('서버와 연결이 끊겼습니다.'); return false; }
}

function showError(msg) {
  const el = state && profile?.inRoom ? $('gameError') : $('lobbyError');
  el.textContent = msg;
  clearTimeout(showError.t);
  showError.t = setTimeout(() => { el.textContent = ''; }, 4000);
}

// ---------- 로그인 ----------

$('authForm').addEventListener('submit', async e => {
  e.preventDefault();
  const mode = e.submitter?.dataset.mode || 'login';
  $('authError').textContent = '';
  try {
    const data = await api('/api/' + mode, { name: $('name').value.trim(), password: $('password').value });
    token = data.token;
    try { localStorage.setItem('dh.token', token); } catch {}
    profile = data.profile;
    await connect();
  } catch (err) { $('authError').textContent = err.message; }
});

function logout() {
  if (token) fetch('/api/logout', { method: 'POST', headers: { Authorization: 'Bearer ' + token } }).catch(() => {});
  token = null; profile = null; state = null;
  try { localStorage.removeItem('dh.token'); } catch {}
  if (conn) { conn.stop(); conn = null; }
  render();
}
$('logoutBtn').addEventListener('click', logout);

// ---------- 실시간 연결 ----------

async function connect() {
  if (conn) await conn.stop();
  conn = new signalR.HubConnectionBuilder()
    .withUrl('/hub', { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build();
  conn.on('profile', p => { profile = p; if (!p.inRoom) state = null; render(); });
  conn.on('lobby', list => { rooms = list; render(); });
  conn.on('state', s => {
    state = s;
    clockOffset = s.serverNow - Date.now();
    const me = s.seats[s.mySeat];
    // 판이 바뀌거나 내 주사위가 바뀌면(다시 굴림) 고정 표시를 지운다
    const key = s.roundNo + ':' + (me?.dice || []).join('') + ':' + (me?.rerolls ?? 0);
    if (key !== heldKey) { heldKey = key; held = [false,false,false,false,false]; }
    if (profile) profile.inRoom = true;
    render();
  });
  conn.on('left', reason => { state = null; if (profile) profile.inRoom = false; render(); if (reason) showError(reason); });
  conn.on('kicked', reason => { alert(reason); logout(); });
  conn.onreconnected(() => render());
  try { await conn.start(); }
  catch { logout(); $('authError').textContent = '서버에 연결하지 못했습니다. 다시 로그인해 주세요.'; }
  render();
}

// ---------- 로비 ----------

$('createForm').addEventListener('submit', e => {
  e.preventDefault();
  invoke('CreateRoom', $('roomName').value.trim());
});
$('rooms').addEventListener('click', e => {
  const b = e.target.closest('[data-join]');
  if (b) invoke('JoinRoom', b.dataset.join);
});
$('exchangeBtn').addEventListener('click', async () => {
  try { profile = await api('/api/exchange', { amount: profile.exchangeUnit }); render(); }
  catch (err) { showError(err.message); }
});
$('grantBtn').addEventListener('click', async () => {
  try { profile = await api('/api/dev/grant', {}); render(); }
  catch (err) { showError(err.message); }
});

// ---------- 테이블 ----------

$('leaveBtn').addEventListener('click', () => invoke('LeaveRoom'));
$('me').addEventListener('click', e => {
  const d = e.target.closest('[data-die]');
  if (d && !d.disabled) { const i = +d.dataset.die; held[i] = !held[i]; render(); return; }
  const b = e.target.closest('[data-act]');
  if (!b || b.disabled) return;
  const act = b.dataset.act;
  if (act === 'reroll') invoke('Reroll', held);
  else if (act === 'stand') invoke('Stand');
  else if (act === 'raise-' || act === 'raise+') { raiseTo += (act === 'raise+' ? 1 : -1) * state.room.ante; render(); }
  else if (act === 'raise') invoke('RaiseTo', raiseTo);
  else invoke('Bet', act);
});

// ---------- 그리기 ----------

// 눈은 SVG로 그린다. 예전 CSS 그리드 방식은 <button> 안에서 칸 크기가 0으로 접혀 눈이 안 보였다(크롬·사파리)
function dieFace(v) {
  const dots = (PIPS[v] || []).map(i => `<circle cx="${20 + (i % 3) * 30}" cy="${20 + Math.floor(i / 3) * 30}" r="10.5"/>`).join('');
  return `<svg viewBox="0 0 100 100" aria-hidden="true">${dots}</svg>`;
}

// 족보 표: 높은 순으로, 예시 주사위와 한 번 굴렸을 때 나올 확률. 지금 내 족보 줄을 칠한다
const EXAMPLES = { 7:[6,6,6,6,6], 6:[5,5,5,5,2], 5:[2,3,4,5,6], 4:[4,4,4,2,2], 3:[3,3,3,6,1], 2:[5,5,2,2,6], 1:[4,4,1,3,6], 0:[1,2,3,5,6] };
let hands = [];
function renderHands(myRank) {
  const el = $('hands');
  if (!el || !hands.length) return;
  el.innerHTML = hands.map(h => `
    <li class="${h.rank === myRank ? 'mine' : ''}">
      <span class="rank-name">${esc(h.name)}${h.rank === myRank ? ' <span class="pill first">지금 내 족보</span>' : ''}</span>
      <span class="mini">${(EXAMPLES[h.rank] || []).map(v => `<span class="die xs">${dieFace(v)}</span>`).join('')}</span>
      <span class="num chance">${(h.chance * 100).toFixed(h.chance < 0.01 ? 1 : 0)}%</span>
    </li>`).join('');
}

function statusPill(s) {
  if (s.folded) return '<span class="pill fold">폴드 (포기)</span>';
  if (s.status === 'raised') return `<span class="pill raise">레이즈 (올리기) ${esc(s.times)}</span>`;
  if (s.status === 'called') return `<span class="pill call">콜 (따라가기) ${esc(s.times)}</span>`;
  if (!s.inHand) return '<span class="pill">다음 판부터</span>';
  if (s.stood) return '<span class="pill">확정</span>';
  return '<span class="pill">대기</span>';
}

function render() {
  const loggedIn = !!(token && profile);
  $('auth').hidden = loggedIn;
  $('lobby').hidden = !loggedIn || !!state;
  $('game').hidden = !loggedIn || !state;

  $('stats').innerHTML = loggedIn ? [
    ['계정', esc(profile.name)], ['싱글 칩', fmt(profile.chips)], ['사이버머니', fmt(profile.money)],
  ].map(([k, v]) => `<div class="stat"><span>${k}</span><b class="num">${v}</b></div>`).join('') : '';
  if (!loggedIn) return;

  if (!state) renderLobby(); else renderGame();
}

function renderLobby() {
  const ex = $('exchangeBtn');
  ex.innerHTML = `환전 ${fmt(profile.exchangeUnit)}칩 → ${fmt(profile.exchangeUnit * profile.exchangeRate)}<small>바꿀 수 있는 칩 ${fmt(profile.exchangeable)}</small>`;
  ex.disabled = profile.exchangeable < profile.exchangeUnit;
  $('grantBtn').hidden = !devGrant;

  const canPlay = profile.money >= profile.buyIn;
  $('lobbyNote').textContent =
    `방에 앉으면 사이버머니 ${fmt(profile.buyIn)}을 테이블로 가져가고, 일어나면 남은 만큼 돌려받습니다.`
    + (canPlay ? '' : ' 지금은 사이버머니가 모자랍니다. PC 게임(싱글)에서 칩을 벌어 환전하면 채울 수 있습니다.');
  $('createForm').querySelector('button').disabled = !canPlay;

  $('rooms').innerHTML = rooms.length ? rooms.map(r => `
    <li>
      <div><strong>${esc(r.name)}</strong>
        <span class="pill num">${r.players}/${r.maxPlayers}명</span>
        <span class="pill">${r.phase === 'waiting' || r.phase === 'countdown' ? '대기 중' : '게임 중'}</span>
        <span class="note num">앤티 ${fmt(r.ante)} · 바이인 ${fmt(r.buyIn)}</span></div>
      <button class="btn primary" data-join="${esc(r.id)}" ${!canPlay || r.players >= r.maxPlayers ? 'disabled' : ''}>앉기</button>
    </li>`).join('') : '<li class="note">아직 방이 없습니다. 방을 만들어 보세요.</li>';
}

function renderGame() {
  const s = state, mine = s.seats[s.mySeat], me = s.me;
  $('roomTitle').textContent = `${s.room.name} · 앤티 ${fmt(s.room.ante)} · ${s.roundNo ? s.roundNo + '판' : '시작 전'}`;

  $('opps').innerHTML = s.seats.map((p, i) => i === s.mySeat ? '' : `
    <div class="opp ${p.turn ? 'turn' : ''} ${p.folded || !p.connected ? 'out' : ''} ${p.winner ? 'win' : ''}">
      <div class="top"><strong>${esc(p.name)}</strong>
        <span class="num">${fmt(p.stack)}</span>
        ${p.starter ? '<span class="pill first">선</span>' : ''}
        ${!p.connected ? '<span class="pill">연결 끊김</span>' : p.leaving ? '<span class="pill">나가는 중</span>' : ''}</div>
      <div class="row" style="gap:6px"><span class="pill">리롤 ${p.rerolls}회</span>${statusPill(p)}</div>
      ${p.inHand ? `<div class="mini" aria-label="${p.dice ? '공개된 주사위 ' + p.dice.join(', ') : '숨겨진 주사위'}">
        ${(p.dice || [0,0,0,0,0]).map(v => p.dice ? `<span class="die sm">${dieFace(v)}</span>` : '<span class="die sm back"></span>').join('')}</div>` : ''}
      ${p.hand ? `<div style="font-family:var(--display)">${esc(p.hand)}</div>` : ''}
    </div>`).join('') || '<p class="note">다른 사람을 기다리는 중입니다. 2명이 되면 시작합니다.</p>';

  $('pot').innerHTML = `<b class="num">${fmt(s.pot)}</b><span>팟 (판돈)</span>`;

  const bn = $('banner');
  bn.hidden = !s.banner;
  if (s.banner) { bn.className = 'banner' + (mine?.winner ? ' win' : ''); bn.textContent = s.banner; }

  let controls = '', hint = '';
  const holding = s.phase === 'hold' && me && me.canReroll;
  if (s.phase === 'waiting') hint = '2명 이상 모이면 시작합니다.';
  else if (s.phase === 'countdown') hint = '곧 시작합니다…';
  else if (!me) hint = '이번 판이 끝나면 함께합니다.';
  else if (s.phase === 'hold') {
    if (me.stood) hint = '확정했습니다. 다른 사람을 기다리는 중…';
    else {
      hint = '남길 주사위를 눌러 고정하고, 나머지를 다시 굴리세요.';
      controls = `<button class="btn" data-act="reroll" ${held.every(Boolean) ? 'disabled' : ''}>다시 굴리기 · ${me.rerollsLeft}회 남음</button>
        <button class="btn primary" data-act="stand">이대로 확정</button>`;
    }
  } else if (s.phase === 'betting') {
    if (me.myTurn) {
      const ante = s.room.ante;
      const key = s.roundNo + ':' + me.minRaise + ':' + me.maxRaise;
      if (key !== raiseKey) { raiseKey = key; raiseTo = s.room.raiseTotal; }
      if (me.canRaise) raiseTo = Math.max(me.minRaise, Math.min(me.maxRaise, raiseTo));
      const times = n => `${+(n / ante).toFixed(1)}배`;
      hint = me.responding ? '뒤에서 레이즈(올리기)가 나왔습니다. 차액을 내고 콜할까요?'
        : me.raiseUsed ? '이번 판 레이즈(올리기)는 이미 나왔습니다. 콜하거나 폴드하세요.'
        : `배수는 앤티 기준. 레이즈(올리기)는 판당 한 명만, −/+로 ${me.canRaise ? `${times(me.minRaise)}~${times(me.maxRaise)}` : '크기를'} 고릅니다.`;
      controls = `<button class="btn" data-act="fold">폴드 (포기)</button>
        <button class="btn primary" data-act="call" ${me.canCall ? '' : 'disabled'}>콜 (따라가기)<small>+${fmt(me.callCost)} · ${esc(me.callTimes)}</small></button>
        ${me.raiseUsed || me.responding ? '' : `<span class="raise-pick">
          <button class="btn step" data-act="raise-" aria-label="레이즈 줄이기" ${me.canRaise && raiseTo > me.minRaise ? '' : 'disabled'}>−</button>
          <button class="btn raise" data-act="raise" ${me.canRaise ? '' : 'disabled'}>레이즈 (올리기)<small>${me.canRaise ? `+${fmt(raiseTo - me.contrib)} · ${times(raiseTo)}` : '머니가 모자랍니다'}</small></button>
          <button class="btn step" data-act="raise+" aria-label="레이즈 늘리기" ${me.canRaise && raiseTo < me.maxRaise ? '' : 'disabled'}>+</button></span>`}`;
    } else {
      const t = s.seats.find(p => p.turn);
      hint = t ? `${t.name} 차례…` : '';
    }
  } else if (s.phase === 'result') hint = '곧 다음 판이 시작됩니다.';

  const dice = mine?.dice;
  const meEl = $('me');
  meEl.className = 'me' + ((holding || me?.myTurn) ? ' turn' : '') + (mine?.winner ? ' win' : '');
  meEl.innerHTML = `
    <div class="top"><strong>${esc(mine?.name)} (나)</strong><span class="num">${fmt(mine?.stack ?? 0)}</span>
      ${mine?.starter ? '<span class="pill first">선</span>' : ''}
      ${mine ? `<span class="pill">리롤 ${mine.rerolls}회</span>${statusPill(mine)}` : ''}</div>
    ${dice ? `<div class="dice">${dice.map((v, i) => `<button class="die ${held[i] ? 'held' : ''}" data-die="${i}"
        aria-pressed="${held[i]}" aria-label="주사위 ${v}${held[i] ? ', 고정됨' : ''}" ${holding ? '' : 'disabled'}>${dieFace(v)}</button>`).join('')}</div>
      <div class="hand">${esc(mine.hand)}<small>지금 내 족보</small></div>` : ''}
    <div class="controls">${controls}</div>
    ${hint ? `<p class="hint">${esc(hint)}</p>` : ''}`;

  $('log').innerHTML = s.log.map(l => `<li>${esc(l)}</li>`).join('');
  renderHands(mine?.rank ?? -1);
}

// 남은 시간 막대
setInterval(() => {
  const bar = $('timerBar');
  if (!state || !state.deadline) { bar.style.width = '0'; return; }
  const total = state.phaseMs || 1;
  const left = state.deadline - (Date.now() + clockOffset);
  bar.style.width = Math.max(0, Math.min(100, left / total * 100)) + '%';
}, 200);

// ---------- 시작 ----------

(async () => {
  try {
    const cfg = await (await fetch('/api/config')).json();
    devGrant = cfg.devGrant;
    hands = cfg.hands || [];
  } catch {}
  renderHands(-1);
  if (token) {
    try { profile = await api('/api/me'); await connect(); }
    catch { token = null; }
  }
  render();
})();
