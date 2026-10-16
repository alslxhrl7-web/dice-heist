# 다이스 하이스트 (가제)

동물 캐릭터들이 주사위 5개로 포커 족보를 겨루는 베팅 게임. Unity 6000.6.0f1, PC.

- GitHub: `alslxhrl7-web/dice-heist` (main)
- 기획안: https://claude.ai/code/artifact/e76f4a32-b184-4cb3-8dac-388017d21692
- 웹 프로토타입 원본: `Docs/dice-heist.html`. C# 규칙은 이 파일의 evaluate / compare / aiHold / aiDecide / betting을 그대로 옮긴 것이므로, 규칙을 바꿀 때는 이 파일과 어긋나는지 먼저 확인한다. 단, 주사위 굴림(`WeightedDice`)과 브론즈·실버 AI 약화(`TableConfig.AiMaxRerolls`·`AiNerve`·`AiMisread`)는 C#에만 있다(HTML은 공정한 주사위, 2026-10-14)

## 현재 상태 (2026-10-14, 1단계 프로토타입)

나(토끼) + AI 너구리·고양이가 브론즈 테이블(참가비 10칩)에서 한 판을 끝까지 한다.
굴리기 → 남기기(최대 2회) → 걸기(포기 / 따라가기 / 올리기) → 공개.

- 확인됨: EditMode 테스트 108개(.NET으로 실행), 서버 테스트 35개, PlayMode 자동 진행 테스트, Windows 실행 파일에서 주사위 클릭 고정과 다시 굴리기
- 직접 눌러 확인하지 못함: 베팅 버튼과 공개 단계의 마우스 조작(자동 진행 테스트로만 확인)
- 임시 수준: 화면 톤(조명·색), 동물 모델(기본 도형 조립)

다음 할 일: 사이트 구성. 소개 페이지인지 WebGL 플레이인지는 아직 정하지 않았다.

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
- **Game**: `GameBootstrap`이 실행 시 테이블·동물·UI를 전부 코드로 조립한다. `TableDirector`가 Core를 한 단계씩 진행시키며 연출과 입력 대기를 끼운다
- 주사위 눈은 Core가 먼저 정한다. `DiceThrower`는 물리를 미리 돌려 녹화하고, 멈춘 윗면에 정해진 눈을 입혀 재생한다. 연출용 난수는 게임 난수를 소비하지 않는다
- 상대 주사위는 컵 안에 있어 공개 전까지 보이지 않고 리롤 횟수만 표시한다
- 내 주사위가 멈췄을 때 스트레이트 이상(스트레이트·포 다이스·파이브 다이스)이면 `Celebration`이 화면 전체 축하(번쩍임·제목·색종이는 제목 뒤, 약 3초, 클릭은 막지 않음)를 띄운다. 한 판에 같은 족보로는 한 번만. 화면은 PlayMode 테스트가 `Logs/Shots/celebrate_*.png`로 남긴다
- `GameBootstrap.useRandomSeed`를 끄면 `seed` 값으로 같은 판이 재현된다

## 멀티 서버 (Server/, 2026-10-14 1단계)

C# ASP.NET Core + SignalR + SQLite. Core 파일을 그대로 링크해 컴파일하므로 규칙이 싱글과 같다. 브라우저 화면은 `wwwroot`(웹 프로토타입 스타일). 주사위 눈은 SVG로 그린다(CSS 그리드는 `<button>` 안에서 칸이 0으로 접혀 눈이 안 보였다). 족보 표는 `/api/config`의 hands(RankWeights 확률)로 그린다. 로컬 미리보기 서버는 5090 포트(5080은 서비스가 씀)

- 계정(PBKDF2 비밀번호, 30일 토큰), 칩·사이버머니는 서버 DB(`Server/DiceHeist.Server/dice-heist.db`, git 제외)
- 방마다 최대 6명(방 수는 제한 없음, 2명부터 시작), 앤티 1,000, 바이인 50,000(머니 → 테이블 stack, 일어나면 돌려받음). 서버가 꺼졌다 켜지면 남은 stack을 돌려준다
- 서버가 주사위·베팅을 판정하고, 남의 주사위는 쇼다운 전까지 보내지 않는다. 굴리기 30초·베팅 20초 시간 초과(자동 확정·폴드), 끊겼다 다시 접속하면 자리를 이어받는다
- 멀티 입장 조건은 없다(2026-10-14 사용자 결정: 브론즈 통과 조건 제거). 가입하면 사이버머니 100,000(`Db.StarterMoney`, 바이인 두 번어치)을 받고, 그 전에 가입한 계정도 서버가 켜질 때 한 번 받는다(장부 kind `starter`로 중복 방지). 이유: 브론즈는 PC 게임에서만 통과할 수 있어 브라우저로만 오는 사람은 멀티를 열 수 없었다. 머니가 모자라면 싱글에서 칩을 벌어 환전한다. "테스트 머니 받기"(`/api/dev/grant`)는 개발 모드에서만 켜지고, 서비스(운영)에서는 기본으로 꺼져 있다(`install-service.ps1 -AllowGrant:$true`로만 켠다)
- 싱글 판 중에 나가거나 서버가 꺼지면 그 판에 낸 칩(앤티·베팅)은 잃는다(세션은 메모리에만 있다)
- 늘 켜 두기: Windows 서비스 `DiceHeist`(`Server/deploy/install-service.ps1`, 관리자 권한, `C:\DiceHeist`에 배포·DB·하루 1회 백업) + Tailscale Funnel로 외부 HTTPS. 서버는 localhost에서만 받는다. 순서는 `Server/deploy/README.md`
- 외부 주소: https://desktop-5omono1.tailab2121.ts.net/ (2026-10-14 휴대폰 LTE로 접속 확인). Tailscale CLI는 PATH에 없어 `C:\Program Files\Tailscale\tailscale.exe`로 부른다

