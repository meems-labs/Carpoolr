param(
    [Parameter(Mandatory = $true)]
    [string]$Context
)

$ErrorActionPreference = 'Stop'
$null = Get-Command kubectl -ErrorAction Stop
$kubectlArguments = @('--context', $Context, '--namespace', 'staging')

$existing = & kubectl @kubectlArguments get secret carpoolr-authentik --ignore-not-found -o name
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the staging authentication Secret. Check context and permissions.'
}
if ($existing) {
    throw 'carpoolr-authentik already exists. No credentials were changed; rotate it through your secret-management process.'
}

$authority = (Read-Host 'Exact issuer from the provider discovery document (https://...)').Trim()
$authorityUri = $null
if (-not [Uri]::TryCreate($authority, [UriKind]::Absolute, [ref]$authorityUri) -or
    $authorityUri.Scheme -ne 'https' -or $authorityUri.UserInfo -or
    $authorityUri.Query -or $authorityUri.Fragment) {
    throw 'An absolute HTTPS issuer without credentials, query or fragment is required.'
}

$clientId = (Read-Host 'Client ID from the confidential Authentik provider').Trim()
if ([string]::IsNullOrWhiteSpace($clientId)) {
    throw 'A client ID is required.'
}
$audience = (Read-Host 'Access-token audience from provider configuration (Enter = client ID)').Trim()
if ([string]::IsNullOrWhiteSpace($audience)) {
    $audience = $clientId
}

$secureClientSecret = Read-Host 'Client secret (hidden input)' -AsSecureString
if ($secureClientSecret.Length -eq 0) {
    throw 'A client secret is required.'
}

$secret = $null
$secretPointer = [IntPtr]::Zero
try {
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureClientSecret)
    $secret = @{
        apiVersion = 'v1'
        kind = 'Secret'
        metadata = @{ name = 'carpoolr-authentik'; namespace = 'staging' }
        type = 'Opaque'
        stringData = @{
            Authentication__Authority = $authority
            Authentication__Audience = $audience
            Authentication__Browser__ClientId = $clientId
            Authentication__Browser__ClientSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
        }
    }

    # The secret is supplied over stdin, never in command arguments or a file.
    # Create refuses to overwrite a Secret if another administrator created it meanwhile.
    $secret | ConvertTo-Json -Depth 5 -Compress | & kubectl @kubectlArguments create -f -
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the staging authentication Secret.'
    }
}
finally {
    if ($secretPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer)
    }
    $secret = $null
    $secureClientSecret.Dispose()
}

Write-Output 'Staging authentication Secret created. Credentials were not written to the repository.'
