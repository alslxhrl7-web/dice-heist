// 함정 의자 라이어 다이스 브라우저 화면. 판정은 전부 서버가 하고, 여기서는 받은 상태를 그리고 버튼을 서버로 보낸다.
'use strict';

const $ = id => document.getElementById(id);
const PIPS = {1:[4],2:[0,8],3:[0,4,8],4:[0,2,6,8],5:[0,2,4,6,8],6:[0,2,3,5,6,8]};
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt = n => Number(n).toLocaleString('ko-KR');
const pct = x => `${Math.round(x * 100)}%`;

let token = null;
try { token = localStorage.getItem('dh.token'); } catch {}
let profile = null, rooms = [], state = null, conn = null, clockOffset = 0, stateAt = 0;
let gachaCfg = null, inventory = null;
// 부르기 고르기: 개수와 눈. 앞 부르기가 바뀌면 가장 작은 부르기로 돌아간다
let pickQty = 1, pickFace = 2, pickKey = '';
let sideTarget = -1;

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
  } catch { showError('서버와 연결이 끊겼습니다.'); return false; }
}

function showError(msg) {
  const el = state ? $('gameError') : $('lobbyError');
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
  netbar(null);
  render();
}
$('logoutBtn').addEventListener('click', logout);

// ---------- 실시간 연결 ----------

