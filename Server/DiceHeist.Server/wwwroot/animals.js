// 동물 캐릭터 얼굴(SVG). Unity의 AnimalBuilder(기본 도형 조립)와 같은 색·생김새를 정면에서 본 모양으로 그린다:
// 둥근 머리, 분홍 볼, 눈에 흰 반짝임. 겉모습만 다르고 게임에는 아무 영향이 없다.
const Animals = (() => {
  const C = {
    black: '#1f2124', white: '#faf7f0', pink: '#f5adb8',
  };
  const NAMES = { rabbit: '토끼', raccoon: '너구리', cat: '고양이', fox: '여우', bear: '곰', penguin: '펭귄' };

  const head = (fill, line) => `<ellipse cx="50" cy="58" rx="34" ry="30" fill="${fill}"${line ? ` stroke="${line}" stroke-width="1.5"` : ''}/>`;
  const cheeks = `<ellipse cx="27" cy="68" rx="7" ry="4" fill="${C.pink}" opacity=".9"/><ellipse cx="73" cy="68" rx="7" ry="4" fill="${C.pink}" opacity=".9"/>`;
  const eyes = (iris, r = 5, dy = 0) => [36, 64].map(x =>
    `<circle cx="${x}" cy="${56 + dy}" r="${r}" fill="${iris}"/><circle cx="${x + 1.6}" cy="${54.2 + dy}" r="${r * 0.36}" fill="${C.white}"/>`).join('');

  const draw = {
    // 길쭉한 귀(안쪽 분홍), 흰 꼬리는 얼굴 그림에서 안 보인다
    rabbit: () => {
      const fur = '#f7f0e0';
      return `<g transform="rotate(-9 38 30)"><rect x="30" y="2" width="15" height="38" rx="7.5" fill="${fur}" stroke="#e3d8c4" stroke-width="1.5"/><rect x="34" y="7" width="7" height="28" rx="3.5" fill="${C.pink}"/></g>
        <g transform="rotate(9 62 30)"><rect x="55" y="2" width="15" height="38" rx="7.5" fill="${fur}" stroke="#e3d8c4" stroke-width="1.5"/><rect x="59" y="7" width="7" height="28" rx="3.5" fill="${C.pink}"/></g>
        ${head(fur, '#e3d8c4')}${cheeks}${eyes(C.black)}<ellipse cx="50" cy="63" rx="4" ry="2.8" fill="${C.pink}"/>`;
    },
    // 둥근 검은 귀, 눈가 검은 가면, 흰 주둥이
    raccoon: () => {
      const fur = '#8f8780', dark = '#333036';
      return `<circle cx="24" cy="34" r="10" fill="${dark}"/><circle cx="76" cy="34" r="10" fill="${dark}"/>
        ${head(fur)}
        <ellipse cx="34" cy="56" rx="13" ry="8" fill="${dark}" transform="rotate(14 34 56)"/><ellipse cx="66" cy="56" rx="13" ry="8" fill="${dark}" transform="rotate(-14 66 56)"/>
        <ellipse cx="50" cy="70" rx="14" ry="10" fill="${C.white}"/>${cheeks}
        ${[36, 64].map(x => `<circle cx="${x}" cy="56" r="5" fill="${C.white}"/><circle cx="${x}" cy="56.5" r="2.6" fill="${C.black}"/>`).join('')}
        <ellipse cx="50" cy="65" rx="4.4" ry="3" fill="${C.black}"/>`;
    },
    // 세모 귀(안쪽 분홍), 연두 눈에 세로 동공, 흰 수염
    cat: () => {
      const fur = '#73809e';
      return `<path d="M20 44 L24 14 L44 32 Z" fill="${fur}"/><path d="M25 38 L27 21 L38 32 Z" fill="${C.pink}"/>
        <path d="M80 44 L76 14 L56 32 Z" fill="${fur}"/><path d="M75 38 L73 21 L62 32 Z" fill="${C.pink}"/>
        ${head(fur)}${cheeks}
        ${[36, 64].map(x => `<circle cx="${x}" cy="56" r="5.4" fill="#ccdb59"/><ellipse cx="${x}" cy="56" rx="1.2" ry="4.2" fill="${C.black}"/><circle cx="${x + 1.8}" cy="54" r="1.5" fill="${C.white}"/>`).join('')}
        <path d="M47 63 h6 l-3 3 z" fill="${C.pink}"/>
        <g stroke="${C.white}" stroke-width="1.3" stroke-linecap="round">
          <path d="M30 66 L12 63"/><path d="M30 69 L12 70"/><path d="M30 72 L13 76"/><path d="M70 66 L88 63"/><path d="M70 69 L88 70"/><path d="M70 72 L87 76"/></g>`;
    },
    // 길쭉한 세모 귀(끝이 짙음), 흰 볼과 주둥이
    fox: () => {
      const fur = '#e07a3a', tip = '#3a2a26';
      return `<path d="M18 46 L22 8 L44 32 Z" fill="${fur}"/><path d="M20 26 L22 8 L31 18 Z" fill="${tip}"/><path d="M25 38 L26 20 L37 32 Z" fill="${C.white}"/>
        <path d="M82 46 L78 8 L56 32 Z" fill="${fur}"/><path d="M80 26 L78 8 L69 18 Z" fill="${tip}"/><path d="M75 38 L74 20 L63 32 Z" fill="${C.white}"/>
        ${head(fur)}
        <ellipse cx="27" cy="70" rx="10" ry="8" fill="${C.white}"/><ellipse cx="73" cy="70" rx="10" ry="8" fill="${C.white}"/>
        <ellipse cx="50" cy="72" rx="13" ry="10" fill="${C.white}"/>
        ${eyes(C.black)}<ellipse cx="50" cy="66" rx="4.4" ry="3" fill="${C.black}"/>`;
    },
    // 둥근 귀(안쪽 밝게), 밝은 주둥이, 작은 눈
    bear: () => {
      const fur = '#8a5a3c', light = '#d9b48f';
      return `<circle cx="22" cy="34" r="12" fill="${fur}"/><circle cx="22" cy="34" r="6.5" fill="${light}"/>
        <circle cx="78" cy="34" r="12" fill="${fur}"/><circle cx="78" cy="34" r="6.5" fill="${light}"/>
        ${head(fur)}<ellipse cx="50" cy="70" rx="15" ry="11" fill="${light}"/>${cheeks}
        ${eyes(C.black, 4.4, -1)}<ellipse cx="50" cy="65" rx="5.2" ry="3.6" fill="${C.black}"/>
        <path d="M50 68 v4 M46 74 q4 3 8 0" stroke="${C.black}" stroke-width="1.6" fill="none" stroke-linecap="round"/>`;
    },
    // 귀 없이 짙은 남색 머리, 하트 모양 흰 얼굴, 주황 부리
    penguin: () => {
      const fur = '#2e3440';
      return `${head(fur)}
        <path d="M50 46 C42 34 22 40 22 58 C22 76 38 86 50 86 C62 86 78 76 78 58 C78 40 58 34 50 46 Z" fill="${C.white}"/>
        ${cheeks}${eyes(C.black, 4.6)}<path d="M43 64 Q50 58 57 64 Q50 72 43 64 Z" fill="#f2a33a"/>`;
    },
  };

  const cache = {};
  return {
    ids: Object.keys(NAMES),
    name: id => NAMES[id] || NAMES.rabbit,
    /** 얼굴 SVG 문자열. 모르는 id면 토끼. */
    svg(id, label) {
      const k = draw[id] ? id : 'rabbit';
      cache[k] ??= draw[k]();
      return `<svg class="face" viewBox="0 0 100 92" role="img" aria-label="${label ?? NAMES[k]}">${cache[k]}</svg>`;
    },
  };
})();
