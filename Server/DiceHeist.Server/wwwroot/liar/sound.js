// 라이어 다이스 효과음: 녹음 파일 없이 WebAudio로 합성한다(멀티 화면 sound.js와 같은 방식에 긴장 소리를 더했다).
// 브라우저는 사람이 한 번 누르기 전에는 소리를 막으므로, 첫 클릭·키 입력 때 오디오를 깨운다.
const Sound = (() => {
  let ctx = null, master = null, noise = null, heart = null;
  let muted = false;
  try { muted = localStorage.getItem('dh.mute') === '1'; } catch {}

  function wake() {
    if (!ctx) {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      ctx = new AC();
      master = ctx.createGain();
      master.gain.value = 0.5;
      master.connect(ctx.destination);
      noise = ctx.createBuffer(1, ctx.sampleRate * 1.0, ctx.sampleRate);
      const d = noise.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
    }
    if (ctx.state === 'suspended') ctx.resume();
  }
  ['pointerdown', 'keydown'].forEach(e => addEventListener(e, wake, { capture: true }));
  const ready = () => !muted && ctx && ctx.state === 'running';

  function click(at, freq, gain, len = 0.04, q = 3) {
    const src = ctx.createBufferSource(); src.buffer = noise;
    const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = freq; bp.Q.value = q;
    const g = ctx.createGain();
    g.gain.setValueAtTime(gain, at);
    g.gain.exponentialRampToValueAtTime(0.001, at + len);
    src.connect(bp).connect(g).connect(master);
    src.start(at, Math.random() * 0.5, len + 0.05);
  }

  function tone(at, freq, len, gain, type = 'sine', endFreq) {
    const o = ctx.createOscillator(); o.type = type;
    o.frequency.setValueAtTime(freq, at);
    if (endFreq) o.frequency.exponentialRampToValueAtTime(endFreq, at + len);
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.0001, at);
    g.gain.exponentialRampToValueAtTime(gain, at + 0.01);
    g.gain.exponentialRampToValueAtTime(0.0001, at + len);
    o.connect(g).connect(master);
    o.start(at); o.stop(at + len + 0.02);
  }

  const play = {
    // 컵 속 주사위 달그락
    shake() {
      let t = ctx.currentTime + 0.02;
      for (let i = 0; i < 14; i++) { click(t, 900 + Math.random() * 900, 0.25, 0.03, 2); t += 0.025 + Math.random() * 0.03; }
    },
    // 부르기: 탁자 두드리는 "톡"
    bid() { const t = ctx.currentTime + 0.01; tone(t, 520, 0.09, 0.15, 'triangle'); click(t, 300, 0.3, 0.05); },
    // "거짓말!": 짧은 경적
    liar() { const t = ctx.currentTime + 0.01; tone(t, 330, 0.35, 0.18, 'sawtooth', 220); tone(t, 440, 0.35, 0.1, 'square', 290); },
    // 컵 여는 소리
    reveal() { const t = ctx.currentTime + 0.01; click(t, 700, 0.4, 0.1); click(t + 0.06, 1500, 0.25); },
    turn() { const t = ctx.currentTime + 0.01; tone(t, 880, 0.18, 0.12, 'triangle'); tone(t + 0.13, 1320, 0.3, 0.12, 'triangle'); },
    // 레버 "철컥"
    lever() { const t = ctx.currentTime + 0.01; click(t, 2400, 0.6, 0.03, 8); click(t + 0.05, 600, 0.7, 0.12, 4); tone(t + 0.05, 90, 0.25, 0.3, 'sine', 50); },
    // 함정이 열려 풍덩
    fall() {
      const t = ctx.currentTime + 0.02;
      tone(t, 700, 0.6, 0.2, 'sine', 90);
      const src = ctx.createBufferSource(); src.buffer = noise;
      const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.setValueAtTime(3000, t + 0.5); lp.frequency.exponentialRampToValueAtTime(300, t + 1.4);
      const g = ctx.createGain(); g.gain.setValueAtTime(0.0001, t + 0.5); g.gain.exponentialRampToValueAtTime(0.6, t + 0.55); g.gain.exponentialRampToValueAtTime(0.001, t + 1.4);
      src.connect(lp).connect(g).connect(master); src.start(t + 0.5, 0, 1);
    },
    // 살았다
    safe() { const t = ctx.currentTime + 0.02; [660, 880, 1320].forEach((f, i) => tone(t + i * 0.07, f, 0.25, 0.1, 'triangle')); },
    win() { const t = ctx.currentTime + 0.02; [523, 659, 784, 1047, 1319].forEach((f, i) => tone(t + i * 0.09, f, 0.4, 0.12, 'triangle')); },
    lose() { const t = ctx.currentTime + 0.02; tone(t, 392, 0.25, 0.1, 'triangle'); tone(t + 0.18, 294, 0.45, 0.1, 'triangle'); },
    pop() { const t = ctx.currentTime + 0.01; tone(t, 660, 0.08, 0.08, 'sine', 990); },
    chips() { const t = ctx.currentTime + 0.02; for (let i = 0; i < 4; i++) tone(t + i * 0.05, 2600 + Math.random() * 900, 0.09, 0.07, 'triangle'); },
    // 금고 주사위 굴리기와 결과
    vault() { let t = ctx.currentTime + 0.02; for (let i = 0; i < 10; i++) { click(t, 1600 + Math.random() * 1200, 0.4 * (1 - i / 10) + 0.1); t += 0.04 + i * 0.012; } },
    jackpot() { const t = ctx.currentTime + 0.02; for (let i = 0; i < 8; i++) tone(t + i * 0.06, 1200 + i * 180, 0.2, 0.09, 'triangle'); },
    // 가챠 캡슐: 희귀할수록 높고 길게
    capsule(tier = 0) { const t = ctx.currentTime + 0.01; tone(t, 500 + tier * 220, 0.18 + tier * 0.08, 0.12, 'triangle', 800 + tier * 400); },
  };

  // 레버 앞 심장 소리: 떨어질 확률이 높을수록 빠르게. stopHeart로 멈춘다
  function startHeart(chance) {
    stopHeart();
    if (!ready()) return;
    const beat = Math.max(0.35, 0.9 - chance * 0.6);
    const loop = () => {
      if (!ready()) return;
      const t = ctx.currentTime + 0.01;
      tone(t, 60, 0.12, 0.5, 'sine', 40);
      tone(t + 0.16, 55, 0.14, 0.35, 'sine', 38);
      heart = setTimeout(loop, beat * 1000);
    };
    loop();
  }
  function stopHeart() { clearTimeout(heart); heart = null; }

  return {
    play(name, ...args) { if (ready() && play[name]) play[name](...args); },
    startHeart, stopHeart,
    get muted() { return muted; },
    toggle() {
      muted = !muted;
      try { localStorage.setItem('dh.mute', muted ? '1' : '0'); } catch {}
      if (muted) stopHeart(); else { wake(); if (ready()) play.chips(); }
      return muted;
    },
  };
})();
