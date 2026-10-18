param(
    [Parameter(Mandatory)][string[]]$Path,
    [Parameter(Mandatory)][string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignTool
)

# Authenticode-signs files with a certificate from the current user's store (signtool.exe from the Windows SDK).
# The timestamp keeps the signature valid after the certificate expires; pass -TimestampUrl '' to sign without one (offline, tests).
$ErrorActionPreference = 'Stop'
if (-not $SignTool) {
    $kits = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin'
    $SignTool = Get-ChildItem -Path $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $SignTool -or -not (Test-Path -LiteralPath $SignTool)) { throw 'signtool.exe was not found (install the Windows SDK) or pass -SignTool.' }
$thumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
if (-not (Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $thumbprint -and $_.HasPrivateKey })) {
    throw "No certificate with a private key and thumbprint $thumbprint in Cert:\CurrentUser\My."
}

$arguments = @('sign', '/fd', 'SHA256', '/sha1', $thumbprint)
if ($TimestampUrl) { $arguments += @('/tr', $TimestampUrl, '/td', 'SHA256') }
& $SignTool @arguments @Path
if ($LASTEXITCODE -ne 0) { throw 'signtool failed.' }

foreach ($file in $Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $thumbprint) { throw "Signing did not stick: $file" }
    # A certificate that is not trusted on this machine (a self-signed test one) signs fine but reads as untrusted here.
    if ($signature.Status -ne 'Valid') { Write-Warning "$([IO.Path]::GetFileName($file)): signed, status $($signature.Status)" }
}
