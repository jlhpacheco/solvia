[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\src\Solvia\Solvia.csproj'),
    [ValidateSet('Qwen/Qwen3.5-9B')]
    [string]$Model = 'Qwen/Qwen3.5-9B',
    [ValidateRange(1, 90)]
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'
$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
$secureKey = Read-Host 'Together API key (hidden; never paste it in chat)' -AsSecureString
$settings = $null
try {
    if ($secureKey.Length -eq 0) { throw 'A Together API key is required. Nothing was stored.' }
    $settings = @{
        'AI:Provider' = 'together'
        'Together:ApiKey' = [System.Net.NetworkCredential]::new('', $secureKey).Password.Trim()
        'Together:Model' = $Model
        'Together:TimeoutSeconds' = $TimeoutSeconds
    }
    if ([string]::IsNullOrWhiteSpace($settings['Together:ApiKey'])) {
        throw 'A Together API key is required. Nothing was stored.'
    }
    # Send JSON through stdin: the key stays out of command-line arguments,
    # shell history, repository files and Git. The development user-secrets
    # store is per-user local configuration, not an encrypted vault.
    $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $resolvedProject
    if ($LASTEXITCODE -ne 0) { throw 'The user-secrets command failed.' }
    Write-Host 'Together settings saved locally. Restart Solvia in Development mode.'
    Write-Host 'Together requests are metered. Use fictional adult demo details only.'
}
finally {
    if ($settings) { $settings.Clear() }
    $secureKey.Dispose()
}
