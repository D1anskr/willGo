$ErrorActionPreference = 'Stop'
$extractDir = Join-Path ([IO.Path]::GetTempPath()) ('willGo-setup-' + [Guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath (Join-Path $PSScriptRoot 'payload.zip') -DestinationPath $extractDir -Force
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $extractDir 'tools/install.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Installation failed with exit code $LASTEXITCODE" }
}
catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'willGo Setup') | Out-Null
    exit 1
}
finally {
    $resolved = [IO.Path]::GetFullPath($extractDir)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
