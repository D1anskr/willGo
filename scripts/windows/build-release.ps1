param(
    [ValidateSet('Patch', 'Minor', 'Major')]
    [string]$Bump = 'Patch',
    [string]$Version,
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
    $versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        return '1.0.0'
    }

    return $versionNode.InnerText
}

function Set-ProjectVersion {
    param(
        [string]$ProjectPath,
        [string]$NewVersion
    )

    [xml]$projectXml = Get-Content $ProjectPath -Raw
    $propertyGroup = $projectXml.SelectSingleNode('/Project/PropertyGroup')
    if ($null -eq $propertyGroup) {
        $propertyGroup = $projectXml.CreateElement('PropertyGroup')
        [void]$projectXml.Project.AppendChild($propertyGroup)
    }

    $versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode) {
        $versionNode = $projectXml.CreateElement('Version')
        [void]$propertyGroup.AppendChild($versionNode)
    }

    $versionNode.InnerText = $NewVersion

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.IndentChars = '  '
    $settings.OmitXmlDeclaration = $true
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace

    $writer = [System.Xml.XmlWriter]::Create($ProjectPath, $settings)
    try {
        $projectXml.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function Get-NextVersion {
    param(
        [string]$CurrentVersion,
        [string]$BumpKind
    )

    if ($CurrentVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "Current version '$CurrentVersion' is not in supported format 'major.minor.patch'."
    }

    $parts = $CurrentVersion.Split('.')
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]

    switch ($BumpKind) {
        'Major' {
            $major += 1
            $minor = 0
            $patch = 0
        }
        'Minor' {
            $minor += 1
            $patch = 0
        }
        'Patch' {
            $patch += 1
        }
        default {
            throw "Unsupported bump kind: $BumpKind"
        }
    }

    return "$major.$minor.$patch"
}

function Test-ExplicitVersion {
    param([string]$TargetVersion)

    if ($TargetVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "Explicit version '$TargetVersion' is not in supported format 'major.minor.patch'."
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$projectPath = Join-Path $repoRoot 'src/FloatingDeskAssistant/FloatingDeskAssistant.csproj'
$setupScript = Join-Path $repoRoot 'scripts/windows/build-setup-exe.ps1'
$currentVersion = Get-ProjectVersion -ProjectPath $projectPath

if ([string]::IsNullOrWhiteSpace($Version)) {
    $targetVersion = Get-NextVersion -CurrentVersion $currentVersion -BumpKind $Bump
}
else {
    Test-ExplicitVersion -TargetVersion $Version
    $targetVersion = $Version
}

if ($targetVersion -eq $currentVersion) {
    throw "Target version matches current version ($currentVersion). Use a different version or omit -Version to auto-bump."
}

Write-Step "Bumping version $currentVersion -> $targetVersion"
Set-ProjectVersion -ProjectPath $projectPath -NewVersion $targetVersion

$setupPath = Join-Path $repoRoot "dist/willGo-Setup-$targetVersion-$Runtime.exe"
$zipPath = Join-Path $repoRoot "dist/willGo-$targetVersion-$Runtime.zip"
$packageDir = Join-Path $repoRoot "dist/willGo-$targetVersion-$Runtime"

try {
    Write-Step 'Building fresh release artifacts'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $setupScript -Runtime $Runtime -Configuration $Configuration
    if ((-not $?) -or $LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path $setupPath)) {
        throw "Expected setup executable was not created: $setupPath"
    }

    Write-Step 'Release completed'
    Write-Host "Version: $targetVersion" -ForegroundColor Green
    Write-Host "Setup:   $setupPath" -ForegroundColor Green
    Write-Host "Zip:     $zipPath" -ForegroundColor Green
    Write-Host "Folder:  $packageDir" -ForegroundColor Green
}
catch {
    Write-Warning "Release build failed. Reverting project version back to $currentVersion."
    Set-ProjectVersion -ProjectPath $projectPath -NewVersion $currentVersion
    throw
}
