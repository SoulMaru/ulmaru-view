param(
    [Parameter(Mandatory = $true)][string]$Build,
    [string]$AppRoot = 'C:\AI_SOULMARU\4.adkiller'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Build)) { throw "빌드 파일이 없습니다: $Build" }
$buildHash = (Get-FileHash -LiteralPath $Build -Algorithm SHA256).Hash

$targets = @(
    (Join-Path $AppRoot 'Adkiller Media Player\얼마루미디어.exe'),
    (Join-Path $AppRoot 'Adkiller Media Player\adkiller Media Player.exe'),
    (Join-Path $AppRoot 'Adkiller Media Player\EolmaruMedia.exe'),
    (Join-Path $AppRoot 'readers-framework\MediaPlayer.engine.exe')
)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $AppRoot "backups\player-deploy-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target) {
        Copy-Item -LiteralPath $target -Destination (Join-Path $backup (Split-Path $target -Leaf)) -Force
    }
}
Write-Output "backup=$backup"

foreach ($target in $targets) {
    if ([IO.Path]::GetFullPath($Build) -ne [IO.Path]::GetFullPath($target)) {
        Copy-Item -LiteralPath $Build -Destination $target -Force
    }
    # zip/다운로드에서 남은 Mark of the Web을 제거한다(내용은 바꾸지 않음).
    Remove-Item -LiteralPath $target -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue
}

$failed = 0
foreach ($target in $targets) {
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    $motw = Get-Item -LiteralPath $target -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue
    $ok = ($hash -eq $buildHash) -and ($null -eq $motw)
    if (-not $ok) { $failed++ }
    $state = 'OK'
    if (-not $ok) { $state = 'FAIL' }
    Write-Output ("{0} {1} {2} motw={3}" -f $state, $hash.Substring(0, 16), $target, [bool]$motw)
}

if ($failed -gt 0) { throw "배포 검증 실패: $failed 개" }
Write-Output 'RESULT PASS'
