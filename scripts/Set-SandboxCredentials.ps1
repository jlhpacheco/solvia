[CmdletBinding()]
param([string]$ProjectPath = (Join-Path $PSScriptRoot '..\src\Solvia\Solvia.csproj'))

$ErrorActionPreference = 'Stop'
$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
$secureClientId = Read-Host 'PayPal Sandbox Client ID (hidden)' -AsSecureString
$clientId = [System.Net.NetworkCredential]::new('', $secureClientId).Password
$secureSecret = Read-Host 'PayPal Sandbox Secret (hidden)' -AsSecureString
if ([string]::IsNullOrWhiteSpace($clientId) -or $secureSecret.Length -eq 0) {
    throw 'Both sandbox values are required. Nothing was stored.'
}
try {
    $credentials = @{
        'PayPal:ClientId' = $clientId.Trim()
        'PayPal:ClientSecret' = [System.Net.NetworkCredential]::new('', $secureSecret).Password
        'PayPal:Environment' = 'sandbox'
        'PayPal:BaseUrl' = 'https://api-m.sandbox.paypal.com'
        'PayPal:Currency' = 'USD'
    }
    # JSON travels through stdin, so the secret is absent from command-line
    # arguments, shell history, project files, and Git changes.
    $credentials | ConvertTo-Json -Compress | dotnet user-secrets set --project $resolvedProject
    if ($LASTEXITCODE -ne 0) { throw 'The user-secrets command failed.' }
    Write-Host 'Sandbox credentials saved locally. Restart Solvia in Development mode.'
}
finally {
    if ($credentials) { $credentials.Clear() }
    $clientId = $null
    $secureClientId.Dispose()
    $secureSecret.Dispose()
}
