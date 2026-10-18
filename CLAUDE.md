# 다이스 하이스트 (가제)

동물 캐릭터들이 주사위 5개로 포커 족보를 겨루는 베팅 게임. Unity 6000.6.0f1, PC.

- GitHub: `alslxhrl7-web/dice-heist` (main)
- 기획안: https://claude.ai/code/artifact/e76f4a32-b184-4cb3-8dac-388017d21692
- 웹 프로토타입 원본: `Docs/dice-heist.html`. C# 규칙은 이 파일의 evaluate / compare / aiHold / aiDecide / betting을 그대로 옮긴 것이므로, 규칙을 바꿀 때는 이 파일과 어긋나는지 먼저 확인한다. 단, 주사위 굴림(`WeightedDice`)과 브론즈·실버 AI 약화(`TableConfig.AiMaxRerolls`·`AiNerve`·`AiMisread`)는 C#에만 있다(HTML은 공정한 주사위, 2026-10-14)

## 현재 상태 (2026-10-14, 1단계 프로토타입)

나(토끼) + AI 너구리·고양이가 브론즈 테이블(참가비 10칩)에서 한 판을 끝까지 한다.
굴리기 → 남기기(최대 2회) → 걸기(포기 / 따라가기 / 올리기) → 공개.

- 확인됨: EditMode 테스트 132개(.NET으로 실행), 서버 테스트 60개, PlayMode 자동 진행 테스트, Windows 실행 파일에서 주사위 클릭 고정과 다시 굴리기
- 직접 눌러 확인하지 못함: 베팅 버튼과 공개 단계의 마우스 조작(자동 진행 테스트로만 확인)
- 임시 수준: 화면 톤(조명·색), 동물 모델(기본 도형 조립)

사이트(2026-10-16, 사용자 결정: WebGL 대신 소개 페이지): `/`는 소개 페이지(한 판의 흐름·족보·싱글/멀티·자주 묻는 것), 멀티 화면은 `/play/`. PC 게임 zip은 `/download/windows`(파일이 없으면 버튼이 숨는다). Unity 시작 화면의 멀티 카드도 `/play/`를 연다.

다음 할 일: 정하지 않았다.

## 구조

```
Assets/DiceHeist/
├─ Core/       순수 C# (UnityEngine 참조 없음). 나중에 서버로 옮길 부분
├─ Game/       MonoBehaviour와 3D 연출
├─ Editor/     씬 생성, 기준 머티리얼, Windows 빌드 메뉴
├─ Resources/  DiceHeistBase.mat (모든 머티리얼의 원본)
├─ Scenes/     Table.unity (GameBootstrap 하나만 있음)
└─ Tests/      EditMode, PlayMode
```

