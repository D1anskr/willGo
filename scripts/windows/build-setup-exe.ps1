param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content $ProjectPath -Raw
    $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$versionNode)) {
        return '1.0.0'
    }

    return [string]$versionNode
}

function New-BootstrapScript {
    param([string]$Path)

    $content = @"
@echo off
setlocal
set "PAYLOAD_ZIP=%~dp0payload.zip"
set "EXTRACT_DIR=%TEMP%\willGo-setup-%RANDOM%%RANDOM%"

if exist "%EXTRACT_DIR%" rmdir /s /q "%EXTRACT_DIR%"
mkdir "%EXTRACT_DIR%"

powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%PAYLOAD_ZIP%' -DestinationPath '%EXTRACT_DIR%' -Force"
if errorlevel 1 (
  echo Failed to extract willGo setup payload.
  pause
  exit /b 1
)

if not exist "%EXTRACT_DIR%\tools\install.ps1" (
  echo Missing extracted installer entrypoint.
  rmdir /s /q "%EXTRACT_DIR%"
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%EXTRACT_DIR%\tools\install.ps1"
set "EXITCODE=%ERRORLEVEL%"

rmdir /s /q "%EXTRACT_DIR%" >nul 2>nul
exit /b %EXITCODE%
"@

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $content, $encoding)
}

function New-SedFile {
    param(
        [string]$Path,
        [string]$StageDir,
        [string]$TargetPath
    )

    $escapedStageDir = $StageDir.TrimEnd('\') + '\'
    $content = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$TargetPath
FriendlyName=willGo Setup
AppLaunched=cmd.exe /d /s /c ""bootstrap-install.cmd""
PostInstallCmd=<None>
AdminQuietInstCmd=cmd.exe /d /s /c ""bootstrap-install.cmd""
UserQuietInstCmd=cmd.exe /d /s /c ""bootstrap-install.cmd""
FILE0="bootstrap-install.cmd"
FILE1="payload.zip"
[SourceFiles]
SourceFiles0=$escapedStageDir
[SourceFiles0]
%FILE0%=
%FILE1%=
"@

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $content, $encoding)
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$distributionScript = Join-Path $repoRoot 'scripts/windows/build-distribution.ps1'
$projectPath = Join-Path $repoRoot 'src/FloatingDeskAssistant/FloatingDeskAssistant.csproj'
$version = Get-ProjectVersion -ProjectPath $projectPath
$packageRoot = Join-Path $repoRoot "dist/willGo-$version-$Runtime"
$packageZip = "$packageRoot.zip"
$outputExe = Join-Path $repoRoot "dist/willGo-Setup-$version-$Runtime.exe"
$stageRoot = Join-Path $repoRoot "dist/setup-staging/$version-$Runtime"
$bootstrapPath = Join-Path $stageRoot 'bootstrap-install.cmd'
$payloadPath = Join-Path $stageRoot 'payload.zip'
$sedPath = Join-Path $stageRoot 'willGo-setup.sed'

Write-Step 'Building fresh package payload'
& powershell -NoProfile -ExecutionPolicy Bypass -File $distributionScript -Runtime $Runtime -Configuration $Configuration -Zip
if ((-not $?) -or $LASTEXITCODE -ne 0) {
    throw "Package build failed with exit code $LASTEXITCODE"
}

if (-not (Get-Command iexpress.exe -ErrorAction SilentlyContinue)) {
    throw 'IExpress.exe is not available on this Windows machine.'
}

Write-Step 'Preparing setup staging'
if (Test-Path $stageRoot) {
    Remove-Item -Path $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
Copy-Item -Path $packageZip -Destination $payloadPath -Force
New-BootstrapScript -Path $bootstrapPath
New-SedFile -Path $sedPath -StageDir $stageRoot -TargetPath $outputExe

if (Test-Path $outputExe) {
    Remove-Item -Path $outputExe -Force
}

Write-Step 'Building single-file Setup.exe'
& iexpress.exe /N $sedPath | Out-Null
if ((-not $?) -or -not (Test-Path $outputExe)) {
    throw 'IExpress failed to create the setup executable.'
}

Write-Step 'Cleaning staging'
Remove-Item -Path $stageRoot -Recurse -Force

Write-Step 'Completed'
Write-Host "Setup executable created: $outputExe" -ForegroundColor Green