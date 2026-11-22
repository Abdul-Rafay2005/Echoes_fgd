<#
.SYNOPSIS
  Generates, validates, builds, packages and (optionally) auto-tests Echoes of Eternity.

.DESCRIPTION
  Locates the Unity editor that matches UnityProject/ProjectSettings/ProjectVersion.txt, runs the
  editor batch methods, checks every exit code and log, and zips the ENTIRE build folder.

  Steps (each logs to tools/logs/<step>.log):
    generate  -executeMethod Echoes.Editor.ProjectBuilder.BuildProject
    validate  -executeMethod Echoes.Editor.ProjectBuilder.ValidateProject
    build     -executeMethod Echoes.Editor.BuildGame.Build     (requires "BUILD_RESULT: Succeeded")
    package   Builds/EchoesOfEternity_Windows.zip
    test      (with -RunTests) launches the built player with -echoesAutoTest and checks its report.
              Add -Headless to run it without a window (-batchmode -nographics); the windowed
              run additionally checks UI layout at 720p/1080p and saves screenshots.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build.ps1
  powershell -ExecutionPolicy Bypass -File tools\build.ps1 -RunTests
  powershell -ExecutionPolicy Bypass -File tools\build.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
#>
param(
    [string]$UnityPath = "",
    [string]$ProjectPath = "",
    [switch]$SkipGenerate,
    [switch]$SkipBuild,
    [switch]$RunTests,
    [switch]$Headless,
    [switch]$SelfTestRebuild
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $ProjectPath) { $ProjectPath = Join-Path $Root "UnityProject" }
$ProjectPath = (Resolve-Path $ProjectPath).Path
$LogDir = Join-Path $PSScriptRoot "logs"
New-Item -ItemType Directory -Force $LogDir | Out-Null
$BuildDir = Join-Path $Root "Builds\Windows"
$ZipPath = Join-Path $Root "Builds\EchoesOfEternity_Windows.zip"

function Write-Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }

function Find-Unity {
    if ($UnityPath) {
        if (Test-Path $UnityPath) { return (Resolve-Path $UnityPath).Path }
        throw "Unity not found at -UnityPath '$UnityPath'."
    }
    $versionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
    $version = (Select-String -Path $versionFile -Pattern "m_EditorVersion:\s*(\S+)").Matches[0].Groups[1].Value
    $candidates = @()
    if ($env:UNITY_EDITOR_PATH) { $candidates += $env:UNITY_EDITOR_PATH }
    $candidates += "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
    $secondary = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
    if (Test-Path $secondary) {
        $p = (Get-Content $secondary -Raw).Trim().Trim('"')
        if ($p) { $candidates += (Join-Path $p "$version\Editor\Unity.exe") }
    }
    $candidates += "C:\Program Files\Unity $version\Editor\Unity.exe"
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    throw "Unity $version is not installed (looked in: $($candidates -join '; ')). Install it with Unity Hub, or pass -UnityPath."
}

function Invoke-Unity([string]$step, [string]$method, [string]$extra = "") {
    $log = Join-Path $LogDir "$step.log"
    if (Test-Path $log) { Remove-Item $log -Force }
    $argLine = "-batchmode -quit -projectPath `"$ProjectPath`" -executeMethod $method -logFile `"$log`" $extra"
    Write-Host "$script:Unity $argLine"
    $p = Start-Process -FilePath $script:Unity -ArgumentList $argLine -Wait -PassThru -NoNewWindow
    $code = $p.ExitCode
    Write-Host "$step exit code: $code  (log: $log)"
    if (Test-Path $log) {
        $errors = Select-String -CaseSensitive -Path $log -Pattern "error CS\d+|Scripts have compiler errors|FAILED|VALIDATION FAILED|BUILD_RESULT: (?!Succeeded)" | Select-Object -First 15
        if ($errors) { Write-Host "Relevant log lines:" -ForegroundColor Yellow; $errors | ForEach-Object { Write-Host "  $($_.Line)" } }
    }
    if ($code -ne 0) { throw "$step failed with exit code $code. See $log" }
    return $log
}

$script:Unity = Find-Unity
Write-Host "Unity editor: $script:Unity"
Write-Host "Project:      $ProjectPath"

if (Get-Process -Name Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $script:Unity }) {
    Write-Host "Warning: this Unity editor is already running. If it has the project open, batch mode will fail - close it first." -ForegroundColor Yellow
}

if (-not $SkipGenerate) {
    Write-Step "Generating project content"
    Invoke-Unity "generate" "Echoes.Editor.ProjectBuilder.BuildProject" | Out-Null
}
if ($SelfTestRebuild) {
    Write-Step "Builder idempotence self-test"
    $log = Invoke-Unity "selftest" "Echoes.Editor.ProjectBuilder.SelfTestRebuild"
    if (-not (Select-String -Path $log -Pattern "SELFTEST PASSED" -Quiet)) { throw "Self-test did not report success." }
}

Write-Step "Validating project"
$log = Invoke-Unity "validate" "Echoes.Editor.ProjectBuilder.ValidateProject"
if (-not (Select-String -Path $log -Pattern "VALIDATION PASSED" -Quiet)) { throw "Validation did not report success." }

if (-not $SkipBuild) {
    Write-Step "Building Windows player"
    $log = Invoke-Unity "build" "Echoes.Editor.BuildGame.Build"
    if (-not (Select-String -Path $log -Pattern "BUILD_RESULT: Succeeded" -Quiet)) { throw "BuildReport did not report Succeeded. See $log" }
    $exe = Join-Path $BuildDir "EchoesOfEternity.exe"
    if (-not (Test-Path $exe)) { throw "Build reported success but $exe is missing." }
    Copy-Item $log (Join-Path $Root "build.log") -Force

    Write-Step "Packaging the entire build folder"
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path (Join-Path $BuildDir "*") -DestinationPath $ZipPath -CompressionLevel Optimal
    $zipMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
    $fileCount = (Get-ChildItem $BuildDir -Recurse -File).Count
    Write-Host "Created $ZipPath ($zipMB MB, $fileCount files from $BuildDir)"
}

if ($RunTests) {
    Write-Step "Running the automated acceptance test in the standalone player"
    $exe = Join-Path $BuildDir "EchoesOfEternity.exe"
    $testDir = Join-Path $LogDir "autotest"
    New-Item -ItemType Directory -Force $testDir | Out-Null
    Get-ChildItem $testDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    $report = Join-Path $testDir "report.txt"
    $playerLog = Join-Path $testDir "player.log"
    $argLine = "-echoesAutoTest -echoesTestReport `"$report`" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile `"$playerLog`""
    if ($Headless) { $argLine = "-batchmode -nographics $argLine" } else { Write-Host "A game window will open and play itself for ~4 minutes. Please do not click it." -ForegroundColor Yellow }
    $p = Start-Process -FilePath $exe -ArgumentList $argLine -PassThru
    if (-not $p.WaitForExit(600000)) { $p.Kill(); throw "Auto-test timed out." }
    Write-Host "Player exit code: $($p.ExitCode)"
    if (-not (Test-Path $report)) { throw "Auto-test report missing. See $playerLog" }
    Get-Content $report | Where-Object { $_ -match "^(FAIL|RESULT)" } | ForEach-Object { Write-Host $_ }
    if ($p.ExitCode -ne 0) { throw "Auto-test reported failures (exit $($p.ExitCode)). Report: $report" }
    Write-Host "Auto-test passed. Report: $report  Screenshots: $(Join-Path $testDir 'screenshots')"
}

Write-Host ""
Write-Host "All requested steps succeeded." -ForegroundColor Green
exit 0
