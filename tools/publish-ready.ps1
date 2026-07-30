param(
    [string]$ProjectDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

Push-Location $ProjectDir
try {
    dotnet build -c Release
    dotnet run -c Release --no-build -- --self-test

    $publishArgs = @(
        "-c", "Release",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:SkipVersionBump=true"
    )

    dotnet publish @publishArgs -r win-x64 -o ".\ready"
    dotnet publish @publishArgs -r win-x86 -o ".\ready-win-x86"

    .\ready\AzertyCommander.exe --self-test
    .\ready-win-x86\AzertyCommander.exe --self-test

    $x64 = Get-Item ".\ready\AzertyCommander.exe"
    $x86 = Get-Item ".\ready-win-x86\AzertyCommander.exe"
    if ($x64.VersionInfo.FileVersion -ne $x86.VersionInfo.FileVersion) {
        throw "x64 version $($x64.VersionInfo.FileVersion) differs from x86 version $($x86.VersionInfo.FileVersion)."
    }

    [pscustomobject]@{
        Version = $x64.VersionInfo.FileVersion
        X64 = $x64.FullName
        X86 = $x86.FullName
    }
}
finally {
    Pop-Location
}
