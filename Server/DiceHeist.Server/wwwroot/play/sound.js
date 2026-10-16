// 효과음: 녹음 파일 없이 WebAudio로 합성한다(Unity의 DiceAudio와 같은 생각).
// 브라우저는 사람이 한 번 누르기 전에는 소리를 막으므로, 첫 클릭·키 입력 때 오디오를 깨운다.
const Sound = (() => {
  let ctx = null, master = null, noise = null;
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
      // 딸깍 소리에 쓸 흰 잡음 0.2초
      noise = ctx.createBuffer(1, ctx.sampleRate * 0.2, ctx.sampleRate);
      const d = noise.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
    }
    if (ctx.state === 'suspended') ctx.resume();
  }
  ['pointerdown', 'keydown'].forEach(e => addEventListener(e, wake, { capture: true }));

  const ready = () => !muted && ctx && ctx.state === 'running';

  // 짧은 잡음을 대역 필터로 걸러 딱딱한 것이 부딪는 소리
  function click(at, freq, gain, len = 0.04) {
    const src = ctx.createBufferSource(); src.buffer = noise;
    const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = freq; bp.Q.value = 3;
    const g = ctx.createGain();
    g.gain.setValueAtTime(gain, at);
    g.gain.exponentialRampToValueAtTime(0.001, at + len);
    src.connect(bp).connect(g).connect(master);
    src.start(at, Math.random() * 0.1, len);
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
    // 주사위가 굴러 여러 번 부딪고 멈춘다(뒤로 갈수록 작고 촘촘하게)
    dice(count = 5) {
      let t = ctx.currentTime + 0.02;
      const hits = 5 + count;
      for (let i = 0; i < hits; i++) {
        click(t, 1800 + Math.random() * 1600, 0.5 * (1 - i / hits) + 0.08);
        t += 0.03 + Math.random() * 0.07 * (1 - i / hits);
      }
    },
    // 칩 몇 개가 쌓이는 쨍그랑
    chips() {
      const t = ctx.currentTime + 0.02;
      for (let i = 0; i < 3; i++) {
        const at = t + i * 0.055 + Math.random() * 0.02;
        tone(at, 2600 + Math.random() * 900, 0.09, 0.07, 'triangle');
        click(at, 5000, 0.18, 0.02);
      }
    },
    fold() { tone(ctx.currentTime + 0.01, 180, 0.18, 0.18, 'sine', 70); },
    // 상대 컵을 들어 주사위를 공개
    reveal() { const t = ctx.currentTime + 0.01; click(t, 700, 0.35, 0.08); click(t + 0.05, 1400, 0.2); },
    turn() { const t = ctx.currentTime + 0.01; tone(t, 880, 0.18, 0.12, 'triangle'); tone(t + 0.13, 1320, 0.3, 0.12, 'triangle'); },
    win() { const t = ctx.currentTime + 0.02; [523, 659, 784, 1047].forEach((f, i) => tone(t + i * 0.09, f, 0.35, 0.12, 'triangle')); },
    lose() { const t = ctx.currentTime + 0.02; tone(t, 392, 0.25, 0.1, 'triangle'); tone(t + 0.18, 294, 0.45, 0.1, 'triangle'); },
  };

  return {
    play(name, ...args) { if (ready() && play[name]) play[name](...args); },
    get muted() { return muted; },
    toggle() {
      muted = !muted;
      try { localStorage.setItem('dh.mute', muted ? '1' : '0'); } catch {}
      if (!muted) { wake(); if (ready()) play.chips(); }
      return muted;
    },
  };
})();
