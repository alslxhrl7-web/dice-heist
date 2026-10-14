# 다이스 하이스트 멀티 서버를 Windows 서비스로 설치하거나 새 버전으로 바꾼다.
# 관리자 권한 PowerShell에서 저장소 폴더 기준으로 실행:  .\Server\deploy\install-service.ps1
# 다시 실행하면 서비스를 멈추고 새로 빌드한 뒤 다시 시작한다(계정·머니가 든 DB는 그대로).
param(
    [string]$Root = "C:\DiceHeist",
    [int]$Port = 5080,
    # "테스트 머니 받기" 버튼. 기본은 꺼짐(2026-10-14부터). 시험할 때만 -AllowGrant:$true로 켠다
    [bool]$AllowGrant = $false
)
$ErrorActionPreference = "Stop"
$name = "DiceHeist"

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "관리자 권한 PowerShell에서 실행해 주세요 (시작 메뉴 → PowerShell 오른쪽 클릭 → 관리자로 실행)." }

$project = Join-Path $PSScriptRoot "..\DiceHeist.Server\DiceHeist.Server.csproj"
$app = Join-Path $Root "server"
$data = Join-Path $Root "data"

$svc = Get-Service $name -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne "Stopped") { Write-Host "서비스 멈추는 중…"; Stop-Service $name; $svc.WaitForStatus("Stopped", "00:00:30") }

Write-Host "빌드 중… ($app)"
dotnet publish $project -c Release -o $app --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

New-Item -ItemType Directory -Force $data | Out-Null
# 운영 설정: 이 PC 안(localhost)에서만 받고, 밖에서는 터널(Tailscale Funnel)로만 들어온다
@{
    Urls = "http://localhost:$Port"
    Db   = @{ Path = (Join-Path $data "dice-heist.db"); BackupDir = (Join-Path $data "backups"); BackupKeep = 7 }
    Dev  = @{ AllowGrant = $AllowGrant; GrantMoney = 100000 }
} | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $app "appsettings.Production.json") -Encoding UTF8

if (-not $svc) {
    Write-Host "서비스 등록…"
    New-Service -Name $name -DisplayName "Dice Heist 멀티 서버" -BinaryPathName "`"$(Join-Path $app 'DiceHeist.Server.exe')`"" -StartupType Automatic | Out-Null
    # 서버가 죽으면 5초 뒤 다시 띄운다(세 번째부터는 30초 뒤)
    sc.exe failure $name reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}

Start-Service $name
Start-Sleep -Seconds 3
try {
    $cfg = Invoke-RestMethod "http://localhost:$Port/api/config" -TimeoutSec 10
    Write-Host "완료: http://localhost:$Port 에서 돌고 있습니다. (테스트 머니 버튼: $($cfg.devGrant))"
} catch {
    Write-Warning "서비스는 시작했지만 응답이 없습니다. 이벤트 뷰어 → Windows 로그 → 응용 프로그램에서 원본이 DiceHeist.Server 또는 .NET Runtime인 오류를 확인하세요."
}