- **Core**: `HandEvaluator`(족보·동점), `AiBrain`(홀드·베팅), `BettingRound`, `DiceGame`(한 판 진행), `SeededRandom`(SplitMix64). 호출 순서는 StartRound → Reroll → BeginBetting → Betting.Apply → ResolveShowdown
- **SingleTable**(Core): 싱글 한 테이블을 요청 단위(Start → Reroll → Stand → Bet → result)로 진행하고 `SingleView`(상태 + 이벤트 목록)를 돌려준다. 서버와 Unity 오프라인이 같은 코드를 쓴다. AI 주사위는 쇼다운 공개 전까지 넣지 않는다. 필드만 쓰는 이유는 Unity JsonUtility로 서버 JSON을 읽기 위해서다
- **Unity 흐름**: 로그인(`LoginScreen`, 토큰은 PlayerPrefs `DiceHeist.Token`) → 시작 화면 → `TableDirector`가 `ISingleBackend`(서버 `RemoteBackend` / 이 PC `LocalBackend`)에 요청하고 돌아온 이벤트를 연출. 서버 주소는 `GameBootstrap.serverUrl`. "서버 없이 연습"과 테스트는 LocalBackend(PlayerPrefs 저장, 멀티 불가)
- **캐릭터 에셋**(2026-10-16, VARCO 3D "포커다이스" 흐름에서 생성): 그림 `Assets/Art/Characters/*.png`(Sprite, 숲 배경, rabbit이 화풍 기준), 3D `Assets/Art/Characters/3D/Resources/Characters3D/<동물>.glb`(glTFast `com.unity.cloud.gltfast`로 가져옴). `AnimalBuilder`가 이 폴더에 모델이 있으면 도형 렌더러를 끄고 모델을 Body에 붙여 키 2.4·바닥 0에 맞춘다(피벗과 연출은 그대로, 모델이 없으면 도형). 3D는 VARCO 결과(1만 2천 삼각형, 2048 PBR)를 Blender(백그라운드)로 다듬었다: 텍스처 1024 JPEG, Decimate 50%(6천 삼각형), VARCO의 노멀·AO 맵은 그대로(노멀 다시 굽기는 UV가 겹쳐 얼룩이 생겨 쓰지 않음), 토끼는 발밑 풀밭을 잘라 냈다. 한 마리 약 0.55MB. 숲 배경 그림을 그대로 3D로 만들면 배경째 판이 되거나 실패해서, 배경을 흰색으로 바꾼 그림(EditImage)으로 다시 만들었다. 테이블 등급마다 상대가 다르다(`TableConfig.Cast`: 브론즈 너구리·고양이, 실버 여우·너구리, 골드 보스 곰·여우)·딜러는 펭귄(`TableConfig.Dealer`, 판에 끼지 않고 테이블 머리에 선다). 겉모습과 이름만 바뀌고 AI 판단은 자리별 성격과 등급 값 그대로라 승률 테스트에 영향이 없다. `SeatView.character`·`SingleView.dealer`로 Unity에 알려 주면 `TableScene.SetCast`가 동물을 바꾸고 바뀌었으면 다시 등장시킨다
- **Game**: `GameBootstrap`이 실행 시 테이블·동물·UI를 전부 코드로 조립한다. `TableDirector`가 Core를 한 단계씩 진행시키며 연출과 입력 대기를 끼운다
- 주사위 눈은 Core가 먼저 정한다. `DiceThrower`는 물리를 미리 돌려 녹화하고, 멈춘 윗면에 정해진 눈을 입혀 재생한다. 연출용 난수는 게임 난수를 소비하지 않는다
- 상대 주사위는 컵 안에 있어 공개 전까지 보이지 않고 리롤 횟수만 표시한다
- 내 주사위가 멈췄을 때 스트레이트 이상(스트레이트·포 다이스·파이브 다이스)이면 `Celebration`이 화면 전체 축하(번쩍임·제목·색종이는 제목 뒤, 약 3초, 클릭은 막지 않음)를 띄운다. 한 판에 같은 족보로는 한 번만. 화면은 PlayMode 테스트가 `Logs/Shots/celebrate_*.png`로 남긴다
- `GameBootstrap.useRandomSeed`를 끄면 `seed` 값으로 같은 판이 재현된다

## 함정 의자 라이어 다이스 (2026-10-18, 브라우저 시제품)

사용자 결정: 세븐포커·사기 도박은 재미가 약해 갈아엎고, 도박성·중독성이 짙은 게임으로. 2026-10-19 못박음: "친구끼리" 게임이 아니라 모르는 사람까지 다 같이 하는 공개 게임(빠른 시작·봇이 필요). "악의적이지만 나쁘지 않은" 장치(떠넘기기·딜러 이벤트·현상금·저주 가챠 등)는 넣되, 규칙·확률은 공개하고 몰래 조작·표시와 다른 확률·현금 거래는 하지 않는다. 순서는 핵심 판 → 관전 베팅 → 더블 오어 낫싱 → 가챠(확률·보상은 사용자가 나중에 gacha.json에 넣는다). 현금 결제·환전은 넣지 않는다(사행성 게임물 선, 사용자에게 안내함).

