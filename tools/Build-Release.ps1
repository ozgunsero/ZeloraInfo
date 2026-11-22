<#
.SYNOPSIS
  Builds, tests and packages Zelora Info into .\release\ (self-contained + framework-dependent zips and SHA-256 sums).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\tools\Build-Release.ps1

  Copyright 2026 Şero Teknoloji — Licensed under the Apache License, Version 2.0
#>
param([string]$Runtime = 'win-x64', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
$out = Join-Path $root 'release'
$app = Join-Path $root 'src\ZeloraInfo.App\ZeloraInfo.App.csproj'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'ZeloraInfo.slnx') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$variants = @(
    @{ Name = "ZeloraInfo-$version-$Runtime";            SelfContained = 'true';  Extra = @('-p:EnableCompressionInSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true') },
    @{ Name = "ZeloraInfo-$version-$Runtime-framework"; SelfContained = 'false'; Extra = @() }
)

foreach ($v in $variants) {
    $dir = Join-Path $out $v.Name
    dotnet publish $app -c Release -r $Runtime --self-contained $($v.SelfContained) -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false @($v.Extra) -o $dir --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($v.Name)" }
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dir 'LICENSE.txt')
    Copy-Item (Join-Path $root 'NOTICE') (Join-Path $dir 'NOTICE.txt')
    Copy-Item (Join-Path $root 'third-party-licenses.json') $dir
    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath (Join-Path $out "$($v.Name).zip") -CompressionLevel Optimal -Force
}

$sums = Get-ChildItem $out -Filter *.zip | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
$sums | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
Get-ChildItem $out -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
