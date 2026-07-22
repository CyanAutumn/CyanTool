[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]]$AllowedRoot,

    [switch]$UserConfirmed,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertTo-TomlBasicString {
    param([Parameter(Mandatory = $true)][string]$Value)

    $escaped = $Value.Replace("\\", "\\\\").Replace('"', '\"').Replace("`r", "\\r").Replace("`n", "\\n")
    return '"{0}"' -f $escaped
}

function Resolve-AllowedDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not $item.PSIsContainer) {
        throw "Allowed root must be a directory: $Path"
    }

    $fullPath = [System.IO.Path]::GetFullPath($item.FullName).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $volumeRoot = [System.IO.Path]::GetPathRoot($item.FullName).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ([string]::Equals($fullPath, $volumeRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "A filesystem root is not allowed as an MCP root: $Path. Specify a concrete working directory."
    }

    return $fullPath
}

function Get-ReleaseRoot {
    $releaseRoot = Split-Path -Parent $PSScriptRoot
    $serverPath = Join-Path $releaseRoot "CyanTools.McpServer.exe"
    if (-not (Test-Path -LiteralPath $serverPath -PathType Leaf)) {
        throw "CyanTools.McpServer.exe was not found in the release directory: $serverPath"
    }

    return $releaseRoot
}

function Get-CodexConfigPath {
    $codexHome = if (-not [string]::IsNullOrWhiteSpace($env:CODEX_HOME)) {
        $env:CODEX_HOME
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        Join-Path $env:USERPROFILE ".codex"
    }
    else {
        throw "Cannot determine the Codex configuration directory. Set CODEX_HOME or USERPROFILE and retry."
    }

    return (Join-Path $codexHome "config.toml")
}

$releaseRoot = Get-ReleaseRoot
$serverPath = Join-Path $releaseRoot "CyanTools.McpServer.exe"
$resolvedRoots = @($AllowedRoot | ForEach-Object { Resolve-AllowedDirectory -Path $_ } | Select-Object -Unique)
if ($resolvedRoots.Count -eq 0) {
    throw "At least one allowed root is required."
}

$configPath = Get-CodexConfigPath
$configDirectory = Split-Path -Parent $configPath
$currentConfig = if (Test-Path -LiteralPath $configPath -PathType Leaf) {
    [System.IO.File]::ReadAllText($configPath)
}
else {
    ""
}

$serverSectionPattern = '(?ms)^\[mcp_servers\.cyantools(?:\.[^\]]+)?\]\r?\n.*?(?=^\[|\z)'
$hasExistingServer = [regex]::IsMatch($currentConfig, '^\[mcp_servers\.cyantools(?:\]|\.)', [System.Text.RegularExpressions.RegexOptions]::Multiline)
if ($hasExistingServer -and -not $Force) {
    throw "The Codex configuration already contains a cyantools MCP entry: $configPath. Use -Force only after the user explicitly confirms replacement."
}

if (-not $UserConfirmed) {
    $rootsForDisplay = $resolvedRoots -join "; "
    throw "This will modify $configPath and grant CyanTools MCP access to: $rootsForDisplay. Obtain explicit user confirmation, then rerun with -UserConfirmed."
}

$allowedRootsValue = $resolvedRoots -join ";"
$serverSection = @"
# CyanTools local MCP server. Managed by scripts\Install-CyanToolsMcp.ps1.
[mcp_servers.cyantools]
command = $(ConvertTo-TomlBasicString -Value $serverPath)
cwd = $(ConvertTo-TomlBasicString -Value $releaseRoot)
startup_timeout_sec = 30
tool_timeout_sec = 3600
default_tools_approval_mode = "prompt"

[mcp_servers.cyantools.env]
CYANTOOLS_MCP_ALLOWED_ROOTS = $(ConvertTo-TomlBasicString -Value $allowedRootsValue)
"@

$updatedConfig = [regex]::Replace($currentConfig, $serverSectionPattern, '').TrimEnd()
if (-not [string]::IsNullOrWhiteSpace($updatedConfig)) {
    $updatedConfig += [Environment]::NewLine + [Environment]::NewLine
}
$updatedConfig += $serverSection.TrimEnd() + [Environment]::NewLine

New-Item -ItemType Directory -Force -Path $configDirectory | Out-Null
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($configPath, $updatedConfig, $utf8NoBom)

Write-Host "CyanTools MCP was installed: $configPath"
Write-Host "Allowed roots: $($resolvedRoots -join '; ')"
Write-Host "Restart or refresh Codex. It will start $serverPath only when a tool is needed."
