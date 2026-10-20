[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\src\Solvia\Solvia.csproj'),
    [ValidateSet('muse-spark-1.3', 'muse-spark-1.2', 'muse-spark-1.1')]
    [string]$Model = 'muse-spark-1.3',
    [ValidateRange(1, 90)]
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'
$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
$secureKey = Read-Host 'Meta Model API key (hidden; never paste it in chat)' -AsSecureString
$settings = $null
try {
    if ($secureKey.Length -eq 0) { throw 'A Meta API key is required. Nothing was stored.' }
    $settings = @{
        'AI:Provider' = 'meta'
        'Meta:ApiKey' = [System.Net.NetworkCredential]::new('', $secureKey).Password.Trim()
        'Meta:Model' = $Model
        'Meta:TimeoutSeconds' = $TimeoutSeconds
    }
    if ([string]::IsNullOrWhiteSpace($settings['Meta:ApiKey'])) {
        throw 'A Meta API key is required. Nothing was stored.'
    }
    # Send JSON through stdin: the key stays out of command-line arguments,
    # shell history, repository files and Git. The development user-secrets
    # store is per-user local configuration, not an encrypted vault.
    $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $resolvedProject
    if ($LASTEXITCODE -ne 0) { throw 'The user-secrets command failed.' }
    Write-Host 'Meta settings saved locally. Restart Solvia in Development mode.'
    Write-Host 'Meta requests are metered. Use fictional adult demo details only.'
}
finally {
    if ($settings) { $settings.Clear() }
    $secureKey.Dispose()
}
