# PowerShell Script to Code-Sign VaultRecon.exe
# Uses native Windows tools (New-SelfSignedCertificate & Set-AuthenticodeSignature)

$exePath = Join-Path $PSScriptRoot "..\bin\Release\net8.0-windows\win-x64\publish\VaultRecon.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Could not find VaultRecon.exe at $exePath. Please run 'dotnet publish -c Release' first."
    Exit 1
}

# 1. Search for an existing code-signing certificate named 'VaultRecon Local Code Signing'
$certSubject = "CN=VaultRecon Local Code Signing"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1

if ($null -eq $cert) {
    Write-Host "Creating a new self-signed code-signing certificate..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certSubject -HashAlgorithm SHA256 -CertStoreLocation Cert:\CurrentUser\My
    
    # Trust the self-signed certificate locally by adding it to Root
    Write-Host "Registering certificate in Trusted Root Certification Authorities for local verification..." -ForegroundColor Cyan
    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
    $rootStore.Open("ReadWrite")
    $rootStore.Add($cert)
    $rootStore.Close()
} else {
    Write-Host "Found existing local code-signing certificate." -ForegroundColor Green
}

# 2. Sign the executable
Write-Host "Signing $exePath..." -ForegroundColor Cyan
$signature = Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert -HashAlgorithm SHA256

if ($signature.Status -eq "Valid") {
    Write-Host "Successfully code-signed VaultRecon.exe!" -ForegroundColor Green
    Write-Host "Signer Subject: $($signature.SignerCertificate.Subject)" -ForegroundColor Green
} else {
    Write-Warning "Code-signing completed, but signature status is: $($signature.Status)"
    Write-Warning "Verify you have trusted the local root certificate if you get certification chain errors."
}
