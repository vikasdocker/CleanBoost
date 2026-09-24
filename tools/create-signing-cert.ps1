param(
    [Parameter(Mandatory=$true)][string]$DevCertStore = "Cert:\CurrentUser\My",
    [Parameter(Mandatory=$true)][string]$Subject = "CN=CleanBoostDev",
    [Parameter(Mandatory=$true)][string]$OutPfx,
    [Parameter(Mandatory=$true)][string]$PfxPassword
)

# Reuse an existing dev cert if present, otherwise create one.
$existing = Get-ChildItem $DevCertStore | Where-Object { $_.Subject -eq "CN=CleanBoostDev" -and $_.HasPrivateKey } | Select-Object -First 1
if ($existing) {
    Write-Host "reusing existing cert: $($existing.Thumbprint)"
    $cert = $existing
} else {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
        -CertStoreLocation $DevCertStore -KeyExportPolicy Exportable `
        -SignatureAlgorithm SHA256 -FriendlyName 'CleanBoost Dev Signing'
    Write-Host "created cert: $($cert.Thumbprint)"
}

$pwd = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $OutPfx -Password $pwd -Force | Out-Null
Write-Host "exported $OutPfx"

# Re-issue a cert of the same subject/thumbprint into the cert's own file is
# done, now print signtool-ready facts.
Write-Host "THUMBPRINT=$($cert.Thumbprint)"
Write-Host "SUBJECT=$($cert.Subject)"