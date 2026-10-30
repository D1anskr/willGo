param([string]$ModelBaseUrl = 'https://hf-mirror.com/sakasegawa/Qwen3-ASR-1.7B-GGUF/resolve/81e2423d24073cdeeccb5951df16accc89e075a2')

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$target = Join-Path $repoRoot 'src/FloatingDeskAssistant/SpeechModels/Qwen3-ASR-1.7B'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$model = Join-Path $target 'Qwen3-ASR-1.7B-Q8_0.gguf'
$modelHash = '5F219B78A1D9C3B9E97DA27708B36F8A0BC1BFC1650B541C0A6DBAF87C9A62D0'
if (!(Test-Path $model) -or (Get-FileHash $model).Hash -ne $modelHash) {
    $partial = "$model.download"
    & curl.exe --ssl-revoke-best-effort -sS -L --fail --retry 2 --connect-timeout 20 --max-time 1800 "$ModelBaseUrl/Qwen3-ASR-1.7B-Q8_0.gguf" -o $partial
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash $partial).Hash -ne $modelHash) { throw 'Qwen3-ASR model checksum failed.' }
    Move-Item -LiteralPath $partial -Destination $model -Force
}
$exe = Join-Path $target 'speech.exe'
$exeHash = '8FF9BC3F27CE2901E1AB9D1C59463EC51125AD6B901E6A7061EFCCF0D75DA512'
if (!(Test-Path $exe) -or (Get-FileHash $exe).Hash -ne $exeHash) {
    $stage = Join-Path $target ('download-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        $zip = Join-Path $stage 'runtime.zip'
        & curl.exe --ssl-revoke-best-effort -sS -L --fail --retry 2 --max-time 180 -H 'User-Agent: willGo' -H 'Accept: application/octet-stream' 'https://api.github.com/repos/nyosegawa/speech.cpp/releases/assets/650023768' -o $zip
        if ($LASTEXITCODE -ne 0 -or (Get-FileHash $zip).Hash -ne '2904DD54DE7F18CF438338C28216EBFFC205D29C6A2DE3E645AF40DD136B92F2') { throw 'speech.cpp runtime checksum failed.' }
        Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
        $binary = Join-Path $stage 'speech.exe'
        if ((Get-FileHash $binary).Hash -ne $exeHash) { throw 'speech.cpp executable checksum failed.' }
        Copy-Item -LiteralPath $binary -Destination $exe -Force
    }
    finally {
        $resolvedStage = [IO.Path]::GetFullPath($stage)
        if (!$resolvedStage.StartsWith([IO.Path]::GetFullPath($target) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected download staging path.' }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
Write-Host "Verified Qwen3-ASR-1.7B Q8 model and Vulkan runtime: $target"
