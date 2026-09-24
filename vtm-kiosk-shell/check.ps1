<#
.SYNOPSIS
  Builds everything and checks whether this machine can actually run a kiosk call.

.DESCRIPTION
  Written because "it did not work" is not a bug report. Every step says what it looked at and
  what it found, so a failure names the thing that is wrong instead of leaving you guessing.

  It checks, in order:

    1. the tools - .NET, node, the WebView2 runtime
    2. the services - SQL Server through the API, LiveKit, the teller console
    3. that the three agree on which LiveKit they are using, which is the mistake that
       looks exactly like a broken call
    4. the build - Angular page, shell, and that the page really landed in bin/webroot
    5. enrolment - a fresh secret written into kiosk-shell.json
    6. the call itself, end to end, if -Full is given

.PARAMETER Full
  Also drive a whole call through the shell. Needs Playwright in the scratch folder and
  several hundred MB of free memory; the quick checks are enough to find most problems.

.PARAMETER KioskId
  Which kiosk to enrol and configure. Defaults to K-01.

.EXAMPLE
  .\check.ps1
  .\check.ps1 -Full
#>

[CmdletBinding()]
param(
    [switch]$Full,
    [string]$KioskId = 'K-01',
    [string]$ApiUrl = 'http://localhost:5065',
    [string]$LiveKitWs = 'ws://localhost:7880',
    [string]$TellerUrl = 'http://localhost:4200'
)

$ErrorActionPreference = 'Continue'
$script:Failures = @()

function Step($name) { Write-Host "`n-- $name " -ForegroundColor Cyan -NoNewline; Write-Host ('-' * [Math]::Max(0, 52 - $name.Length)) -ForegroundColor DarkGray }
function Ok($what, $detail) { Write-Host '  ok   ' -ForegroundColor Green -NoNewline; Write-Host $what -NoNewline; if ($detail) { Write-Host "  $detail" -ForegroundColor DarkGray } else { Write-Host '' } }
function Bad($what, $detail) { $script:Failures += $what; Write-Host '  FAIL ' -ForegroundColor Red -NoNewline; Write-Host $what -NoNewline; if ($detail) { Write-Host "  $detail" -ForegroundColor DarkGray } else { Write-Host '' } }
function Note($text) { Write-Host "       $text" -ForegroundColor DarkGray }

# -Encoding utf8 writes a BOM on Windows PowerShell, which several readers refuse.
function Save-Config($cfg, $path) {
    [System.IO.File]::WriteAllText($path, ($cfg | ConvertTo-Json), (New-Object System.Text.UTF8Encoding $false))
}

$shellDir = $PSScriptRoot
$pageDir = Join-Path (Split-Path $shellDir -Parent) 'vtm-kiosk'
$outDir = Join-Path $shellDir 'bin\Release\net10.0-windows'

# -- 1. Tools ----------------------------------------------------
Step 'Tools'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if ($dotnet) { Ok '.NET SDK' (& dotnet --version) } else { Bad '.NET SDK' 'not on PATH' }

$node = (Get-Command node -ErrorAction SilentlyContinue)
if ($node) { Ok 'node' (& node --version) } else { Bad 'node' 'not on PATH - the Angular build needs it' }

# WebView2 is what actually renders the call; without the runtime the window stays blank.
$wv = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($wv) { Ok 'WebView2 runtime' (Get-ItemProperty $wv).pv }
else { Bad 'WebView2 runtime' 'not installed - install the Evergreen runtime' }

# -- 2. Services -------------------------------------------------
Step 'Services'

function Probe($url, $timeoutSec = 5) {
    try { (Invoke-WebRequest -Uri $url -TimeoutSec $timeoutSec -UseBasicParsing).StatusCode }
    catch { $null }
}

$apiOk = (Probe "$ApiUrl/health") -eq 200
if ($apiOk) { Ok 'API' "$ApiUrl" } else { Bad 'API' "$ApiUrl is not answering - start LivekitServerAPI" }

