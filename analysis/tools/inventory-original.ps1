[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\Original Application'),
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\phase1-evidence-inventory.json')
)

$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$workspacePath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourcePrefix = $sourcePath.TrimEnd([char[]]'\/') + [System.IO.Path]::DirectorySeparatorChar

if (-not $sourcePath.StartsWith($workspacePath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Source must be inside the workspace: $workspacePath"
}

$files = @(Get-ChildItem -LiteralPath $sourcePath -Recurse -File | Sort-Object FullName)
$totalBytes = [long]0
$records = foreach ($file in $files) {
    $totalBytes += $file.Length
    if (-not $file.FullName.StartsWith($sourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Inventory file escaped the source directory: $($file.FullName)"
    }
    $relative = $file.FullName.Substring($sourcePrefix.Length)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $signatureStatus = $null
    $signer = $null
    $product = $null
    $version = $null
    $company = $null

    if ($file.Extension -in '.exe', '.dll') {
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        $signatureStatus = $signature.Status.ToString()
        if ($signature.SignerCertificate) {
            $signer = $signature.SignerCertificate.Subject
        }

        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
        $product = $versionInfo.ProductName
        $version = $versionInfo.FileVersion
        $company = $versionInfo.CompanyName
    }

    [ordered]@{
        path = $relative.Replace('\', '/')
        size = $file.Length
        sha256 = $hash
        extension = $file.Extension.ToLowerInvariant()
        signature_status = $signatureStatus
        signer = $signer
        product = $product
        version = $version
        company = $company
    }
}

$extensionCounts = [ordered]@{}
foreach ($group in ($records | Group-Object extension | Sort-Object Name)) {
    $key = if ([string]::IsNullOrEmpty($group.Name)) { '[none]' } else { $group.Name }
    $extensionCounts[$key] = $group.Count
}

$manifest = [ordered]@{
    schema_version = 1
    source = 'Original Application'
    generated_utc = (Get-Date).ToUniversalTime().ToString('o')
    collection_method = 'Recursive file enumeration, SHA-256, Authenticode inspection, and PE version-resource inspection. No supplied program was executed.'
    file_count = $records.Count
    total_bytes = $totalBytes
    extension_counts = $extensionCounts
    files = $records
}

$outputDirectory = Split-Path -Parent $Output
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Output -Encoding utf8

$verification = Get-Content -LiteralPath $Output -Raw | ConvertFrom-Json
if ($verification.file_count -ne $files.Count -or $verification.total_bytes -ne $totalBytes) {
    throw 'Inventory verification failed after writing the manifest.'
}
Write-Output "Wrote $($records.Count) records to $Output"

