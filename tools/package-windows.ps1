[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts/windows/win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $root $OutputDirectory
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$app = Join-Path $output 'Baccarat Workspace'
$archive = Join-Path $output "Baccarat-Workspace-$Runtime-unsigned.zip"
$checksum = "$archive.sha256"

if (Test-Path -LiteralPath $app -PathType Any) {
    throw "Output already exists: $app"
}
if (Test-Path -LiteralPath $archive -PathType Any) {
    throw "Output already exists: $archive"
}

New-Item -ItemType Directory -Path $app -Force | Out-Null
& dotnet publish (Join-Path $root 'src/Baccarat.Desktop/Baccarat.Desktop.csproj') `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $app `
    -p:ContinuousIntegrationBuild=true `
    -p:DebugSymbols=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $app 'Baccarat.Desktop.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The Windows application executable was not published.'
}
if ((Get-Item -LiteralPath $executable).Length -eq 0) {
    throw 'The Windows application executable is empty.'
}

Compress-Archive -LiteralPath $app -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksum -Value "$hash  $([IO.Path]::GetFileName($archive))" -Encoding ascii -NoNewline

Write-Output "Validated Windows publish: $app"
Write-Output "Unsigned development archive: $archive"
Write-Output "SHA-256: $hash"