- 주소: `/liar/`(화면 `wwwroot/liar/`: index.html·liar.css·app.js·sound.js), 허브 `/liarhub`. 예전 멀티(`/play/`, 주사위 포커)는 그대로 있다
- 규칙(Core `LiarDice.cs`의 `LiarGame`, 공정한 주사위): 2~6명, 모두 컵 속 주사위 5개. 차례대로 "테이블 전체에 X 눈이 N개 이상"을 더 높게(개수↑ 또는 같은 개수에 큰 눈) 부르거나 "거짓말!". 1은 만능, 부를 수 있는 눈은 2~6. 틀린 쪽이 함정 레버: 사람마다 6칸 중 한 칸이 함정(시작 때 정함, 비밀) → 떨어질 확률 1/6 → 1/5 → … → 1, 떨어지면 탈락. 마지막 생존자 승. 다음 판은 진 사람부터
- 방(`Server/.../Liar/LiarRoom.cs`): 바이인 50,000(stacks 표, 멀티와 같은 곳이라 한 사람은 한 테이블에만), 게임마다 참가비 5,000 → 판돈. 단계 waiting → countdown → bid(25초, 넘기면 거짓말 가능하면 거짓말·아니면 가장 작게) → reveal(4초) → lever(12초, 넘기면 자동으로 당김) → fall(3.5초) → … → double(12초, 넘기면 챙김) ⇄ doubleRoll → result
- 관전 베팅: 떨어진(또는 이번 게임에 안 낀) 사람이 살아 있는 사람에게 1,000/5,000/10,000을 게임마다 한 번. 맞히면 건 금액 × 건 때의 생존자 수(카지노 돈이라 머니 총합이 변한다)
- 더블 오어 낫싱: 이긴 사람이 판돈을 금고 주사위에 건다. 4~6 두 배, 1~3 0, 연달아 최대 3번
- 빠른 시작·봇(2026-10-19): 로비 "빠른 시작"(허브 `QuickStart`)은 대기 중인 방 → 게임 중이라도 빈자리 있는 방 → 새 방 순으로 앉힌다. 사람이 한 명이라도 있으면 게임 사이에 봇이 4명(`LiarTimings.BotFillTo`, 설정 Liar:BotFillTo, 0이면 끔)까지 채우고 사람 자리 2개는 남긴다. 사람이 오면 판에 안 낀 봇이 비켜 주고, 사람이 다 나가면 방이 닫힌다. 봇은 이름에 "봇"·화면에 봇 표시(숨기지 않는다), 음수 id, 머니는 카지노 돈이라 DB에 없고 기록도 안 남긴다. 판단은 Core `LiarBot`(자기 주사위 + 남의 주사위 × 1/3 기대 개수, 18% 허세), 차례·레버마다 1.6~1.8초 생각한 뒤 움직인다. 봇이 이기면 35% 확률로 금고 주사위를 한 번 굴린다
- 가챠: `Server/DiceHeist.Server/gacha.json`(placeholder: true, 임시 확률·가격) → `GachaConfig`. 확률 = weight / 합, 화면에 그대로 보인다. `/api/gacha`(확률), `/api/gacha/me`, `/api/gacha/pull {count:1|10}`(테이블에 앉아 있으면 못 뽑음), `/api/gacha/equip {item}`. DB `inventory` 표. 쓴 꾸미기는 자리 얼굴 위 아이콘
- 시험: `LiarDiceTests`(Core), `LiarRoomTests`·`LiarFlowTests`(서버, 허브로 한 게임 끝까지·가챠). 로컬 시험 서버는 `.claude/launch.json`의 `liar-dev`(5090, scratchpad DB)
- 서버 PC(desktop-5omono1)에서 저장소를 받고 `Server\deploy\update-service.cmd`를 실행해야 서비스에 반영된다. 2026-10-18 작업 PC(P로딩)에도 시험 삼아 서비스가 설치돼 있다(외부 주소 없음, DB 비어 있음)

## 멀티 서버 (Server/, 2026-10-14 1단계)

C# ASP.NET Core + SignalR + SQLite. Core 파일을 그대로 링크해 컴파일하므로 규칙이 싱글과 같다. 브라우저 화면은 `wwwroot`(웹 프로토타입 스타일): `index.html` 소개 페이지, `play/` 멀티 화면. 소개 페이지는 `/api/config`(족보·테이블 등급·멀티 규칙)와 `/api/site`(열린 방 수·앉은 사람 수·zip 크기)를 읽는다. zip 위치는 설정 `Site:WindowsZip`(기본: 실행 파일 옆 `downloads/DiceHeist-Windows.zip`), 서비스 설치 스크립트가 `Builds/DiceHeist-Windows.zip`을 `C:\DiceHeist\downloads`로 복사한다. 주사위 눈은 SVG로 그린다(CSS 그리드는 `<button>` 안에서 칸이 0으로 접혀 눈이 안 보였다). 멀티 화면 효과음은 `play/sound.js`가 WebAudio로 합성한다(굴림·칩·폴드·공개·승패·내 차례, 음소거는 localStorage `dh.mute`). 상태가 바뀔 때 `cue`가 바로 전 상태와 비교해 소리를 고르고, 다른 탭을 보는 중에 내 차례면 탭 제목을 깜빡인다. 로비와 소개 페이지에 순위표(`/api/ranking`, 머니 + 테이블 stack 상위 10명) 족보 표는 `/api/config`의 hands(RankWeights 확률)로 그린다. 로컬 미리보기 서버는 5090 포트(5080은 서비스가 씀)

