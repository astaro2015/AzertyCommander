param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir,

    [string]$Configuration = "",
    [string]$RuntimeIdentifier = "",
    [string]$SelfContained = ""
)

$ErrorActionPreference = "Stop"

function Get-StateInt {
    param(
        [object]$State,
        [string]$Name,
        [int]$Default
    )

    if ($null -ne $State -and $null -ne $State.PSObject.Properties[$Name]) {
        return [int]$State.$Name
    }

    return $Default
}

function Escape-CSharpString {
    param([string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return $Value.Replace('\', '\\').Replace('"', '\"')
}

function Escape-MarkdownCell {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "-"
    }

    return $Value.Replace('|', '\|')
}

$statePath = Join-Path $ProjectDir "version.json"
$historyPath = Join-Path $ProjectDir "VERSION_HISTORY.md"
$generatedDir = Join-Path $ProjectDir "Generated"
$propertiesDir = Join-Path $ProjectDir "Properties"
$buildInfoPath = Join-Path $generatedDir "BuildInfo.cs"
$assemblyInfoPath = Join-Path $propertiesDir "AssemblyInfo.cs"

if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    $state = $null
}

$major = Get-StateInt $state "major" 0
$minor = Get-StateInt $state "minor" 1
$patch = Get-StateInt $state "patch" 0
$build = (Get-StateInt $state "build" 0) + 1

$version = "$major.$minor.$patch.$build"
$informationalVersion = "$major.$minor.$patch+build.$build"
$localTime = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss K")
$utcTime = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'")

New-Item -ItemType Directory -Force -Path $generatedDir, $propertiesDir | Out-Null

$newState = [ordered]@{
    major = $major
    minor = $minor
    patch = $patch
    build = $build
    lastVersion = $version
    lastBuildLocal = $localTime
    lastBuildUtc = $utcTime
    lastConfiguration = $Configuration
    lastRuntime = $RuntimeIdentifier
    lastSelfContained = $SelfContained
}

$newState | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding UTF8

$buildInfo = @"
namespace AzertyCommander;

internal static class BuildInfo
{
    public const int Major = $major;
    public const int Minor = $minor;
    public const int Patch = $patch;
    public const int Build = $build;
    public const string Version = "$version";
    public const string InformationalVersion = "$informationalVersion";
    public const string BuildTimeLocal = "$(Escape-CSharpString $localTime)";
    public const string BuildTimeUtc = "$(Escape-CSharpString $utcTime)";
    public const string Configuration = "$(Escape-CSharpString $Configuration)";
    public const string RuntimeIdentifier = "$(Escape-CSharpString $RuntimeIdentifier)";
    public const string SelfContained = "$(Escape-CSharpString $SelfContained)";
}
"@

$assemblyInfo = @"
using System.Reflection;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]
[assembly: AssemblyTitle("AZERTY Commander")]
[assembly: AssemblyDescription("Two-panel Windows file manager")]
[assembly: AssemblyCompany("AZERTY")]
[assembly: AssemblyProduct("AZERTY Commander")]
[assembly: AssemblyCopyright("Copyright 2026")]
[assembly: AssemblyVersion("$version")]
[assembly: AssemblyFileVersion("$version")]
[assembly: AssemblyInformationalVersion("$informationalVersion")]
"@

Set-Content -LiteralPath $buildInfoPath -Value $buildInfo -Encoding UTF8
Set-Content -LiteralPath $assemblyInfoPath -Value $assemblyInfo -Encoding UTF8

if (!(Test-Path -LiteralPath $historyPath)) {
    @(
        "# Version History",
        "",
        "| Version | Local time | UTC time | Configuration | Runtime | Self-contained |",
        "| --- | --- | --- | --- | --- | --- |"
    ) | Set-Content -LiteralPath $historyPath -Encoding UTF8
}

$historyLine = "| $version | $(Escape-MarkdownCell $localTime) | $(Escape-MarkdownCell $utcTime) | $(Escape-MarkdownCell $Configuration) | $(Escape-MarkdownCell $RuntimeIdentifier) | $(Escape-MarkdownCell $SelfContained) |"
Add-Content -LiteralPath $historyPath -Value $historyLine -Encoding UTF8

Write-Host "AZERTY Commander version: $version"
