param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$SevenZipPath = '',
    [switch]$ReusePackage
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
if (!$ReusePackage) {
    $distributionArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $distributionScript, '-Runtime', $Runtime, '-Configuration', $Configuration, '-Zip')
    if ($SevenZipPath) { $distributionArgs += @('-SevenZipPath', $SevenZipPath) }
    & powershell @distributionArgs
    if ((-not $?) -or $LASTEXITCODE -ne 0) { throw "Package build failed with exit code $LASTEXITCODE" }
}
elseif (!(Test-Path $packageZip) -or !(Test-Path (Join-Path $packageRoot 'package-info.json'))) {
    throw 'No completed package exists to reuse.'
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

if ((Get-Item $packageZip).Length -gt 900MB) {
    Write-Step 'Building large self-extracting setup'
    if (!$SevenZipPath) { $SevenZipPath = (Get-Command 7z.exe -ErrorAction Stop).Source }
    $module = Join-Path $PSScriptRoot 'installer/7zSD.sfx'
    if (!(Test-Path $module) -or (Get-FileHash $module).Hash -ne '837A2D84A4D15366AF7A91670908AA29A958B721F40DB46DA734621F6234FEA2') {
        throw 'Missing or invalid pinned LZMA SDK 26.04 installer module.'
    }
    Copy-Item (Join-Path $PSScriptRoot 'package/bootstrap-large.ps1') (Join-Path $stageRoot 'bootstrap-install.ps1')
    $archive = Join-Path $stageRoot 'setup.7z'
    Push-Location $stageRoot
    try {
        & $SevenZipPath a -t7z -mx=0 $archive 'bootstrap-install.ps1' 'payload.zip' -bso0 -bsp0
        if ($LASTEXITCODE -ne 0) { throw 'Large setup archive creation failed.' }
    }
    finally { Pop-Location }
    $config = [Text.Encoding]::UTF8.GetBytes(";!@Install@!UTF-8!`r`nTitle=`"willGo Setup`"`r`nDirectory=`"`"`r`nRunProgram=`"powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File bootstrap-install.ps1`"`r`n;!@InstallEnd@!`r`n")
    $output = [IO.File]::Create($outputExe)
    try {
        $input = [IO.File]::OpenRead($module)
        try { $input.CopyTo($output) } finally { $input.Dispose() }
        $output.Write($config, 0, $config.Length)
        $input = [IO.File]::OpenRead($archive)
        try { $input.CopyTo($output) } finally { $input.Dispose() }
    }
    finally { $output.Dispose() }
    & $SevenZipPath t $outputExe -bso0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw 'Large setup integrity check failed.' }
    $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
    if (!$resolvedStage.StartsWith([IO.Path]::GetFullPath((Join-Path $repoRoot 'dist/setup-staging')) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected staging path' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    Write-Host "Setup executable created: $outputExe" -ForegroundColor Green
    exit 0
}

if (Test-Path $outputExe) {
    Remove-Item -Path $outputExe -Force
}

Write-Step 'Building single-file Setup.exe'
$iexpressPath = Join-Path $env:WINDIR 'SysWOW64/iexpress.exe'
if (!(Test-Path $iexpressPath)) { $iexpressPath = (Get-Command iexpress.exe).Source }
$setupBuild = Start-Process -FilePath $iexpressPath -ArgumentList @('/N', '/Q', 'willGo-setup.sed') `
    -WorkingDirectory $stageRoot -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $stageRoot 'build.stdout.log') `
    -RedirectStandardError (Join-Path $stageRoot 'build.stderr.log')
if ($setupBuild.ExitCode -ne 0 -or -not (Test-Path $outputExe)) {
    throw 'IExpress failed to create the setup executable.'
}

Write-Step 'Cleaning staging'
Remove-Item -Path $stageRoot -Recurse -Force

Write-Step 'Completed'
Write-Host "Setup executable created: $outputExe" -ForegroundColor Green