- 계정(PBKDF2 비밀번호, 30일 토큰), 칩·사이버머니는 서버 DB(`Server/DiceHeist.Server/dice-heist.db`, git 제외)
- 로그인 보호(`LoginGuard`, 메모리): 있는 아이디로 15분 안에 비밀번호를 5번 틀리면 그 아이디를 잠시 잠근다(429). 가입은 서버 전체에서 시간당 20개(시작 머니를 노린 계정 찍어내기 방지). Funnel 뒤라 접속 주소가 모두 localhost로 보여 주소 대신 아이디·서버 전체로 센다. 값은 설정 `Security:LoginFailures`·`LockMinutes`·`RegistrationsPerHour`. 멀티 로비의 "비밀번호 바꾸기"(`/api/password`)는 지금 비밀번호를 확인하고(틀리면 같은 잠금 횟수에 든다) 이 기기를 뺀 다른 로그인을 끊는다
- 방마다 최대 6명(방 수는 제한 없음, 2명부터 시작), 앤티 1,000, 바이인 50,000(머니 → 테이블 stack, 일어나면 돌려받음). 서버가 꺼졌다 켜지면 남은 stack을 돌려준다
- 서버가 주사위·베팅을 판정하고, 남의 주사위는 쇼다운 전까지 보내지 않는다. 굴리기 30초·베팅 20초 시간 초과(자동 확정·폴드), 끊겼다 다시 접속하면 자리를 이어받는다. 자리 비움: 판에 껴서 아무 조작 없이 2판을 연달아 넘기면(`Room.AwayAfterIdleRounds`) 판에 끼지 않고 앤티도 안 낸다. "돌아왔어요"로 복귀, 스스로 "잠깐 쉬기"도 된다(지금 판은 끝까지 하고 다음 판부터). 탭을 열어 둔 채 자리를 뜨면 판마다 앤티만 나가 바이인을 다 잃던 문제
- 멀티 입장 조건은 없다(2026-10-14 사용자 결정: 브론즈 통과 조건 제거). 가입하면 사이버머니 100,000(`Db.StarterMoney`, 바이인 두 번어치)을 받고, 그 전에 가입한 계정도 서버가 켜질 때 한 번 받는다(장부 kind `starter`로 중복 방지). 이유: 브론즈는 PC 게임에서만 통과할 수 있어 브라우저로만 오는 사람은 멀티를 열 수 없었다. 머니가 모자라면 싱글에서 칩을 벌어 환전하거나, 구제 머니를 받는다: 머니가 바이인보다 적고 테이블에 앉아 있지 않으면 하루(한국 시간 자정 기준) 한 번 바이인(50,000)까지 채운다(`Db.ClaimRescue`, 장부 kind `rescue`, 2026-10-16. 브라우저로만 하는 사람은 다 잃으면 다시 앉을 길이 없었다). "테스트 머니 받기"(`/api/dev/grant`)는 개발 모드에서만 켜지고, 서비스(운영)에서는 기본으로 꺼져 있다(`install-service.ps1 -AllowGrant:$true`로만 켠다)
- 감정 표현: 방에서 정해진 짧은 표현(`Room.Emotes`: 👍 😱 ㅋㅋ 😎 🙏 빨리요!)만 보낸다. 자유 채팅은 두지 않는다(욕설·도배 관리가 필요 없게). 한 사람이 2초에 한 번, 자리 위 말풍선으로 3초
- 싱글 판 중에 나가거나 서버가 꺼지면 그 판에 낸 칩(앤티·베팅)은 잃는다(세션은 메모리에만 있다)
- 멀티 기록(`multi_stats`, 사람마다 한 줄): 낀 판, 이긴 판(나눈 판 포함), 쇼다운에서 보인 가장 높은 족보, 이겨서 가져온 가장 큰 금액. 판이 끝나면 `Room`이 결과를 모아 두고 `RoomManager.Publish`가 `Db.RecordRounds`로 적는다. 로비 "내 기록"에 보인다
- 늘 켜 두기: Windows 서비스 `DiceHeist`(`Server/deploy/install-service.ps1`, 관리자 권한, `C:\DiceHeist`에 배포·DB·하루 1회 백업) + Tailscale Funnel로 외부 HTTPS. 서버는 localhost에서만 받는다. 순서는 `Server/deploy/README.md`
- 외부 주소(바뀌면 `GameBootstrap.serverUrl`과 `wwwroot` 두 페이지의 og 주소도 바꾼다): https://desktop-5omono1.tailab2121.ts.net/ (2026-10-14 휴대폰 LTE로 접속 확인). Tailscale CLI는 PATH에 없어 `C:\Program Files\Tailscale\tailscale.exe`로 부른다

