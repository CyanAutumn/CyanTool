param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$FilePath failed with exit code $LASTEXITCODE" }
}

function Get-BootstrapUv {
    param([string]$ProjectRoot)
    $uvRoot = Join-Path $ProjectRoot "dist\runtime-tools\uv"
    $uvExe = Join-Path $uvRoot "uv.exe"
    if (Test-Path -LiteralPath $uvExe) { return $uvExe }

    $existing = Get-Command "uv.exe" -ErrorAction SilentlyContinue
    if ($existing) { return $existing.Source }
    New-Item -ItemType Directory -Force -Path $uvRoot | Out-Null
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("cyantools-uv-" + [Guid]::NewGuid().ToString("N"))
    try {
        New-Item -ItemType Directory -Force -Path $temporary | Out-Null
        $archive = Join-Path $temporary "uv.zip"
        Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip" -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $temporary -Force
        $downloaded = Get-ChildItem -LiteralPath $temporary -Recurse -Filter "uv.exe" -File | Select-Object -First 1
        if (-not $downloaded) { throw "uv archive did not contain uv.exe" }
        Copy-Item -LiteralPath $downloaded.FullName -Destination $uvExe -Force
        $uvx = Join-Path $downloaded.Directory.FullName "uvx.exe"
        if (Test-Path -LiteralPath $uvx) { Copy-Item -LiteralPath $uvx -Destination (Join-Path $uvRoot "uvx.exe") -Force }
        return $uvExe
    }
    finally {
        Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Install-RepositorySeed {
    param([string]$Id, [string]$ArchiveUrl, [string]$SeedReposRoot)
    $destination = Join-Path $SeedReposRoot $Id
    if (Test-Path -LiteralPath (Join-Path $destination ".anima-archive-revision")) { return }
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("cyantools-seed-" + [Guid]::NewGuid().ToString("N"))
    try {
        New-Item -ItemType Directory -Force -Path $temporary | Out-Null
        $archive = Join-Path $temporary "repo.zip"
        $extract = Join-Path $temporary "extract"
        Invoke-WebRequest -UseBasicParsing -Uri $ArchiveUrl -OutFile $archive
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $extractRoot = [IO.Path]::GetFullPath($extract + [IO.Path]::DirectorySeparatorChar)
            foreach ($entry in $zip.Entries) {
                $target = [IO.Path]::GetFullPath((Join-Path $extract $entry.FullName))
                if (-not $target.StartsWith($extractRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Unsafe GitHub archive path: $($entry.FullName)"
                }
            }
        }
        finally { $zip.Dispose() }
        Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
        $roots = @(Get-ChildItem -LiteralPath $extract -Directory)
        if ($roots.Count -ne 1) { throw "Unexpected GitHub archive layout: $ArchiveUrl" }
        Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $roots[0].FullName -Destination $destination
        Set-Content -LiteralPath (Join-Path $destination ".anima-archive-revision") -Value "archive:main" -Encoding UTF8
    }
    finally {
        Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Build-TrainingBackendBootstrap {
    param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot), [switch]$Force)

    $uv = Get-BootstrapUv $ProjectRoot
    $seedRoot = Join-Path $ProjectRoot "dist\managed-root-seed"
    $seedRepos = Join-Path $seedRoot "repos"
    $wheelhouse = Join-Path $ProjectRoot "dist\offline-wheelhouse"
    $uvCache = Join-Path $ProjectRoot "dist\uv-cache"
    $manifestPath = Join-Path $wheelhouse "offline-wheelhouse-manifest.json"
    New-Item -ItemType Directory -Force -Path $seedRepos | Out-Null

    Install-RepositorySeed "sd-scripts" "https://github.com/kohya-ss/sd-scripts/archive/main.zip" $seedRepos
    Install-RepositorySeed "anima-fast-lora" "https://github.com/GALIAIS/LoraHub/archive/main.zip" $seedRepos
    if (-not $Force -and (Test-Path -LiteralPath $manifestPath) -and (Test-Path -LiteralPath $uvCache)) {
        Write-Host "Reusing training backend bootstrap resources."
        return $seedRoot
    }

    Remove-Item -LiteralPath $wheelhouse -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $uvCache -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $wheelhouse, $uvCache | Out-Null
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("cyantools-wheelhouse-" + [Guid]::NewGuid().ToString("N"))
    try {
        $py310Venv = Join-Path $temporary "py310"
        $py313Venv = Join-Path $temporary "py313"
        Invoke-Checked $uv @("venv", "--python", "3.10", "--seed", $py310Venv)
        Invoke-Checked $uv @("venv", "--python", "3.13", "--seed", $py313Venv)
        $py310 = Join-Path $py310Venv "Scripts\python.exe"
        $py313 = Join-Path $py313Venv "Scripts\python.exe"
        $sdRoot = Join-Path $seedRepos "sd-scripts"
        $animaRoot = Join-Path $seedRepos "anima-fast-lora\external\anima_lora"

        Invoke-Checked $py310 @("-m", "pip", "download", "--dest", $wheelhouse, "--extra-index-url", "https://download.pytorch.org/whl/cu128", "-r", (Join-Path $sdRoot "requirements.txt"))
        Invoke-Checked $py310 @(
            "-m", "pip", "download", "--dest", $wheelhouse, "--index-url", "https://download.pytorch.org/whl/cu128", "--no-deps",
            "torch==2.10.0+cu128", "torchvision==0.25.0+cu128", "torchaudio==2.10.0+cu128", "xformers==0.0.35"
        )
        Invoke-Checked $py310 @("-m", "pip", "download", "--dest", $wheelhouse, "triton-windows==3.6.0.post26", "lycoris-lora", "pip", "setuptools", "wheel")

        $pyproject = Join-Path $animaRoot "pyproject.toml"
        if (Test-Path -LiteralPath $pyproject) {
            $text = [IO.File]::ReadAllText($pyproject)
            if ($text -notmatch '(?m)^\[tool\.uv\]') { $text += "`r`n[tool.uv]`r`nenvironments = [`"sys_platform == 'win32'`"]`r`n" }
            elseif ($text -notmatch '(?m)^environments\s*=') { $text = $text -replace '(?m)^\[tool\.uv\]\s*$', "[tool.uv]`r`nenvironments = [`"sys_platform == 'win32'`"]" }
            [IO.File]::WriteAllText($pyproject, $text, [Text.UTF8Encoding]::new($false))
        }
        Invoke-Checked $uv @("--cache-dir", $uvCache, "--directory", $animaRoot, "sync", "--python", "3.13", "--no-dev", "--no-default-groups")
        Invoke-Checked $py313 @("-m", "pip", "download", "--dest", $wheelhouse, "--extra-index-url", "https://download.pytorch.org/whl/cu128", $animaRoot)

        foreach ($wheel in Get-ChildItem -LiteralPath $wheelhouse -Filter "torch-*.whl" -File) {
            if ($wheel.Name -notmatch '\+cu128') { Remove-Item -LiteralPath $wheel.FullName -Force }
        }
        [ordered]@{
            generated_at = (Get-Date).ToUniversalTime().ToString("o")
            backends = @("sd-scripts", "anima-fast-lora")
            python = @{ sd_scripts = "3.10"; anima_fast_lora = "3.13" }
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    }
    finally {
        Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
    }
    return $seedRoot
}

if ($MyInvocation.InvocationName -ne ".") {
    Build-TrainingBackendBootstrap -ProjectRoot $Root -Force:$Force | Out-Host
}
