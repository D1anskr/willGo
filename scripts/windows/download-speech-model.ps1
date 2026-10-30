param([string]$ArchiveUrl = '', [string]$SevenZipPath = '')

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$modelRoot = Join-Path $repoRoot 'src/FloatingDeskAssistant/SpeechModels'
$modelName = 'sherpa-onnx-x-asr-480ms-streaming-zipformer-transducer-zh-en-punct-int8-2026-06-05'
$modelPath = Join-Path $modelRoot $modelName
$expectedHash = 'FA5F63D618E5A01526E275A358BB7772E403F84808A4769FBA52CFFD8160BF74'
$files = @('encoder.int8.onnx', 'decoder.onnx', 'joiner.int8.onnx', 'tokens.txt')
if (@($files | Where-Object { !(Test-Path (Join-Path $modelPath $_)) }).Count -eq 0) {
    Write-Host "Speech model is present: $modelPath"
    exit 0
}
New-Item -ItemType Directory -Force -Path $modelRoot | Out-Null
if (!$ArchiveUrl) { $ArchiveUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/$modelName.tar.bz2" }
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ('willGo-x-asr-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$archive = Join-Path $stage 'model.tar.bz2'
try {
    Invoke-WebRequest -UseBasicParsing $ArchiveUrl -OutFile $archive -TimeoutSec 600
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Speech model checksum mismatch. The downloaded archive will not be installed.'
    }
    if ($SevenZipPath) {
        & $SevenZipPath x -y "-o$stage" $archive
        if ($LASTEXITCODE -ne 0) { throw 'Speech model bzip2 extraction failed.' }
        & $SevenZipPath x -y "-o$stage" (Join-Path $stage 'model.tar')
        if ($LASTEXITCODE -ne 0) { throw 'Speech model tar extraction failed.' }
    }
    else {
        $extractor = Start-Process -FilePath 'tar.exe' -ArgumentList @('-xjf', ('"' + $archive + '"'), '-C', ('"' + $stage + '"')) -WindowStyle Hidden -PassThru
        if (!$extractor.WaitForExit(120000)) {
            $extractor.Kill()
            $extractor.WaitForExit()
            throw 'tar.exe timed out. Retry with -SevenZipPath pointing to 7z.exe.'
        }
        if ($extractor.ExitCode -ne 0) { throw 'Speech model archive extraction failed.' }
    }
    foreach ($file in $files) {
        if (!(Test-Path (Join-Path (Join-Path $stage $modelName) $file))) { throw "Missing model file: $file" }
    }
    Copy-Item -LiteralPath (Join-Path $stage $modelName) -Destination $modelRoot -Recurse -Force
    Write-Host "Speech model installed: $modelPath"
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