```powershell
dotnet run --project Server/DiceHeist.Server --launch-profile http   # http://localhost:5080
dotnet test Server/DiceHeist.Server.Tests
```
## 지켜야 할 것

- 판정 로직은 Core에만 둔다. Core 어셈블리는 `noEngineReferences`라 UnityEngine을 쓰면 컴파일되지 않는다
- 족보 개수 검증(7,776가지: 노페어 480, 원페어 3600, 투페어 1800, 트리플 1200, 풀하우스 300, 스트레이트 240, 포 다이스 150, 파이브 다이스 6)을 깨지 않는다. 이것은 판정(HandEvaluator) 검증이고, 실제 굴림 확률은 따로다
- 주사위는 공정하지 않다. 굴릴 때마다 결과 족보를 `WeightedDice.RankWeights` 확률표(노페어 36% > 원페어 28% > … > 파이브 0.4%, 낮은 족보일수록 흔함)로 먼저 뽑는다. 화면의 족보 표도 이 값을 읽는다
- 테이블 등급(`TableConfig.Tiers`): 브론즈 500칩 시작·한도 1,000·앤티 10 / 실버 700칩 이상 입장·한도 1,500·앤티 20 / 골드 1,000칩 이상 입장·한도 2,000·앤티 30. 한도를 넘게 딴 칩은 받지 못한다(`ShowdownResult.OverCap`). 실버·골드 앤티는 사용자가 정하지 않아 임시로 정한 값이다. 로그인하면 칩은 서버 계정에, "서버 없이 연습"이면 PlayerPrefs `DiceHeist.Chips`에 저장된다. 오프라인에서만 기록 지우기(두 번 누름)가 있다. 테스트는 `GameBootstrap.UseSave = false` 또는 AutoPlay로 저장을 건드리지 않는다
- 레이즈 크기(2026-10-16): 사람은 -/+ 버튼으로 앤티 2배~10배(`TableConfig.MinRaiseMultiplier`·`MaxRaiseMultiplier`), 앤티 단위로 고른다. 가진 칩보다 크게는 못 올린다. AI는 기본 크기(앤티 3배, `RaiseMultiplier`)로만 올린다. 기본보다 큰 레이즈를 받으면 AI는 트리플 밑의 패로 `1 - 기본/크기` 확률만큼 더 폴드한다(기본 크기면 난수를 쓰지 않아 예전 판이 그대로 재현). 서버 싱글 API도 `/api/single/bet`의 `raiseTo`로 크기를 받는다(0이면 기본 크기, 범위 밖이면 Core가 맞춤, 2026-10-16). 멀티(브라우저)도 같은 -/+로 고르고 허브 `RaiseTo(total)`로 보낸다(`Bet("raise")`는 기본 크기 그대로). 누적 금액은 앤티를 뺀 값이라 "7배"면 앤티 7개를 더 낸다
- 브론즈는 사람도 AI도 500칩으로 시작한다(`AiStartChips = 500`, 2026-10-16 사용자 결정). AI는 칩이 앤티 4배 밑으로 떨어지면 500칩으로 다시 채운다
- 소리: `DiceAudio`가 기본은 코드로 합성한다(칩·폴드·공개·승패·컵 내려놓기). `Resources/Audio`에 파일이 있으면 그것을 쓴다: `dice_pour`(컵에서 쏟아 구르다 멈추는 한 번 전체, 던질 때 물리의 첫 충돌에 맞춰 한 번 틀고 충돌마다 딸깍·구르기 합성음은 끈다), `cup_shake`(상대 컵 흔들기), `dice_hit*`·`dice_roll`(합성 딸깍·구르기 대체). 지금 들어 있는 dice_pour·cup_shake는 VARCO Sound(sound.varco.ai, 사용자 계정)의 "야추 주사위" 생성음을 앞뒤 무음을 자르고 크기를 맞춘 것(2026-10-16, 사용자 선택: Dice_pour_1, Dice_roll_3의 0.1~1.0초)- 환전(ChipExchange): 싱글에서 번 칩을 멀티용 사이버머니로 한 방향만 바꾼다. 칩 1 = 100머니, 100칩 단위, 처음 500칩 위로 번 칩만(구제 칩이 새지 않게). 사이버머니는 PlayerPrefs DiceHeist.CyberMoney(문자열). 현금 환전·현금 구매는 넣지 않는다(사용자 결정, 법적 제약 회피)
- 싱글 상대 AI 강도(2026-10-14 사용자 목표: 사람 승률 브론즈 약 70%, 실버 약 60%, 골드는 그대로). 브론즈: 다시 굴리기 1회, 65% 확률로 제 패를 거꾸로 읽는다(`AiMisread`: 약한 패로 콜·레이즈, 투페어 이상은 폴드), 판당 약 +21칩. 실버: 다시 굴리기 2회, 배짱 절반, 30% 거꾸로 읽음, 판당 약 +13칩. 골드: 제 실력, 사람 승률 약 35%. `브론즈에서는_사람이_열_판에_일곱_판쯤_딴다`·`실버에서는_사람이_열_판에_여섯_판쯤_딴다` 테스트가 지킨다. 예전 "쉽게 폴드" 방식은 팟이 작아 판당 +10칩뿐이라 거꾸로 읽기로 바꿨다
- 브론즈 한도(1,000칩)에 닿았는지는 기록만 한다(`TableConfig.ClearsBronze`, 서버 bronze_cleared). Unity 시작 화면의 멀티 카드는 로그인하면 열리고 브라우저 사이트를 연다
- 입력은 Input System 패키지만 쓴다(`activeInputHandler: 1`). `UnityEngine.Input`이나 `StandaloneInputModule`을 쓰면 실행 중 예외가 난다. UI 클릭은 `HudView`가 만드는 `InputSystemUIInputModule`이 처리한다
- 머티리얼은 `Fx.Mat`으로 만든다. 코드에서 셰이더만으로 새 머티리얼을 만들면 빌드에서 셰이더가 빠져 분홍색으로 나온다
- 캐릭터나 연출이 족보 확률에 영향을 주지 않는다(기획 원칙). 확률표는 모든 자리에 같고, 난이도는 테이블 등급의 AI 판단으로만 조절한다

