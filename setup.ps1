#Requires -RunAsAdministrator
# Trusts the integrations.at code-signing certificate that signs PresenterTools.exe and the MSI,
# so Windows no longer shows "Unknown publisher".

$ErrorActionPreference = 'Stop'
$cer = Join-Path $PSScriptRoot 'certs\integrations-at-codesigning.cer'
$thumbprint = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $cer).Thumbprint

foreach ($store in 'Root', 'TrustedPublisher') {
    $location = "Cert:\LocalMachine\$store"
    if (Test-Path (Join-Path $location $thumbprint)) {
        Write-Host "Already trusted in LocalMachine\$store"
    }
    else {
        Import-Certificate -FilePath $cer -CertStoreLocation $location | Out-Null
        Write-Host "Imported into LocalMachine\$store"
    }
}