async function connect() {
  if (conn) await conn.stop();
  conn = new signalR.HubConnectionBuilder()
    .withUrl('/liarhub', { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build();
  conn.on('profile', p => { profile = p; if (!p.inRoom) state = null; render(); });
  conn.on('lobby', list => { rooms = list; render(); });
  conn.on('state', s => {
    const prev = state;
    state = s;
    stateAt = Date.now();
    clockOffset = s.serverNow - Date.now();
    cue(prev, s);
    render();
  });
  conn.on('left', reason => { state = null; Sound.stopHeart(); render(); if (reason) showError(reason); });
  conn.on('kicked', reason => { alert(reason); logout(); });
  conn.onreconnecting(() => netbar('연결이 끊겨 다시 잇는 중…'));
  conn.onreconnected(() => { netbar(null); render(); });
  conn.onclose(() => { if (token) netbar('서버와 연결이 끊겼습니다.', true); });
  try { await conn.start(); netbar(null); }
  catch { logout(); $('authError').textContent = '서버에 연결하지 못했습니다. 다시 로그인해 주세요.'; }
  render();
}

function netbar(text, retry) {
  const el = $('netbar');
  el.hidden = !text;
  if (!text) return;
  el.innerHTML = esc(text) + (retry ? ' <button class="btn" id="reconnectBtn">다시 연결</button>' : '');
  if (retry) $('reconnectBtn').addEventListener('click', () => { netbar('다시 잇는 중…'); connect(); });
}

// ---------- 소리와 연출: 바로 전 상태와 비교해 무슨 일이 일어났는지 본다 ----------

// 연출 상태: 떨어지는/살아난 자리(애니메이션), 컵 흔들기, 레버 결과를 보여 줄 판(철컥 소리 뒤에 결과를 연다)
let fx = { falling: -1, saved: -1, shakeCups: false, fallKey: "" };
const fallKey = s => s.gameNo + ":" + s.round + ":" + (s.lastPull?.seat ?? -1);
function cue(prev, s) {
  const me = s.seats[s.mySeat];
  if (!prev) { fx.fallKey = fallKey(s); if (s.phase === "lever" && s.me.canPull) Sound.startHeart(s.seats[s.mySeat]?.fallChance || 0.2); return; }
  const sameGame = prev.gameNo === s.gameNo;
  if (s.phase === 'bid' && (!sameGame || prev.round !== s.round)) { Sound.play('shake'); fx.shakeCups = true; setTimeout(() => { fx.shakeCups = false; render(); }, 1100); }
  if (s.bid && (!prev.bid || prev.bid.quantity !== s.bid.quantity || prev.bid.face !== s.bid.face)) Sound.play('bid');
  if (s.phase === 'reveal' && prev.phase !== 'reveal') { Sound.play('liar'); setTimeout(() => Sound.play('reveal'), 450); }
  // 떠넘기기: 새로 넘어왔으면 모두에게 크게 알리고, 나에게 넘어왔으면 심장 소리를 다시 시작한다
  if (s.lastPass && s.lastPass.seq !== prev.lastPass?.seq) {
    Sound.play("pass");
    fx.pass = s.lastPass;
    shakeTable();
    setTimeout(() => { fx.pass = null; render(); }, 2600);
  }
  if (s.phase === 'lever' && s.me.canPull && !prev.me.canPull) Sound.startHeart(me?.fallChance || 0.2);
  if (s.phase === 'lever' && !s.me.canPull && prev.phase !== 'lever') Sound.startHeart(0.1);
  if (s.phase === "lever" && prev.me.canPull && !s.me.canPull) Sound.startHeart(0.1); // 내가 떠넘겼다: 구경하는 심장 소리로
  if (s.phase !== 'lever' && prev.phase === 'lever') Sound.stopHeart();
  if (s.phase === 'fall' && prev.phase !== 'fall' && s.lastPull) {
    Sound.stopHeart();
    Sound.play('lever');
    const seat = s.lastPull.seat;
    setTimeout(() => {
      fx.fallKey = fallKey(s);
      if (s.lastPull.fell) { Sound.play('fall'); fx.falling = seat; shakeTable(); }
      else { Sound.play('safe'); fx.saved = seat; }
      render();
      setTimeout(() => { fx.falling = fx.saved = -1; render(); }, 1400);
    }, 600);
  }
  if (s.phase === 'double' && prev.phase !== 'double' && prev.phase !== 'doubleRoll') Sound.play(s.winner === s.mySeat ? 'win' : 'chips');
  if (s.phase === 'doubleRoll' && prev.phase !== 'doubleRoll') { Sound.play('vault'); setTimeout(() => Sound.play(s.vault > 0 ? 'jackpot' : 'lose'), 650); }
  if (s.phase === 'result' && prev.phase !== 'result' && me?.inGame) setTimeout(() => Sound.play(s.winner === s.mySeat && s.vault > 0 ? 'win' : 'lose'), 300);
  const seqs = new Map(prev.seats.map(p => [p.userId, p.emote?.seq]));
  if (s.seats.some(p => p.emote && p.emote.seq !== seqs.get(p.userId))) Sound.play('pop');
  const myTurnNow = s.me.myTurn || s.me.canPull || s.me.canDouble;
  const before = prev.me.myTurn || prev.me.canPull || prev.me.canDouble;
  if (myTurnNow && !before) { Sound.play('turn'); if (navigator.vibrate && !Sound.muted) navigator.vibrate(120); }
  attention(s.me.canPull ? '레버를 당길 차례!' : s.me.myTurn ? '부를 차례!' : s.me.canDouble ? '금고를 고를 차례!' : null);
}

function shakeTable() {
  const t = $('table');
  t.classList.remove('shake'); void t.offsetWidth; t.classList.add('shake');
}

// 다른 탭을 보고 있을 때 내 차례면 탭 제목을 깜빡인다
const baseTitle = document.title;
let attentionText = null, attentionTimer = null;
function attention(text) {
  if (text === attentionText && (attentionTimer || !document.hidden)) return;
  attentionText = text;
  clearInterval(attentionTimer); attentionTimer = null;
  document.title = baseTitle;
  if (!text || !document.hidden) return;
  let on = false;
  attentionTimer = setInterval(() => { on = !on; document.title = on ? '● ' + attentionText : baseTitle; }, 900);
}
document.addEventListener('visibilitychange', () => attention(attentionText));

function renderMute() {
  const b = $('muteBtn');
  b.textContent = Sound.muted ? '🔇 소리 꺼짐' : '🔊 소리 켜짐';
  b.setAttribute('aria-pressed', String(!Sound.muted));
}
$('muteBtn').addEventListener('click', () => { Sound.toggle(); renderMute(); });
renderMute();

// ---------- 로비 ----------

$('createForm').addEventListener('submit', e => { e.preventDefault(); invoke('CreateRoom', $('roomName').value.trim()); });
$("quickBtn").addEventListener("click", () => invoke("QuickStart"));
$('rooms').addEventListener('click', e => { const b = e.target.closest('[data-join]'); if (b) invoke('JoinRoom', b.dataset.join); });
$('rescueBtn').addEventListener('click', async () => {
  try { profile = await api('/api/rescue', {}); render(); } catch (err) { showError(err.message); }
});

// ---------- 테이블 조작 ----------

$('leaveBtn').addEventListener('click', () => {
  const me = state?.seats[state.mySeat];
  if (me?.inGame && me.alive && state.phase !== 'result' && !confirm('게임 중에 나가면 탈락으로 칩니다(참가비는 잃습니다). 나갈까요?')) return;
  invoke('LeaveRoom');
});
$('emotes').addEventListener('click', e => { const b = e.target.closest('[data-emote]'); if (b) invoke('Emote', +b.dataset.emote); });

document.addEventListener('click', e => {
  const b = e.target.closest('[data-act]');
  if (!b || b.disabled || !state) return;
  const act = b.dataset.act;
  if (act === 'qty-') { pickQty = Math.max(1, pickQty - 1); fixFace(); render(); }
  else if (act === 'qty+') { pickQty = Math.min(state.diceInPlay, pickQty + 1); fixFace(); render(); }
  else if (act === 'face') { pickFace = +b.dataset.face; render(); }
  else if (act === 'bid') invoke('Bid', pickQty, pickFace);
  else if (act === 'liar') invoke('Challenge');
  else if (act === 'pull') { Sound.stopHeart(); invoke('Pull'); }
  else if (act === "pass") invoke("Pass", state.seats[+b.dataset.seat].userId);
  else if (act === 'side-target') { sideTarget = +b.dataset.seat; render(); }
  else if (act === 'side') invoke('SideBet', state.seats[sideTarget].userId, +b.dataset.amount);
  else if (act === 'double') invoke('Double');
  else if (act === 'cash') invoke('Cash');
});

/** 부르기가 앞보다 높은지(개수가 많거나, 같은 개수에 더 큰 눈). */
function beats(q, f, bid) { return !bid || q > bid.quantity || (q === bid.quantity && f > bid.face); }
/** 고른 개수에서 고를 수 있는 가장 작은 눈으로 맞춘다. */
function fixFace() {
  if (beats(pickQty, pickFace, state.bid)) return;
  for (let f = 2; f <= 6; f++) if (beats(pickQty, f, state.bid)) { pickFace = f; return; }
}

// ---------- 그리기 ----------

function dieFace(v) {
  const dots = (PIPS[v] || []).map(i => `<circle cx="${20 + (i % 3) * 30}" cy="${20 + Math.floor(i / 3) * 30}" r="10.5"/>`).join('');
  return `<svg viewBox="0 0 100 100" aria-hidden="true">${dots}</svg>`;
}
function die(v, cls = 'sm', extra = '') { return `<span class="die ${cls} ${extra}" aria-label="${v}">${dieFace(v)}</span>`; }
function hatIcon(id) { return id && gachaCfg ? (gachaCfg.items.find(i => i.id === id)?.icon || '') : ''; }
function avatar(p) {
  return `<span class="avatar-wrap">${Animals.face(p.character, 'avatar')}${p.hat ? `<span class="hat" aria-label="꾸미기">${hatIcon(p.hat)}</span>` : ''}</span>`;
}

let bubbleTimer = null;
function bubble(p) {
  if (!p?.emote) return '';
  const age = p.emote.ago + (Date.now() - stateAt);
  if (age >= 3000) return '';
  clearTimeout(bubbleTimer);
  bubbleTimer = setTimeout(render, 3000 - age + 50);
  return `<span class="bubble" style="animation-delay:-${age}ms" aria-live="polite">${esc(p.emote.text)}</span>`;
}

function render() {
  const loggedIn = !!(token && profile);
  $('auth').hidden = loggedIn;
  $('lobby').hidden = !loggedIn || !!state;
  $('game').hidden = !loggedIn || !state;
  $('stats').innerHTML = loggedIn ? [
    ['계정', `<span class="who">${Animals.face(profile.character, 'avatar sm')}${esc(profile.name)}</span>`], ['사이버머니', fmt(profile.money)],
  ].map(([k, v]) => `<div class="stat"><span>${k}</span><b class="num">${v}</b></div>`).join('') : '';
  if (!loggedIn) return;
  if (!state) { attention(null); renderLobby(); } else renderGame();
}

function renderLobby() {
  const canPlay = profile.money >= 50000;
  const rb = $('rescueBtn');
  rb.hidden = !profile.canRescue;
  rb.innerHTML = '구제 머니 받기<small>하루 한 번</small>';
  $('lobbyNote').textContent = '방에 앉으면 사이버머니 50,000을 테이블로 가져가고, 게임마다 참가비 5,000을 냅니다. 일어나면 남은 만큼 돌려받습니다.'
    + (canPlay ? '' : ' 지금은 사이버머니가 모자랍니다.');
  $('createForm').querySelector('button').disabled = !canPlay;
  $("quickBtn").disabled = !canPlay;
  $('rooms').innerHTML = rooms.length ? rooms.map(r => `
    <li><div><strong>${esc(r.name)}</strong> <span class="pill num">사람 ${r.players}명${r.bots ? ` · 봇 ${r.bots}` : ""}</span>
      <span class="pill ${r.phase === 'waiting' ? '' : 'red'}">${r.phase === 'waiting' ? '대기 중' : '게임 중'}</span>
      <span class="note num">참가비 ${fmt(r.ante)} · 바이인 ${fmt(r.buyIn)}</span></div>
      <button class="btn primary" data-join="${esc(r.id)}" ${!canPlay || r.players >= r.maxPlayers ? 'disabled' : ''}>앉기</button></li>`).join('')
    : '<li class="note">아직 방이 없습니다. 방을 만들고 친구에게 이 주소를 보내세요.</li>';
}

function renderGame() {
  const s = state, mine = s.seats[s.mySeat], me = s.me;
  const em = $('emotes'), emKey = s.room.emotes.join('|');
  if (em.dataset.key !== emKey) {
    em.dataset.key = emKey;
    em.innerHTML = s.room.emotes.map((t, i) => `<button class="btn" data-emote="${i}" aria-label="감정 표현: ${esc(t)}">${esc(t)}</button>`).join('');
  }
  $('roomTitle').textContent = `${s.room.name} · ${s.gameNo ? s.gameNo + '번째 게임' : '시작 전'}${s.round ? ` · ${s.round}판` : ''} · 판돈 ${fmt(s.pot || s.vault)}`;

  const open = s.phase === 'reveal' || s.phase === 'lever' || s.phase === 'fall';
  $('seats').innerHTML = s.seats.map((p, i) => i === s.mySeat ? '' : seatCard(p, i, open)).join('')
    || '<p class="note">친구를 기다리는 중입니다. 2명이 되면 시작합니다.</p>';
  $('center').innerHTML = centerView(s);
  $('me').className = 'me' + (me.myTurn || me.canPull || me.canDouble ? ' turn' : '');
  $('me').innerHTML = myView(s, mine, me);
  $('log').innerHTML = s.log.map(l => `<li>${esc(l)}</li>`).join('');
}

function seatCard(p, i, open) {
  const s = state;
  const cls = ['seat', p.turn ? 'turn' : '', s.lever === i ? 'lever' : '', p.inGame && !p.alive ? 'dead' : '',
    fx.falling === i ? 'falling' : '', fx.saved === i ? 'saved' : '', s.winner === i && (s.phase === 'double' || s.phase === 'doubleRoll' || s.phase === 'result') ? 'winner' : ''].join(' ');
  const dice = p.dice ? `<div class="mini">${p.dice.map(v => die(v, 'sm', diceClass(v))).join('')}</div>`
    : p.inGame && p.alive ? `<div class="cup ${fx.shakeCups ? 'shaking' : ''}" aria-label="컵 속 주사위 5개"></div>` : '';
  const chance = p.inGame && p.alive ? `<span class="chance ${p.fallChance >= 0.34 ? 'hot' : ''}">레버 ${p.pulls}번 · 다음 ${pct(p.fallChance)}</span>`
    : p.inGame ? '<span class="pill red">풍덩! 탈락</span>' : '<span class="pill">다음 게임부터</span>';
  const side = p.side ? `<span class="side">관전 베팅: ${esc(s.seats[p.side.target]?.name)}에 ${fmt(p.side.amount)} (${p.side.odds}배)</span>` : '';
  return `<div class="${cls}">${bubble(p)}${avatar(p)}
    <span class="name">${esc(p.name)}${p.isBot ? ' <span class="pill">봇</span>' : ""}</span><span class="stack num">${fmt(p.stack)}</span>
    ${!p.connected ? '<span class="pill">연결 끊김</span>' : ''}${chance}${dice}${side}</div>`;
}

/** 컵을 열었을 때: 부른 눈은 금색, 1(만능)은 분홍. */
function diceClass(v) {
  const r = state.reveal;
  if (!r) return '';
  return v === r.face ? 'match' : v === 1 ? 'wild' : '';
}

/** 떠넘기기 카드: 살아 있는 다른 사람마다 버튼(그 사람의 떨어질 확률과 함께). */
function passButtons(s) {
  const targets = s.seats.map((p, i) => ({ p, i })).filter(x => x.i !== s.mySeat && x.p.inGame && x.p.alive);
  return `<div class="pass"><span class="note">떠넘기기 카드 · ${fmt(s.me.passCost)}을 내고 레버를 넘긴다(낸 돈은 판돈으로, 다시 넘기면 두 배)</span>
    <div class="controls">${targets.map(x => `<button class="btn" data-act="pass" data-seat="${x.i}">😈 ${esc(x.p.name)}에게<small>그 사람 확률 ${pct(x.p.fallChance)}</small></button>`).join("")}</div></div>`;
}

function name(i) { return esc(state.seats[i]?.name ?? '?'); }

function centerView(s) {
  switch (s.phase) {
    case 'waiting': return '<div class="callout">친구를 기다리는 중<small>2명 이상 모이면 시작합니다 · 방 주소를 보내 주세요</small></div>';
    case 'countdown': return '<div class="callout">곧 시작합니다!<small>모두 참가비를 내고 컵을 흔듭니다</small></div>';
    case 'bid': {
      const turn = s.seats.findIndex(p => p.turn);
      return s.bid
        ? `<div class="callout">${name(s.bid.by)}: ${die(s.bid.face, 'md')} 눈이 <b class="num">${s.bid.quantity}</b>개 이상!
            <small>테이블 주사위 ${s.diceInPlay}개 · 1은 만능 · 다음은 ${name(turn)} 차례</small></div>`
        : `<div class="callout">${name(turn)}의 첫 부르기<small>테이블 주사위 ${s.diceInPlay}개 · 1은 만능</small></div>`;
    }
    case "reveal":
    case "lever": {
      const r = s.reveal;
      if (!r) return "";
      if (fx.pass) return `<div class="boom fell">떠넘겼다!</div><div class="callout"><small>${name(fx.pass.from)}이(가) ${fmt(fx.pass.cost)}을 내고 레버를 ${name(fx.pass.to)}에게 넘겼다</small></div>`;
      const verdict = r.bidTrue
        ? `<div class="verdict truth">진짜였다! ${name(r.challenger)}의 헛짚기</div>`
        : `<div class="verdict liar">거짓말 들통! ${name(r.bidder)}</div>`;
      let lever = '';
      if (s.phase === 'lever' && s.lever >= 0) {
        const p = s.seats[s.lever];
        lever = `<div class="lever-box">
          <span>${name(s.lever)}이(가) 함정 레버를 잡았다…</span>
          <div class="odds">${pct(p.fallChance)}<small>떨어질 확률 (${p.pulls + 1}번째 당김)</small></div>
          <div class="chambers" aria-hidden="true">${Array.from({ length: 6 }, (_, k) => `<i class="${k < p.pulls ? 'used' : ''}"></i>`).join('')}</div>
          ${s.me.canPull ? `<button class="btn red big" data-act="pull">함정 레버 당기기</button>` : ``}
          ${s.me.canPass ? passButtons(s) : ``}</div>`;
      }
      return `<div class="callout"><small>${name(r.challenger)}: "거짓말!"</small>
          ${die(r.face, 'md')} 눈(1 포함) <span class="big-num num">${r.count}</span> / 부른 개수 ${r.quantity}</div>${verdict}${lever}`;
    }
    case 'fall': {
      const l = s.lastPull;
      if (!l) return '';
      if (fx.fallKey !== fallKey(s)) return `<div class="callout">철컥…<small>${name(l.seat)}이(가) 레버를 당겼다</small></div>`;
      return l.fell
        ? `<div class="boom fell">풍덩!</div><div class="callout"><small>${name(l.seat)} 탈락 · ${pct(l.chance)} 확률이었다</small></div>`
        : `<div class="boom safe">살았다!</div><div class="callout"><small>${name(l.seat)} · ${l.pullNo}번째 레버 · 떨어질 확률 ${pct(l.chance)}이었다</small></div>`;
    }
    case 'double': {
      const mineWin = s.winner === s.mySeat;
      return `<div class="callout">${name(s.winner)} 최후의 생존자!</div><div class="vault num">${fmt(s.vault)}</div>
        <div class="callout"><small>금고 주사위: ${s.room.doubleWinFrom}~6이면 두 배, 1~${s.room.doubleWinFrom - 1}이면 전부 잃음 · 더블 ${s.doubles}/${s.room.maxDoubles}번</small></div>
        ${mineWin ? `<div class="controls"><button class="btn red big" data-act="double">🎲 금고 주사위<small>${fmt(s.vault * 2)} 노리기</small></button>
          <button class="btn primary big" data-act="cash">챙기기<small>${fmt(s.vault)} 확정</small></button></div>` : `<p class="hint">${name(s.winner)}이(가) 고민 중…</p>`}`;
    }
    case 'doubleRoll':
      return `<div class="vault-die">${die(s.vaultRoll, 'lg')}</div>
        <div class="boom ${s.vault > 0 ? 'safe' : 'fell'}">${s.vault > 0 ? '두 배!' : '꽝!'}</div><div class="vault num">${fmt(s.vault)}</div>`;
    case 'result':
      return s.banner ? `<div class="banner ${s.vault > 0 ? '' : 'lost'}">${esc(s.banner)}</div><p class="hint">곧 다음 게임이 시작됩니다</p>` : '';
  }
  return '';
}

function myView(s, mine, me) {
  if (!mine) return '';
  const head = `${bubble(mine)}<div class="row">${avatar(mine)}<strong class="title">${esc(mine.name)} (나)</strong>
    <span class="num">${fmt(mine.stack)}</span>
    ${mine.inGame && mine.alive ? `<span class="chance ${mine.fallChance >= 0.34 ? 'hot' : ''}">레버 ${mine.pulls}번 · 다음 ${pct(mine.fallChance)}</span>` : ''}
    ${mine.inGame && !mine.alive ? '<span class="pill red">탈락 · 관전 중</span>' : ''}</div>`;
  const dice = mine.dice ? `<div class="my-dice">${mine.dice.map(v => die(v, 'lg', diceClass(v))).join('')}</div>` : '';
  let body = '', hint = '';

  if (me.myTurn) {
    const key = `${s.gameNo}:${s.round}:${s.bid?.quantity}:${s.bid?.face}`;
    if (key !== pickKey) { pickKey = key; pickQty = me.minBid.quantity; pickFace = me.minBid.face; }
    const ok = beats(pickQty, pickFace, s.bid);
    // 도움말: 내 주사위로 본 기대 개수(내 것 + 남의 주사위 × 1/3)
    const mineCount = (mine.dice || []).filter(v => v === pickFace || v === 1).length;
    const expect = mineCount + (s.diceInPlay - 5) / 3;
    body = `<div class="picker">
      <div class="qty"><button class="btn" data-act="qty-" aria-label="개수 줄이기" ${pickQty <= 1 ? 'disabled' : ''}>−</button>
        <b class="num" aria-label="개수">${pickQty}개</b>
        <button class="btn" data-act="qty+" aria-label="개수 늘리기" ${pickQty >= s.diceInPlay ? 'disabled' : ''}>+</button></div>
      <div class="faces" role="group" aria-label="눈 고르기">${[2, 3, 4, 5, 6].map(f => `<button class="face" data-act="face" data-face="${f}" aria-pressed="${f === pickFace}"
        aria-label="${f} 눈" ${beats(pickQty, f, s.bid) ? '' : 'disabled'}>${dieFace(f)}</button>`).join('')}</div>
      <div class="controls">
        <button class="btn primary big" data-act="bid" ${ok ? '' : 'disabled'}>"${pickFace} 눈 ${pickQty}개 이상!"</button>
        ${me.canChallenge ? '<button class="btn red big" data-act="liar">거짓말!</button>' : ''}</div></div>`;
    hint = `내 주사위로 보면 ${pickFace} 눈(1 포함)은 내 것 ${mineCount}개 + 남의 것 약 ${((s.diceInPlay - 5) / 3).toFixed(1)}개 ≈ ${expect.toFixed(1)}개`;
  } else if (me.canPull) {
    hint = `위의 레버를 당기세요 · 떨어질 확률 ${pct(mine.fallChance)} · 시간이 지나면 자동으로 당겨집니다.`;
  } else if (me.canSideBet) {
    const alive = s.seats.map((p, i) => ({ p, i })).filter(x => x.p.inGame && x.p.alive);
    if (!alive.some(x => x.i === sideTarget)) sideTarget = alive[0]?.i ?? -1;
    body = `<div class="picker"><strong class="title">관전 베팅 · 누가 살아남을까?</strong>
      <div class="controls">${alive.map(x => `<button class="btn ${x.i === sideTarget ? 'primary' : ''}" data-act="side-target" data-seat="${x.i}">${esc(x.p.name)}</button>`).join('')}</div>
      <div class="controls">${s.room.sideBets.map(a => `<button class="btn red" data-act="side" data-amount="${a}" ${mine.stack < a || sideTarget < 0 ? 'disabled' : ''}>${fmt(a)} 걸기<small>맞히면 ${fmt(a * alive.length)} (${alive.length}배)</small></button>`).join('')}</div></div>`;
  } else if (mine.side) {
    hint = `관전 베팅: ${esc(s.seats[mine.side.target]?.name)}에게 ${fmt(mine.side.amount)} · 맞히면 ${fmt(mine.side.amount * mine.side.odds)}`;
  } else if (s.phase === 'bid' && mine.inGame && mine.alive) {
    hint = '내 차례를 기다리는 중… 남이 부르는 숫자가 진짜일지 생각해 보세요.';
  } else if (!mine.inGame && s.phase !== 'waiting' && s.phase !== 'countdown') {
    hint = '이번 게임이 끝나면 함께합니다.';
  }
  return head + dice + body + (hint ? `<p class="hint">${hint}</p>` : '');
}

// 남은 시간 막대
setInterval(() => {
  const bar = $('timerBar');
  if (!state || !state.deadline) { bar.style.width = '0'; return; }
  const left = state.deadline - (Date.now() + clockOffset);
  bar.style.width = Math.max(0, Math.min(100, left / (state.phaseMs || 1) * 100)) + '%';
}, 200);

// ---------- 가챠 ----------

$('gachaBtn').addEventListener('click', async () => {
  $('gacha').showModal();
  $('gachaResult').innerHTML = '';
  try { inventory = await api('/api/gacha/me'); } catch (err) { $('gachaNote').textContent = err.message; }
  renderGacha();
});
$('gachaClose').addEventListener('click', () => $('gacha').close());
$('gachaPulls').addEventListener('click', async e => {
  const b = e.target.closest('[data-pull]');
  if (!b || b.disabled) return;
  b.disabled = true;
  try {
    const res = await api('/api/gacha/pull', { count: +b.dataset.pull });
    inventory = res.inventory;
    profile.money = res.inventory.money;
    showCapsules(res.results);
  } catch (err) { $('gachaNote').textContent = err.message; }
  renderGacha();
  render();
});
$('inventory').addEventListener('click', async e => {
  const b = e.target.closest('[data-item]');
  if (!b) return;
  const item = b.getAttribute('aria-pressed') === 'true' ? '' : b.dataset.item;
  try { inventory = await api('/api/gacha/equip', { item }); } catch (err) { $('gachaNote').textContent = err.message; }
  renderGacha();
});

function rarity(id) { return gachaCfg.rarities.find(r => r.id === id) || { name: id, color: '#888' }; }
const tierOf = id => Math.max(0, gachaCfg.rarities.findIndex(r => r.id === id));

function showCapsules(results) {
  const el = $('gachaResult');
  // 희귀한 것은 마지막에 열리게 정렬하고, 하나씩 늦게 튀어나오게 한다
  const sorted = results.slice().sort((a, b) => tierOf(a.rarity) - tierOf(b.rarity));
  el.innerHTML = sorted.map((r, k) => {
    const ra = rarity(r.rarity);
    return `<div class="capsule ${r.rarity}" style="--rc:${ra.color};animation-delay:${k * 0.18}s"><span class="icon">${r.icon}</span><span class="nm">${esc(r.name)}</span><span class="nm" style="color:${ra.color}">${esc(ra.name)}</span></div>`;
  }).join('');
  sorted.forEach((r, k) => setTimeout(() => Sound.play('capsule', tierOf(r.rarity)), k * 180));
}

function renderGacha() {
  if (!gachaCfg) return;
  const money = inventory?.money ?? profile?.money ?? 0;
  $('gachaNote').textContent = (gachaCfg.placeholder ? '※ 확률과 보상은 아직 임시 값입니다. ' : '')
    + `사이버머니로만 뽑습니다(현금 결제 없음). 지금 머니 ${fmt(money)} · 테이블에 앉아 있으면 뽑을 수 없습니다.`;
  $('gachaPulls').innerHTML = `<button class="btn primary" data-pull="1" ${money < gachaCfg.cost ? 'disabled' : ''}>1번 뽑기<small>${fmt(gachaCfg.cost)}</small></button>
    <button class="btn red" data-pull="10" ${money < gachaCfg.tenPullCost ? 'disabled' : ''}>10번 뽑기<small>${fmt(gachaCfg.tenPullCost)}</small></button>`;
  $('gachaOdds').innerHTML = gachaCfg.items.map(i => {
    const ra = rarity(i.rarity);
    return `<li><span>${i.icon} ${esc(i.name)} <span style="color:${ra.color}">${esc(ra.name)}</span></span><span class="num">${(i.chance * 100).toFixed(i.chance < 0.1 ? 1 : 0)}%</span></li>`;
  }).join('');
  const have = inventory?.items || [];
  $('inventory').innerHTML = have.length ? have.map(h => {
    const it = gachaCfg.items.find(i => i.id === h.id);
    return it ? `<button data-item="${h.id}" aria-pressed="${h.equipped}"><span class="icon">${it.icon}</span><span>${esc(it.name)}</span><span class="note">×${h.count}</span></button>` : '';
  }).join('') : '<p class="note">아직 없습니다.</p>';
}

// ---------- 시작 ----------

(async () => {
  try { gachaCfg = await (await fetch('/api/gacha')).json(); } catch {}
  if (token) {
    try { profile = await api('/api/me'); await connect(); }
    catch { token = null; }
  }
  render();
})();