## 실행과 검증

사용자 결정(2026-10-16): 에디터를 끄지 않는다. 테스트는 열린 에디터에서 보이게 돌린다 — `Temp/DiceHeist.cmd`에 한 줄 명령(refresh / play / stop / test-edit / test-play / only <클래스 전체 이름>)을 쓰면 `EditorRemote`가 실행하고 `Temp/DiceHeist.result`에 결과를 쓴다. 빌드는 사용자가 직접 한다(Claude는 빌드하지 않는다). 아래 배치 모드 명령은 에디터가 닫혀 있을 때만 쓴다(열려 있으면 실패한다).

```powershell
$u = "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"

# 테스트 (EditMode 자리에 PlayMode도 가능)
# EditMode 테스트는 Core만 쓰므로, 에디터가 열려 있으면 .NET SDK로 Core + Tests/EditMode를 묶은 NUnit 프로젝트를 만들어 dotnet test로도 돌릴 수 있다
& $u -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$env:TEMP\dh.xml" -logFile "$env:TEMP\dh.log"

# Windows 빌드 → Builds/Windows/DiceHeist.exe, 보낼 파일은 Builds/DiceHeist-Windows.zip(빌드할 때마다 덮어씀)
& $u -batchmode -quit -projectPath . -executeMethod DiceHeist.EditorTools.DiceHeistSetup.BuildWindows -logFile "$env:TEMP\dh_build.log"
```

- PlayMode 테스트는 단계별 화면을 `Logs/Shots`에 저장한다
- 에디터 로그는 `Logs/Editor.log`에 쌓인다(`%LOCALAPPDATA%\Unity\Editor\Editor.log`가 아님)
- 에디터 메뉴 `DiceHeist`에 씬 다시 만들기, 기준 머티리얼 만들기, Windows 빌드가 있다

## 이 PC의 환경

- git은 PATH에 없다. GitHub Desktop에 들어 있는 것을 쓴다: `%LOCALAPPDATA%\GitHubDesktop\app-*\resources\app\git\cmd` (가장 최신 `app-*` 폴더)
- gh CLI는 없다. 새 GitHub 저장소는 사람이 직접 만들어야 한다
- 푸시가 인증에서 막히면 `$env:GCM_INTERACTIVE = "always"`로 바꾸고 `GIT_TERMINAL_PROMPT` 환경 변수를 지운 뒤 다시 시도한다
- Unity CLI는 `%LOCALAPPDATA%\Unity\bin\unity.exe`. `unity projects add <경로>`로 허브 목록에 등록한다
- 사용자 폴더 이름이 한글이라 PowerShell에서는 `$env:USERPROFILE`을 쓰는 편이 안전하다
