[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ApplicationDirectory,
    [Parameter(Mandatory)]
    [string]$OutputArchive,
    [uri]$TimestampUrl = 'https://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$thumbprint = $env:WINDOWS_SIGNING_CERTIFICATE_THUMBPRINT
if ([string]::IsNullOrWhiteSpace($thumbprint)) {
    throw 'WINDOWS_SIGNING_CERTIFICATE_THUMBPRINT must identify an installed code-signing certificate.'
}

$app = [IO.Path]::GetFullPath($ApplicationDirectory)
$archive = [IO.Path]::GetFullPath($OutputArchive)
if (-not (Test-Path -LiteralPath $app -PathType Container)) {
    throw "Application directory does not exist: $app"
}
if (Test-Path -LiteralPath $archive -PathType Any) {
    throw "Output archive already exists: $archive"
}

$signTool = Get-Command signtool.exe -ErrorAction Stop
$targets = @(
    (Join-Path $app 'Baccarat.Desktop.exe'),
    (Join-Path $app 'Baccarat.Core.dll'),
    (Join-Path $app 'Baccarat.Infrastructure.dll'),
    (Join-Path $app 'Baccarat.Desktop.dll')
)
foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Expected application binary is missing: $target"
    }
    & $signTool.Source sign /sha1 $thumbprint /fd SHA256 /tr $TimestampUrl.AbsoluteUri /td SHA256 $target
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode signing failed for $target."
    }
    & $signTool.Source verify /pa /all $target
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode verification failed for $target."
    }
}

Compress-Archive -LiteralPath $app -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" `
    -Value "$hash  $([IO.Path]::GetFileName($archive))" -Encoding ascii -NoNewline

Write-Output "Signed and verified Windows archive: $archive"
Write-Output "SHA-256: $hash"
