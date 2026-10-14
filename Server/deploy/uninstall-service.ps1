# 다이스 하이스트 서비스를 멈추고 지운다. 관리자 권한 PowerShell에서 실행. DB(C:\DiceHeist\data)는 지우지 않는다.
$ErrorActionPreference = "Stop"
$name = "DiceHeist"
$svc = Get-Service $name -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host "설치된 서비스가 없습니다."; return }
if ($svc.Status -ne "Stopped") { Stop-Service $name; $svc.WaitForStatus("Stopped", "00:00:30") }
sc.exe delete $name | Out-Null
Write-Host "서비스를 지웠습니다. 계정·머니 DB는 C:\DiceHeist\data 에 그대로 있습니다."
