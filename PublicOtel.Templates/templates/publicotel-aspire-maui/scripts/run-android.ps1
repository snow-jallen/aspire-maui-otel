<#
.SYNOPSIS
    Builds and launches the MAUI app on an Android emulator, working around a bug in
    Aspire.Hosting.Maui 13.5.3-preview.1.

.DESCRIPTION
    Run this with the AppHost already running (aspire start). It does the whole Android
    launch in one step:

      1. Starts the mobile-android-emulator resource, unless a recent environment file
         already exists. That resource pre-builds the app and writes an environment targets
         file carrying the OTLP endpoint and service discovery values, and then fails with
         NETSDK1085. THAT FAILURE IS EXPECTED AND IS NOT A PROBLEM - see below.
      2. Waits for the environment file to appear.
      3. Launches the app with the same command Aspire uses, minus the one flag that breaks.

    Why the resource fails on its own:

    Aspire launches Android with a command equivalent to

        dotnet build (no restore) /t:Run -p:NoBuild=true <csproj> -f net10.0-android ...

    On Windows that is fine, because /t:Run resolves to the SDK's Run target, which never
    reaches Build. On Android, Run depends on Install, which pulls in the full build chain,
    so Build IS invoked and the SDK's _CheckForBuildWithNoBuild guard hard-errors with
    NETSDK1085. NoBuild arrives as an MSBuild global property and the guard target is
    defined after any user Directory.Build.targets, so it cannot be overridden from the
    repo. This script simply omits that flag.

    The environment file is consumed at BUILD time, so omitting NoBuild is also what lets
    the OTLP and service discovery values reach the APK at all.

.PARAMETER SkipStart
    Do not start the resource; use the newest existing environment file as-is.

.EXAMPLE
    ./scripts/run-android.ps1
#>
[CmdletBinding()]
param(
    [string]$Project,
    [string]$EnvTargets,
    [ValidateSet('emulator', 'device')]
    [string]$Target = 'emulator',
    [string]$Configuration = 'Debug',
    [string]$Framework = 'net10.0-android',
    [string]$ResourceName = 'mobile-android-emulator',
    [switch]$SkipStart,
    [int]$TimeoutSeconds = 600,
    [string]$Avd,
    [switch]$NoEmulatorLaunch,
    [int]$EmulatorTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-AndroidSdkRoot {
    foreach ($candidate in @(
            $env:ANDROID_HOME,
            $env:ANDROID_SDK_ROOT,
            "$env:LOCALAPPDATA\Android\Sdk",
            "${env:ProgramFiles(x86)}\Android\android-sdk",
            "$env:ProgramFiles\Android\android-sdk")) {
        if ($candidate -and (Test-Path (Join-Path $candidate 'platform-tools\adb.exe'))) {
            return $candidate
        }
    }
    return $null
}

function Get-ReadyDeviceSerial {
    <# Returns the serial of an attached, fully-booted target of the requested kind. #>
    param([string]$Adb, [string]$Kind)
    $rows = & $Adb devices 2>$null | Select-Object -Skip 1
    foreach ($row in $rows) {
        if ($row -match '^(\S+)\s+device\s*$') {
            $serial = $Matches[1]
            $isEmulator = $serial -like 'emulator-*'
            if (($Kind -eq 'emulator') -eq $isEmulator) { return $serial }
        }
    }
    return $null
}

function Start-AndroidEmulator {
    <#
        Launches an AVD without blocking, so it boots while the app is being built. Returns
        the emulator.exe path used, or $null if nothing could be started.
    #>
    param([string]$SdkRoot, [string]$AvdName)

    $emulatorExe = Join-Path $SdkRoot 'emulator\emulator.exe'
    if (-not (Test-Path $emulatorExe)) {
        Write-Warning "No emulator.exe under $SdkRoot. Start an emulator manually, or pass -Target device."
        return $null
    }

    $avds = @(& $emulatorExe -list-avds 2>$null | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() })
    if ($avds.Count -eq 0) {
        Write-Warning "No Android virtual devices are defined. Create one in Android Studio's Device Manager, then re-run."
        return $null
    }

    if ($AvdName) {
        if ($avds -notcontains $AvdName) {
            throw "AVD '$AvdName' not found. Available: $($avds -join ', ')"
        }
    }
    else {
        $AvdName = $avds[0]
    }

    Write-Host "Starting Android emulator '$AvdName' (it will boot while the app builds)..." -ForegroundColor Cyan
    if ($avds.Count -gt 1) {
        Write-Host "  Other AVDs available: $(($avds | Where-Object { $_ -ne $AvdName }) -join ', '). Pick one with -Avd <name>." -ForegroundColor DarkGray
    }
    Start-Process -FilePath $emulatorExe -ArgumentList @('-avd', $AvdName) -WindowStyle Minimized | Out-Null
    return $emulatorExe
}

