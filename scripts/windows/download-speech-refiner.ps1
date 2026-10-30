param([string]$ModelBaseUrl = 'https://modelscope.cn/models/FunAudioLLM/Fun-ASR-Nano-GGUF/resolve/51dcf4922439c10e0c2e59bc99be8a343d2fe71f')

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$target = Join-Path $repoRoot 'src/FloatingDeskAssistant/SpeechModels/Fun-ASR-Nano-GGUF'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$models = @(
    @('funasr-encoder-f16.gguf', 'F92F91D01A24FBED6C863495B2EE8C6A6788144A02858B75743F0946668DE8A2'),
    @('qwen3-0.6b-q8_0.gguf', '819F385DC0E035DCCC3D9E7EDAF6B7B044B8BA7ACE63CBCBF84C7E397EECBF27')
)
foreach ($model in $models) {
    $path = Join-Path $target $model[0]
    if ((Test-Path $path) -and (Get-FileHash $path).Hash -eq $model[1]) { continue }
    $partial = "$path.download"
    & curl.exe --ssl-revoke-best-effort -sS -L --fail --retry 2 --connect-timeout 20 --max-time 1200 "$ModelBaseUrl/$($model[0])" -o $partial
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash $partial).Hash -ne $model[1]) { throw "Model download/checksum failed: $($model[0])" }
    Move-Item -LiteralPath $partial -Destination $path -Force
}
$exe = Join-Path $target 'llama-funasr-cli.exe'
if (!(Test-Path $exe) -or (Get-FileHash $exe).Hash -ne 'C789FF8B69470B75B6A8FA25FDC7987C4A0D4DFF7FD8795D45817EA2575584D0') {
    $stage = Join-Path $target ('download-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        $zip = Join-Path $stage 'runtime.zip'
        & curl.exe --ssl-revoke-best-effort -sS -L --fail --retry 2 --max-time 180 -H 'User-Agent: willGo' -H 'Accept: application/octet-stream' 'https://api.github.com/repos/modelscope/FunASR/releases/assets/536126544' -o $zip
        if ($LASTEXITCODE -ne 0 -or (Get-FileHash $zip).Hash -ne '062CDA8FEFADD31C3E811227116DACCF448A8520F4B0BB168D225C896E65EBBD') { throw 'Runtime checksum failed' }
        Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
        $binary = Join-Path $stage 'llama-funasr-cli.exe'
        if ((Get-FileHash $binary).Hash -ne 'C789FF8B69470B75B6A8FA25FDC7987C4A0D4DFF7FD8795D45817EA2575584D0') { throw 'Runtime executable checksum failed' }
        Copy-Item -LiteralPath $binary -Destination $exe -Force
    }
    finally {
        $resolvedStage = [IO.Path]::GetFullPath($stage)
        if ($resolvedStage.StartsWith([IO.Path]::GetFullPath($target) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
    }
}
Write-Host "Verified final speech model and runtime: $target"
