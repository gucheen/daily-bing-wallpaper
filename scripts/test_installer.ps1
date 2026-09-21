param([string]$Iscc)
$ErrorActionPreference = 'Stop'
if (-not $Iscc) {
    $Iscc = @($env:ISCC, "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $Iscc) { throw 'Inno Setup compiler not found; use -Iscc PATH' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$testId = 'DWTest.' + [guid]::NewGuid().ToString('N')
$testRoot = Join-Path $projectRoot "artifacts\$testId"
$installDirectory = Join-Path $testRoot 'Installed App With Spaces'
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$testId.lnk"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${testId}_is1"
$runtimeInfo = Get-Content (Join-Path $projectRoot 'installer\runtime.json') -Raw | ConvertFrom-Json
$dependency = $runtimeInfo.downloads.'win-x64'
[xml]$project = Get-Content (Join-Path $projectRoot 'src\DailyWallpaper.Windows\DailyWallpaper.Windows.csproj')
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Build-TestInstaller([string]$Architecture, [string]$Name) {
    & $Iscc '/Q' "/O$testRoot" "/F$Name" "/DAppId=$testId" "/DAppName=$testId" "/DStartupValueName=$testId" `
        "/DAppVersion=$($project.Project.PropertyGroup.Version)" '/DRuntime=win-x64' `
        '/DAllowedArchitecture=x64compatible' "/DDotNetArchitecture=$Architecture" `
        "/DRuntimeUrl=$($dependency.url)" "/DRuntimeSHA256=$($dependency.sha256)" `
        (Join-Path $projectRoot 'installer\DailyWallpaper.iss')
    Assert-True ($LASTEXITCODE -eq 0) 'Test installer compilation failed'
}

function Invoke-Setup([string]$Exe, [string[]]$Options, [bool]$ExpectSuccess = $true) {
    Write-Output "Testing: $Exe $Options"
    $process = Start-Process -FilePath $Exe -ArgumentList $Options -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) {
        & taskkill /PID $process.Id /T /F | Out-Null
        throw 'Installer did not finish within 30 seconds'
    }
    if ($ExpectSuccess) { Assert-True ($process.ExitCode -eq 0) "Installer failed: $Exe (exit $($process.ExitCode))" }
    else { Assert-True ($process.ExitCode -ne 0) 'Missing runtime should stop unattended installation' }
    if ((Split-Path $Exe -Leaf) -eq 'unins000.exe') {
        # Inno removes its original uninstaller via a short-lived helper after exit.
        $cleanupTimer = [Diagnostics.Stopwatch]::StartNew()
        while ((Test-Path $Exe) -and $cleanupTimer.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 100 }
        Assert-True (-not (Test-Path $Exe)) 'Uninstaller self-cleanup did not finish'
    }
}

function Get-TestStartup {
    (Get-ItemProperty -LiteralPath $startupKey -ErrorAction SilentlyContinue).$testId
}

$quiet = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
$setup = Join-Path $testRoot 'InstallerSmoke.exe'
$uninstaller = Join-Path $installDirectory 'unins000.exe'
$shutdownHost = $null
try {
    # Isolate all registry values, shortcuts, app registration and files from the real app.
    Build-TestInstaller 'installer-test-missing' 'MissingRuntime'
    Invoke-Setup (Join-Path $testRoot 'MissingRuntime.exe') ($quiet + @("/DIR=`"$installDirectory`"", "/LOG=`"$testRoot\missing.log`"")) $false
    Assert-True (-not (Test-Path (Join-Path $installDirectory 'DailyWallpaper.exe'))) 'App installed without its runtime'
    Assert-True (-not (Test-Path $uninstallKey)) 'Failed installation registered an uninstaller'

    Build-TestInstaller 'x64' 'InstallerSmoke'
    Invoke-Setup $setup ($quiet + @("/DIR=`"$installDirectory`"", '/TASKS=startup', "/LOG=`"$testRoot\install.log`""))
    $expected = '"' + (Join-Path $installDirectory 'DailyWallpaper.exe') + '"'
    Assert-True ((Get-TestStartup) -eq $expected) 'Startup command missing or incorrectly quoted'
    Assert-True (Test-Path $shortcut) 'Start menu shortcut missing'
    Assert-True (Test-Path $uninstallKey) 'Uninstall registration missing'
    Assert-True ((Get-Content "$testRoot\install.log" -Raw) -match 'download skipped') 'Installed runtime was not detected'

    & dotnet build (Join-Path $projectRoot 'tests\DailyWallpaper.Windows.Tests') -c Release
    Assert-True ($LASTEXITCODE -eq 0) 'Shutdown test host build failed'
    $hostExe = Join-Path $projectRoot 'tests\DailyWallpaper.Windows.Tests\bin\Release\net10.0-windows\DailyWallpaper.Windows.Tests.exe'
    $shutdownHost = Start-Process -FilePath $hostExe -ArgumentList @('--shutdown-host',
        ('"' + (Join-Path $installDirectory 'DailyWallpaper.exe') + '"'), ('"' + $testRoot + '"')) -WindowStyle Hidden -PassThru
    $readyTimer = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path "$testRoot\ready") -and -not $shutdownHost.HasExited -and $readyTimer.Elapsed.TotalSeconds -lt 15) {
        Start-Sleep -Milliseconds 100
    }
    Assert-True ((Test-Path "$testRoot\ready") -and -not $shutdownHost.HasExited) 'Application context did not start'
    Invoke-Setup $setup ($quiet + @("/DIR=`"$installDirectory`"", '/TASKS=startup',
        '/CLOSEAPPLICATIONS', '/NOFORCECLOSEAPPLICATIONS', '/RESTARTEXITCODE=3010', "/LOG=`"$testRoot\running-upgrade.log`""))
    Assert-True ($shutdownHost.WaitForExit(10000)) 'Running application did not exit during upgrade'
    Assert-True ($shutdownHost.ExitCode -eq 0) 'Running application failed during shutdown'
    Assert-True (Test-Path "$testRoot\cancelled") 'Shutdown did not cancel the active download'
    Assert-True (Test-Path "$testRoot\stopped") 'Installer did not close the application gracefully'

    Invoke-Setup $setup ($quiet + @("/DIR=`"$installDirectory`"", '/TASKS=!startup'))
    Assert-True ($null -eq (Get-TestStartup)) 'Unchecking startup on upgrade did not remove it'
    Invoke-Setup $setup ($quiet + @("/DIR=`"$installDirectory`"", '/TASKS=startup'))
    Assert-True ((Get-TestStartup) -eq $expected) 'Re-enabling startup failed'
    Invoke-Setup $uninstaller $quiet
    Assert-True ($null -eq (Get-TestStartup)) 'Uninstall left its startup entry'
    Assert-True (-not (Test-Path $shortcut)) 'Uninstall left its shortcut'
    Assert-True (-not (Test-Path (Join-Path $installDirectory 'DailyWallpaper.exe'))) 'Uninstall left its executable'
    Assert-True (-not (Test-Path $uninstallKey)) 'Uninstall left its registration'

    Invoke-Setup $setup ($quiet + @("/DIR=`"$installDirectory`"", '/TASKS=startup'))
    $otherInstallation = '"C:\Another Location\DailyWallpaper.exe"'
    Set-ItemProperty -LiteralPath $startupKey -Name $testId -Value $otherInstallation
    Invoke-Setup $uninstaller $quiet
    Assert-True ((Get-TestStartup) -eq $otherInstallation) 'Uninstall removed another installation startup entry'
    Write-Output 'PASS: missing-runtime refusal, runtime detection, running upgrade with graceful shutdown and download cancellation, startup selection, uninstall, other-installation preservation'
} finally {
    try {
        if ($shutdownHost -and -not $shutdownHost.HasExited) { Stop-Process -Id $shutdownHost.Id -Force }
        if (Test-Path $uninstaller) { Invoke-Setup $uninstaller $quiet }
    } finally {
        Remove-ItemProperty -LiteralPath $startupKey -Name $testId -ErrorAction SilentlyContinue
    }
}
