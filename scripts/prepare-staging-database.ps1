param(
    [Parameter(Mandatory = $true)]
    [string]$Context
)

$ErrorActionPreference = 'Stop'
$null = Get-Command kubectl -ErrorAction Stop
$kubectlArguments = @('--context', $Context, '--namespace', 'staging')

# Reusing the Secret is essential: changing POSTGRES_PASSWORD does not change
# the password of an already initialized PostgreSQL data volume.
$existing = & kubectl @kubectlArguments get secret carpoolr-database --ignore-not-found -o name
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the staging database Secret. Check context and permissions.'
}

if ($existing) {
    Write-Output 'Existing staging database Secret retained.'
    exit 0
}

$random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $bytes = New-Object byte[] 32
    $random.GetBytes($bytes)
    $password = [Convert]::ToBase64String($bytes)
}
finally {
    $random.Dispose()
}

$secret = @{
    apiVersion = 'v1'
    kind = 'Secret'
    metadata = @{ name = 'carpoolr-database'; namespace = 'staging' }
    type = 'Opaque'
    stringData = @{
        POSTGRES_PASSWORD = $password
        ConnectionStrings__Default = "Host=carpoolr-db;Port=5432;Database=carpoolr;Username=carpoolr;Password=$password;GSS Encryption Mode=Disable"
    }
}

# Credentials travel through stdin, never command arguments or a temporary file.
$secret | ConvertTo-Json -Depth 5 -Compress | & kubectl @kubectlArguments apply -f -
if ($LASTEXITCODE -ne 0) {
    throw 'Could not prepare the staging database Secret.'
}

$password = $null
$secret = $null
Write-Output 'Staging database Secret prepared. No credentials were written to the repository.'
