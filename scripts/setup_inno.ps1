param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$version = '6.7.3'
$expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$destinationPath = [IO.Path]::GetFullPath($Destination)
$download = Join-Path ([IO.Path]::GetTempPath()) "daily-wallpaper-innosetup-$version.exe"

Invoke-WebRequest "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-$version.exe" -OutFile $download
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Inno Setup download checksum mismatch'
}
$installation = Start-Process -FilePath $download -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS',
    '/MERGETASKS=!fileassoc', ('/DIR="' + $destinationPath + '"')
) -WindowStyle Hidden -Wait -PassThru
if ($installation.ExitCode -ne 0) { throw "Inno Setup installation failed: $($installation.ExitCode)" }

$compiler = Join-Path $destinationPath 'ISCC.exe'
$engine = Join-Path $destinationPath 'ISCmplr.dll'
if (-not (Test-Path -LiteralPath $compiler) -or -not (Test-Path -LiteralPath $engine)) {
    throw "Inno Setup compiler missing in $destinationPath"
}
# ISCC.exe and ISCmplr.dll report 0.0.0.0 in their Windows file metadata.
# Query the actual compiler engine with a minimal preprocessor-only build.
@'
#if Ver != EncodeVer(6, 7, 3)
  #error Expected Inno Setup 6.7.3
#endif
[Setup]
AppName=Compiler version check
AppVersion=1.0
CreateAppDir=no
Uninstallable=no
Output=no
'@ | & $compiler /Q -
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compiler version verification failed' }
Write-Output "Installed Inno Setup $version at $compiler"