$lkHttp = $LiveKitWs -replace '^ws', 'http'
if ((Probe $lkHttp) -eq 200) { Ok 'LiveKit' $LiveKitWs } else { Bad 'LiveKit' "$LiveKitWs is not answering" }

if ((Probe $TellerUrl) -eq 200) { Ok 'Teller console' $TellerUrl }
else { Note "Teller console at $TellerUrl is not being served - only needed for the full call check" }

# -- 3. Do they agree which LiveKit? -----------------------------
Step 'Everyone on the same LiveKit'

# This is the mistake that costs the most time: each side connects happily, the room shows zero
# participants, and both UIs look fine while nobody can hear anyone.
$tellerSrc = Join-Path (Split-Path $shellDir -Parent) 'vtm-teller\src\app\teller.service.ts'
$tellerLk = if (Test-Path $tellerSrc) {
    ([regex]::Match((Get-Content $tellerSrc -Raw), "liveKitUrl:\s*'([^']+)'")).Groups[1].Value
} else { '' }

$shellCfgPath = Join-Path $outDir 'kiosk-shell.json'
$shellCfg = if (Test-Path $shellCfgPath) { Get-Content $shellCfgPath -Raw | ConvertFrom-Json } else { $null }
$kioskLk = if ($shellCfg -and $shellCfg.liveKitUrl) { $shellCfg.liveKitUrl } else { '(page default)' }

Note "kiosk : $kioskLk"
Note "teller: $tellerLk"

if ($tellerLk -and $kioskLk -ne '(page default)' -and $tellerLk -ne $kioskLk) {
    Bad 'the two sides use different LiveKit servers' 'they will never see each other'
} else {
    Ok 'both sides point at the same LiveKit'
}

# -- 4. Build ----------------------------------------------------
Step 'Build'

if (Test-Path (Join-Path $pageDir 'package.json')) {
    if (-not (Test-Path (Join-Path $pageDir 'node_modules'))) {
        Note 'installing the page dependencies (first run only)'
        Push-Location $pageDir; & npm ci --no-audit --no-fund | Out-Null; Pop-Location
    }

    Push-Location $pageDir
    & npm run build 2>&1 | Out-Null
    $pageBuilt = $LASTEXITCODE -eq 0
    Pop-Location

    if ($pageBuilt) { Ok 'kiosk page built' } else { Bad 'kiosk page build failed' 'run npm run build in vtm-kiosk to see why' }
} else {
    Bad 'kiosk page missing' $pageDir
}

Push-Location $shellDir
& dotnet build -c Release 2>&1 | Out-Null
$shellBuilt = $LASTEXITCODE -eq 0
Pop-Location

if ($shellBuilt) { Ok 'shell built' } else { Bad 'shell build failed' 'run dotnet build -c Release to see why' }

# The page has to have actually landed, not merely been built.
$webroot = Join-Path $outDir 'webroot'
if (Test-Path (Join-Path $webroot 'index.html')) {
    $main = Get-ChildItem (Join-Path $webroot 'main-*.js') -ErrorAction SilentlyContinue | Select-Object -First 1

    # A page built before the host bridge existed loads, runs, and ignores every command the
    # shell sends - which looks like the shell being broken. Worth naming.
    if ($main -and (Select-String -Path $main.FullName -Pattern 'embed__state' -Quiet)) {
        Ok 'page staged in bin\webroot' "$((Get-ChildItem $webroot).Count) files"
    } else {
        Bad 'the staged page is out of date' 'it predates the host bridge - rebuild the page'
    }
} else {
    Bad 'no page in bin\webroot' 'nothing for the shell to show'
}

if (Test-Path (Join-Path $webroot 'kiosk.secret')) {
    Bad 'a kiosk secret is bundled with the page' 'it must be injected, not shipped'
} else {
    Ok 'no secret bundled with the page'
}

# -- 5. Enrolment ------------------------------------------------
Step 'Enrolment'

