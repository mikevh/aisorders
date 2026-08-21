#Requires -Version 7
<#
.SYNOPSIS
    Publishes the Functions app and removes the platform-injected storage key.

.DESCRIPTION
    Terraform provisions infrastructure only. This publishes application code
    on top of it, so code can be redeployed without touching infrastructure.

    It also deletes the AzureWebJobsStorage app setting. Azure injects that
    setting with a full connection string including an account key when the
    Function App is created, and the host resolves it ahead of the
    identity-based AzureWebJobsStorage__accountName — meaning the managed
    identity is bypassed entirely while it is present. Removing it is what
    makes the no-secrets position in SPEC.md 9.2 actually true.

    That deletion has to happen here rather than in Terraform: once the key is
    gone the host genuinely needs the role assignments from W09 to reach
    storage, and those do not exist until apply completes.

.PARAMETER SkipSettingCleanup
    Leave AzureWebJobsStorage in place. For diagnosing whether a storage
    failure is caused by identity configuration.

.EXAMPLE
    ./scripts/deploy-functions.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipSettingCleanup
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$infraDir = Join-Path $repoRoot 'infra'
$srcDir = Join-Path $repoRoot 'src/AisDemo.Functions'

function Get-TerraformOutput {
    param([Parameter(Mandatory)][string]$Name)

    Push-Location $infraDir
    try {
        $value = terraform output -raw $Name 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
            throw "Terraform output '$Name' is unavailable. Run 'terraform apply' in infra/ first."
        }
        return $value.Trim()
    }
    finally {
        Pop-Location
    }
}

foreach ($tool in 'terraform', 'func', 'az') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "'$tool' was not found on PATH. See the prerequisites in README.md."
    }
}

Write-Host 'Reading Terraform outputs...' -ForegroundColor Cyan
$functionApp = Get-TerraformOutput -Name 'function_app_name'
$resourceGroup = Get-TerraformOutput -Name 'resource_group_name'
Write-Host "  function app   : $functionApp"
Write-Host "  resource group : $resourceGroup"

Write-Host "`nPublishing $functionApp..." -ForegroundColor Cyan
Push-Location $srcDir
try {
    func azure functionapp publish $functionApp --dotnet-version 10.0
    if ($LASTEXITCODE -ne 0) {
        throw "func publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

if ($SkipSettingCleanup) {
    Write-Warning 'Skipping AzureWebJobsStorage cleanup. The storage account key remains in app settings.'
    return
}

# Publishing can reintroduce the setting, so this runs afterwards rather than
# before.
Write-Host "`nRemoving the platform-injected AzureWebJobsStorage key..." -ForegroundColor Cyan
$existing = az functionapp config appsettings list `
    --name $functionApp --resource-group $resourceGroup `
    --query "[?name=='AzureWebJobsStorage'] | length(@)" -o tsv

if ($existing -eq '0') {
    Write-Host '  not present; nothing to remove.'
}
else {
    az functionapp config appsettings delete `
        --name $functionApp --resource-group $resourceGroup `
        --setting-names AzureWebJobsStorage --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to delete the AzureWebJobsStorage setting."
    }
    Write-Host '  removed.'
}

$remaining = az functionapp config appsettings list `
    --name $functionApp --resource-group $resourceGroup `
    --query "[?name=='AzureWebJobsStorage'] | length(@)" -o tsv

if ($remaining -ne '0') {
    throw "AzureWebJobsStorage is still present. The app is authenticating to storage with a key rather than its managed identity."
}

Write-Host "`nDeployed. Storage access is identity-based; no key remains in app settings." -ForegroundColor Green
Write-Host "Function app: https://$(Get-TerraformOutput -Name 'function_app_hostname')"