function Wait-ForAndroidTarget {
    <# Polls until a target is attached AND sys.boot_completed reports 1. #>
    param([string]$Adb, [string]$Kind, [int]$TimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $announced = $false

    while ((Get-Date) -lt $deadline) {
        $serial = Get-ReadyDeviceSerial -Adb $Adb -Kind $Kind
        if ($serial) {
            # Attached is not the same as ready: installs fail during boot.
            $booted = (& $Adb -s $serial shell getprop sys.boot_completed 2>$null | Out-String).Trim()
            if ($booted -eq '1') {
                if ($announced) { Write-Host " ready ($serial)." -ForegroundColor Green }
                return $serial
            }
        }
        if (-not $announced) {
            Write-Host "Waiting for the Android $Kind to finish booting..." -NoNewline
            $announced = $true
        }
        Start-Sleep -Seconds 3
        Write-Host "." -NoNewline
    }

    Write-Host ""
    return $null
}

function Invoke-Aspire {
    # The Aspire CLI locates the AppHost from the current directory and does NOT scan
    # upward, so every call must run from the repo root regardless of the caller's cwd.
    param([string[]]$Arguments)
    Push-Location $repoRoot
    try { & aspire @Arguments 2>$null } finally { Pop-Location }
}

function Wait-ForDevTunnels {
    <#
        Right after 'aspire start' the DevTunnel resources are still Starting and their
        DevTunnelPort children are NotStarted. The environment file is generated from those
        tunnel URLs, so starting the Android resource too early produces a file with no
        usable OTLP endpoint - the app runs but nothing ever reaches the dashboard, silently.
    #>
    param([int]$TimeoutSeconds = 90)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $announced = $false

    while ((Get-Date) -lt $deadline) {
        $model = $null
        try { $model = (Invoke-Aspire @('describe', '--format', 'Json') | Out-String | ConvertFrom-Json) } catch { }

        if ($model -and $model.resources) {
            $tunnels = @($model.resources | Where-Object { $_.resourceType -in @('DevTunnel', 'DevTunnelPort') })
            if ($tunnels.Count -eq 0) { return }                                  # app has no tunnels
            $pending = @($tunnels | Where-Object { $_.state -ne 'Running' })
            if ($pending.Count -eq 0) {
                if ($announced) { Write-Host " ready." -ForegroundColor Green }
                return
            }
            if (-not $announced) {
                Write-Host "Waiting for dev tunnels ($($pending.Count) of $($tunnels.Count) not ready)..." -NoNewline
                $announced = $true
            }
        }
        Start-Sleep -Seconds 3
        if ($announced) { Write-Host "." -NoNewline }
    }

    # Not necessarily a problem: DevTunnelPort resources can sit in 'Starting' while working
    # perfectly well. The authoritative check is whether the generated environment file ends
    # up containing an OTLP endpoint, which Test-EnvTargetsUsable does below - so proceed
    # quietly rather than raising a false alarm here.
    if ($announced) { Write-Host " continuing." -ForegroundColor DarkGray }
}

function Test-EnvTargetsUsable {
    <#
        A file generated while the dev tunnels were failing still contains the static OTEL_*
        settings but NOT the endpoint or the service discovery keys. An app built with it
        runs fine and reports nothing, anywhere - the worst possible failure mode. Check
        content rather than trusting the file's age.
    #>
    param([string]$Path)
    if (-not $Path -or -not (Test-Path $Path)) { return $false }
    $text = Get-Content -Path $Path -Raw
    $hasOtlp = $text -match 'OTEL_EXPORTER_OTLP_ENDPOINT=\S+'
    $hasSvc  = $text -match 'SERVICES__[A-Z0-9_]+=\S+'
    return ($hasOtlp -and $hasSvc)
}

function Get-NewestEnvTargets {
    Get-ChildItem -Path $env:TEMP -Filter 'aspire-maui-android-env*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem -Path $_.FullName -Filter '*.targets' -File -ErrorAction SilentlyContinue } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

# --- Locate the MAUI project -------------------------------------------------------------
if (-not $Project) {
    # Skip build output, and skip any template pack that vendors a copy of the solution.
    # Match on the path RELATIVE to the app root: testing the absolute path breaks whenever
    # the app itself lives under a folder called bin, obj or templates - for example an app
    # generated straight into a template pack's bin\Release folder, where every candidate
    # would be filtered out and the script would claim there was no project at all.
    $rootPrefix = $repoRoot.TrimEnd('\') + '\'
    $candidates = @(Get-ChildItem -Path $repoRoot -Filter '*.Mobile.csproj' -Recurse -Depth 2 -File |
        Where-Object {
            $relative = $_.FullName.Substring($rootPrefix.Length)
            $relative -notmatch '(^|\\)(bin|obj|templates)(\\|$)'
        })
    if ($candidates.Count -eq 0) {
        throw "No *.Mobile.csproj found under $repoRoot. Run this from the app's own folder, or pass -Project explicitly."
    }
    if ($candidates.Count -gt 1) {
        throw "Found $($candidates.Count) candidate projects. Pass -Project explicitly:`n" +
              (($candidates | ForEach-Object { "  $($_.FullName)" }) -join "`n")
    }
    $Project = $candidates[0].FullName
}
if (-not (Test-Path $Project)) { throw "Project not found: $Project" }
Write-Host "Project : $Project"

# --- Get an Android target booting as early as possible -------------------------------------
# The build takes ~50s and a cold emulator ~60s, so kicking it off now means they overlap
# instead of queueing. Without this, /t:Run fails with:
#   error XA0010: Selected device is not running.
$sdkRoot = Get-AndroidSdkRoot
$adb = if ($sdkRoot) { Join-Path $sdkRoot 'platform-tools\adb.exe' } else { $null }

if (-not $adb) {
    Write-Warning "Could not locate the Android SDK (set ANDROID_HOME). Skipping the device check; the launch will fail if nothing is attached."
}
else {
    $ready = Get-ReadyDeviceSerial -Adb $adb -Kind $Target
    if ($ready) {
        Write-Host "Device  : $ready (already running)"
    }
    elseif ($Target -eq 'device') {
        Write-Warning "No physical Android device is attached. Plug one in with USB debugging enabled, or use -Target emulator."
    }
    elseif ($NoEmulatorLaunch) {
        Write-Warning "No emulator running and -NoEmulatorLaunch was set. The launch will fail unless you start one."
    }
    else {
        Start-AndroidEmulator -SdkRoot $sdkRoot -AvdName $Avd | Out-Null
    }
}

# --- Get an environment file ---------------------------------------------------------------
if ($EnvTargets -eq 'none') {
    $EnvTargets = $null
    Write-Warning "Launching with no Aspire wiring. Nothing will appear in the dashboard."
}
elseif (-not $EnvTargets) {
    $existing = Get-NewestEnvTargets
    $reusable = $existing -and
                ((Get-Date) - $existing.LastWriteTime).TotalMinutes -lt 10 -and
                (Test-EnvTargetsUsable $existing.FullName)

    if ($reusable -or $SkipStart) {
        if (-not $existing) { throw "No environment file found and -SkipStart was specified. Run without -SkipStart." }
        if ($SkipStart -and -not (Test-EnvTargetsUsable $existing.FullName)) {
            throw "The newest environment file has no OTLP endpoint or service discovery keys, so the app would report nothing. Re-run without -SkipStart."
        }
        $EnvTargets = $existing.FullName
        Write-Host "EnvFile : reusing $([IO.Path]::GetFileName($EnvTargets))"
    }
    else {
        $before = if ($existing) { $existing.LastWriteTime } else { [datetime]::MinValue }

        Write-Host ""
        Write-Host "Starting '$ResourceName' to pre-build the app and generate its environment file." -ForegroundColor Cyan
        Write-Host "It will report a NETSDK1085 build failure. That is expected - this script works around it." -ForegroundColor DarkGray
        Write-Host ""

        # The environment file is built from the dev tunnel URLs, so those must exist first.
        Wait-ForDevTunnels

        Push-Location $repoRoot
        try { & aspire resource $ResourceName start --non-interactive }
        finally { $startExit = $LASTEXITCODE; Pop-Location }

        if ($startExit -ne 0) {
            throw "Could not start '$ResourceName' (aspire exited $startExit). Is the AppHost running? Start it with: aspire start"
        }

        Write-Host "Waiting for the environment file..." -NoNewline
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            $candidate = Get-NewestEnvTargets
            if ($candidate -and $candidate.LastWriteTime -gt $before) { $EnvTargets = $candidate.FullName; break }
            Start-Sleep -Seconds 3
            Write-Host "." -NoNewline
        }
        Write-Host ""

        if (-not $EnvTargets) {
            throw @"
Timed out after $TimeoutSeconds seconds waiting for the Aspire environment file.

Check the '$ResourceName' resource logs in the dashboard. A build failure other than
NETSDK1085 (a missing MAUI workload, a missing Android SDK, or no emulator) would stop
the file from being written.
"@
        }
        if (-not (Test-EnvTargetsUsable $EnvTargets)) {
            throw @"
The environment file was generated but contains no OTLP endpoint and no service discovery
keys, so the app would launch and silently report nothing.

That means the dev tunnels are not up. Check the tunnel resources in the dashboard - the
usual causes are a first-time Microsoft login prompt, or:

    Rate limit exceeded. Please wait about a minute and try again.

Wait a minute, restart the AppHost, and run this again. The Windows target needs no tunnels
if you just want to see the telemetry working.
"@
        }
        Write-Host "EnvFile : $([IO.Path]::GetFileName($EnvTargets))" -ForegroundColor Green
    }
}

# --- Launch ---------------------------------------------------------------------------------
# Resolve the target BEFORE building the argument list: the serial is what makes AdbTarget
# unambiguous, so the wait has to happen first.
# The emulator was started before the build; make sure it has finished booting, because
# installing into a half-booted device fails.
$serial = $null
if ($adb) {
    $serial = Wait-ForAndroidTarget -Adb $adb -Kind $Target -TimeoutSeconds $EmulatorTimeoutSeconds
    if (-not $serial) {
        throw "No Android $Target became ready within $EmulatorTimeoutSeconds seconds. Check 'adb devices', or start the emulator manually and re-run with -NoEmulatorLaunch."
    }
}

# Target the exact serial we waited on. '-e' means "the only running emulator" and breaks
# when a second or stale entry exists - the failure looks like:
#   error XAFD7000: Mono.AndroidTools.AdbException: device 'emulator-5556' not found
$adbTarget = if ($serial) { "-s $serial" }
             elseif ($Target -eq 'emulator') { '-e' }
             else { '-d' }

$dotnetArgs = @(
    'build'
    '--no-restore'
    '/t:Run'                       # NOTE: deliberately no -p:NoBuild=true -- that is the bug.
    $Project
    '--configuration', $Configuration
    '-f', $Framework
    "-p:AdbTarget=$adbTarget"
)
if ($EnvTargets) { $dotnetArgs += "-p:CustomAfterMicrosoftCommonTargets=$EnvTargets" }

Write-Host ""
Write-Host "Building and launching on Android $Target..." -ForegroundColor Cyan
Write-Host "dotnet $($dotnetArgs -join ' ')" -ForegroundColor DarkGray
Write-Host ""

# Tee the output: show it live for a terminal user, and keep it so a failure can repeat the
# actual diagnostics. When this runs as the dashboard command there is no scrollback for the
# user to consult, so 'see the output above' is worthless there.
$output = & dotnet @dotnetArgs 2>&1 | Tee-Object -Variable captured
$exit = $LASTEXITCODE
$output | Out-Null

Write-Host ""
if ($exit -eq 0) {
    Write-Host "Running on Android $Target. Press Get Weather in the app; traces, logs and metrics appear in the dashboard under '$ResourceName'." -ForegroundColor Green
    exit 0
}

$lines = @($captured | ForEach-Object { "$_" })
$diagnostics = @($lines | Where-Object { $_ -match ': error |error XA|error APT' } | Select-Object -Unique -First 6)

Write-Host "Launch failed (dotnet exit code $exit)." -ForegroundColor Red
if ($diagnostics) {
    Write-Host ""
    $diagnostics | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}

# The most common cause once the build itself is fine.
if ($lines -match 'XA0010|Selected device is not running|no devices|device not found|INSTALL_FAILED|XA5306') {
    Write-Host ""
    Write-Host "Hint: no Android $Target is running." -ForegroundColor Yellow
    Write-Host "      Start one first, then re-run this script:" -ForegroundColor Yellow
    Write-Host "        adb devices                 # want a line ending in 'device'" -ForegroundColor Yellow
    Write-Host "        emulator -list-avds         # then: emulator -avd <name>" -ForegroundColor Yellow
    Write-Host "      Or launch the emulator from Android Studio's Device Manager." -ForegroundColor Yellow
}

exit $exit