```powershell
dotnet run --project Server/DiceHeist.Server --launch-profile http   # http://localhost:5080
dotnet test Server/DiceHeist.Server.Tests
```
## 지켜야 할 것

- 판정 로직은 Core에만 둔다. Core 어셈블리는 `noEngineReferences`라 UnityEngine을 쓰면 컴파일되지 않는다
- 족보 개수 검증(7,776가지: 노페어 480, 원페어 3600, 투페어 1800, 트리플 1200, 풀하우스 300, 스트레이트 240, 포 다이스 150, 파이브 다이스 6)을 깨지 않는다. 이것은 판정(HandEvaluator) 검증이고, 실제 굴림 확률은 따로다
- 주사위는 공정하지 않다. 굴릴 때마다 결과 족보를 `WeightedDice.RankWeights` 확률표(노페어 36% > 원페어 28% > … > 파이브 0.4%, 낮은 족보일수록 흔함)로 먼저 뽑는다. 화면의 족보 표도 이 값을 읽는다
- 테이블 등급(`TableConfig.Tiers`): 브론즈 500칩 시작·한도 1,000·앤티 10 / 실버 700칩 이상 입장·한도 1,500·앤티 20 / 골드 1,000칩 이상 입장·한도 2,000·앤티 30. 한도를 넘게 딴 칩은 받지 못한다(`ShowdownResult.OverCap`). 실버·골드 앤티는 사용자가 정하지 않아 임시로 정한 값이다. 로그인하면 칩은 서버 계정에, "서버 없이 연습"이면 PlayerPrefs `DiceHeist.Chips`에 저장된다. 오프라인에서만 기록 지우기(두 번 누름)가 있다. 테스트는 `GameBootstrap.UseSave = false` 또는 AutoPlay로 저장을 건드리지 않는다
- 레이즈 크기(2026-10-16): 사람은 -/+ 버튼으로 앤티 2배~10배(`TableConfig.MinRaiseMultiplier`·`MaxRaiseMultiplier`), 앤티 단위로 고른다. 가진 칩보다 크게는 못 올린다. AI는 기본 크기(앤티 3배, `RaiseMultiplier`)로만 올린다. 기본보다 큰 레이즈를 받으면 AI는 트리플 밑의 패로 `1 - 기본/크기` 확률만큼 더 폴드한다(기본 크기면 난수를 쓰지 않아 예전 판이 그대로 재현). 서버 싱글 API도 `/api/single/bet`의 `raiseTo`로 크기를 받는다(0이면 기본 크기, 범위 밖이면 Core가 맞춤, 2026-10-16)
- 브론즈는 사람도 AI도 500칩으로 시작한다(`AiStartChips = 500`, 2026-10-16 사용자 결정). AI는 칩이 앤티 4배 밑으로 떨어지면 500칩으로 다시 채운다
- 소리: `DiceAudio`가 녹음 파일 없이 코드로 합성한다. 굴린 주사위는 물리 녹화에서 찾은 충돌 시점마다 딸깍 소리, 상대 컵은 흔들 때 달그락·내려놓을 때 둥. 실제 녹음으로 바꾸려면 `DiceAudio`의 클립만 갈아 끼우면 된다- 환전(ChipExchange): 싱글에서 번 칩을 멀티용 사이버머니로 한 방향만 바꾼다. 칩 1 = 100머니, 100칩 단위, 처음 500칩 위로 번 칩만(구제 칩이 새지 않게). 사이버머니는 PlayerPrefs DiceHeist.CyberMoney(문자열). 현금 환전·현금 구매는 넣지 않는다(사용자 결정, 법적 제약 회피)
- 싱글 상대 AI 강도(2026-10-14 사용자 목표: 사람 승률 브론즈 약 70%, 실버 약 60%, 골드는 그대로). 브론즈: 다시 굴리기 1회, 65% 확률로 제 패를 거꾸로 읽는다(`AiMisread`: 약한 패로 콜·레이즈, 투페어 이상은 폴드), 판당 약 +21칩. 실버: 다시 굴리기 2회, 배짱 절반, 30% 거꾸로 읽음, 판당 약 +13칩. 골드: 제 실력, 사람 승률 약 35%. `브론즈에서는_사람이_열_판에_일곱_판쯤_딴다`·`실버에서는_사람이_열_판에_여섯_판쯤_딴다` 테스트가 지킨다. 예전 "쉽게 폴드" 방식은 팟이 작아 판당 +10칩뿐이라 거꾸로 읽기로 바꿨다
- 브론즈 한도(1,000칩)에 닿았는지는 기록만 한다(`TableConfig.ClearsBronze`, 서버 bronze_cleared). Unity 시작 화면의 멀티 카드는 로그인하면 열리고 브라우저 사이트를 연다
- 입력은 Input System 패키지만 쓴다(`activeInputHandler: 1`). `UnityEngine.Input`이나 `StandaloneInputModule`을 쓰면 실행 중 예외가 난다. UI 클릭은 `HudView`가 만드는 `InputSystemUIInputModule`이 처리한다
- 머티리얼은 `Fx.Mat`으로 만든다. 코드에서 셰이더만으로 새 머티리얼을 만들면 빌드에서 셰이더가 빠져 분홍색으로 나온다
- 캐릭터나 연출이 족보 확률에 영향을 주지 않는다(기획 원칙). 확률표는 모든 자리에 같고, 난이도는 테이블 등급의 AI 판단으로만 조절한다

## 실행과 검증

에디터가 이 프로젝트를 열고 있으면 배치 모드 명령은 실패한다. 먼저 에디터를 닫는다.

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
