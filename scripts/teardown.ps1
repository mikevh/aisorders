#Requires -Version 7
<#
.SYNOPSIS
    Destroys the demo environment.

.DESCRIPTION
    Wraps 'terraform destroy' and verifies the resource group is actually gone
    afterwards, rather than trusting the exit code.

    Worth running between demos. Service Bus Standard carries a fixed monthly
    base charge and is the only meaningful cost in this environment; destroyed,
    the whole thing costs nothing.

.PARAMETER Force
    Skip the confirmation prompt.

.EXAMPLE
    ./scripts/teardown.ps1
    ./scripts/teardown.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$infraDir = Join-Path $repoRoot 'infra'

Push-Location $infraDir
try {
    $resourceGroup = (terraform output -raw resource_group_name 2>$null)

    if ([string]::IsNullOrWhiteSpace($resourceGroup)) {
        Write-Host 'No Terraform outputs found. Nothing appears to be deployed.' -ForegroundColor Yellow
        return
    }

    if (-not $Force) {
        Write-Host "About to destroy resource group '$resourceGroup' and everything in it." -ForegroundColor Yellow
        $answer = Read-Host "Type the resource group name to confirm"
        if ($answer -ne $resourceGroup) {
            Write-Host 'Names did not match. Nothing destroyed.' -ForegroundColor Yellow
            return
        }
    }

    Write-Host "`nDestroying..." -ForegroundColor Cyan
    terraform destroy -auto-approve
    if ($LASTEXITCODE -ne 0) {
        throw "terraform destroy failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

# Verify rather than trust. A destroy can report success while leaving
# resources behind if a delete was still in flight.
$exists = az group exists --name $resourceGroup 2>$null

if ($exists -eq 'true') {
    Write-Warning "Resource group '$resourceGroup' still exists. Check the portal before assuming this is free."
}
else {
    Write-Host "`nDestroyed. Resource group '$resourceGroup' is gone; the environment now costs nothing." -ForegroundColor Green
}

Write-Host ''
Write-Host 'Local containers are separate and keep running. Stop them with:' -ForegroundColor DarkGray
Write-Host '  cd local && docker compose down' -ForegroundColor DarkGray