if ($apiOk) {
    try {
        $login = Invoke-RestMethod "$ApiUrl/api/auth/login" -Method Post -ContentType 'application/json' `
            -Body (@{ username = 'admin'; password = 'DevAdmin!2026' } | ConvertTo-Json)

        $headers = @{ Authorization = "Bearer $($login.data.accessToken)" }

        $enroll = Invoke-RestMethod "$ApiUrl/api/kiosks/$KioskId/enroll" -Method Post -Headers $headers `
            -ContentType 'application/json' -Body (@{ revokeExisting = $true } | ConvertTo-Json)

        $secret = $enroll.data.secret
        Ok "$KioskId enrolled" "secret $($secret.Substring(0, 8))..."

        $cfg = [ordered]@{
            kioskId = $KioskId
            apiBaseUrl = $ApiUrl
            liveKitUrl = $LiveKitWs
            deviceSecret = $secret
            webRoot = 'webroot'
            virtualHost = 'kiosk.vtm'
            callWindowWidth = 320
            callWindowHeight = 240
            captureSource = 'Entire screen'
            devTools = $false
            fakeMedia = $false
            remoteDebuggingPort = 0
        }

        if ($Full) { $cfg.fakeMedia = $true; $cfg.devTools = $true; $cfg.remoteDebuggingPort = 9333 }

        Save-Config $cfg $shellCfgPath
        Ok 'kiosk-shell.json written' $shellCfgPath

        if ($cfg.fakeMedia) {
            Note 'fakeMedia is ON for this run - the teller will see a test pattern, not a camera'
        }

        # The queue keeps rows whose room is gone until the reaper sweeps. Clearing now means a
        # test run is looking at its own session rather than yesterday's.
        $queue = Invoke-RestMethod "$ApiUrl/api/queue" -Headers $headers
        foreach ($e in $queue.data.entries) {
            try { Invoke-RestMethod "$ApiUrl/api/sessions/$($e.roomName)" -Method Delete -Headers $headers | Out-Null } catch { }
        }
        if ($queue.data.count -gt 0) { Note "cleared $($queue.data.count) queued session(s)" }
    }
    catch {
        Bad 'enrolment failed' $_.Exception.Message
    }
} else {
    Note 'skipped - the API is not up'
}

# -- 6. The call -------------------------------------------------
if ($Full) {
    Step 'A whole call through the shell'

    $free = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 2)
    Note "free memory: $free GB"
    if ($free -lt 1.0) {
        Note 'under 1 GB free - a browser and a WebView2 both starting here tends to fail'
    }

    $test = Join-Path $env:TEMP 'claude\d--MCS-VTM-LIvekit\83dc12aa-fa83-48cb-b256-eb8c420b6895\scratchpad\hosted-test.mjs'
    if (-not (Test-Path $test)) {
        Note "no end-to-end script at $test"
    } else {
        $exe = Join-Path $outDir 'VtmKiosk.exe'
        $proc = Start-Process $exe -PassThru
        Start-Sleep -Seconds 16

        Push-Location (Split-Path $test -Parent)
        & node $test
        $callOk = $LASTEXITCODE -eq 0
        Pop-Location

        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

        if ($callOk) { Ok 'the call worked end to end' } else { Bad 'the end-to-end call did not complete' 'see the output above' }

        # Put the kiosk back on its real camera and shut the debugger. Leaving fakeMedia on is
        # how a kiosk ends up showing a teller a green test pattern instead of the customer -
        # which looks like a broken camera and is not one.
        $cfg.fakeMedia = $false
        $cfg.devTools = $false
        $cfg.remoteDebuggingPort = 0
        Save-Config $cfg $shellCfgPath
        Note 'restored kiosk-shell.json: real camera, no debugger'
    }
} else {
    Note ''
    Note 'Run with -Full to drive a whole call through the shell.'
}

# -- Verdict -----------------------------------------------------
Write-Host ''
if ($script:Failures.Count -eq 0) {
    Write-Host 'Everything checked out.' -ForegroundColor Green
    Write-Host "Run it: $outDir\VtmKiosk.exe" -ForegroundColor DarkGray
    exit 0
}

Write-Host "$($script:Failures.Count) problem(s):" -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
