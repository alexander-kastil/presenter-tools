#Requires -Version 7
# Signs the MSI that the Release workflow built for a tag and publishes it to the GitHub Release.
# Only the MSI is signed: PresenterTools.exe inside a CI-built MSI stays unsigned, because the
# signing certificate exists only in this machine's certificate store. A fully signed build
# (PresenterTools.exe and MSI) comes from a local dotnet build of src\PresenterTools.Setup\PresenterTools.Setup.wixproj.
# Usage: .\sign-release.ps1 -Tag v1.0.0 [-RunId 123456789]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$RunId
)

$ErrorActionPreference = 'Stop'
$thumbprint = 'B29050FA48FD6A2337F3944A10E6F461486C533E'
$timestampServer = 'http://timestamp.digicert.com'
$version = $Tag -replace '^v', ''

$cert = Get-Item "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue
if (-not $cert) { throw "Code-signing certificate $thumbprint not found in Cert:\CurrentUser\My" }

if (-not $RunId) {
    $RunId = gh run list --workflow release.yml --branch $Tag --status success --limit 1 --json databaseId --jq '.[0].databaseId'
    if (-not $RunId) { throw "No successful release.yml run found for $Tag" }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) "presenter-tools-sign-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    gh run download $RunId --name "PresenterTools-$version-unsigned" --dir $temp
    if ($LASTEXITCODE -ne 0) { throw "Download of artifact PresenterTools-$version-unsigned from run $RunId failed" }

    $msi = Join-Path $temp 'PresenterTools.msi'
    if (-not (Test-Path $msi)) { throw "PresenterTools.msi not found in the downloaded artifact" }

    $result = Set-AuthenticodeSignature -FilePath $msi -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $timestampServer
    if (-not $result.SignerCertificate -or -not $result.TimeStamperCertificate) { throw "Signing failed: $($result.StatusMessage)" }

    $status = (Get-AuthenticodeSignature $msi).Status
    if ($status -ne 'Valid') { throw "PresenterTools.msi signature status is $status" }
    Write-Host "Signed PresenterTools.msi: $status"

    gh release view $Tag *> $null
    if ($LASTEXITCODE -ne 0) {
        gh release create $Tag --verify-tag --title "Presenter Tools $version" --generate-notes
        if ($LASTEXITCODE -ne 0) { throw "Creating release $Tag failed" }
    }

    gh release upload $Tag $msi --clobber
    if ($LASTEXITCODE -ne 0) { throw "Uploading PresenterTools.msi to $Tag failed" }
    Write-Host "Published PresenterTools.msi to release $Tag"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
