#Requires -Version 7
<#
.SYNOPSIS
    Fires concurrent orders through the gateway. Demo scenario 14.6.

.DESCRIPTION
    Drives the queue depth up so buffering and scale-out are visible while the
    API stays responsive.

    Raise the processing delay first, or the processor keeps up and the queue
    never visibly fills:

        cd infra
        terraform apply -var processing_delay_ms=3000

    Then watch queue depth in demo/queries.kql query 5, or the Live Metrics
    blade for instance count.

.PARAMETER Count
    Orders to submit. Default 100.

.PARAMETER Parallel
    Concurrent requests in flight. Default 20.

.EXAMPLE
    ./scripts/load-test.ps1 -Count 200 -Parallel 40
#>
[CmdletBinding()]
param(
    [int]$Count = 100,
    [int]$Parallel = 20
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$infraDir = Join-Path $repoRoot 'infra'

Push-Location $infraDir
try {
    $gateway = (terraform output -raw gateway_url 2>$null)
    $key = (terraform output -raw subscription_key 2>$null)
}
finally {
    Pop-Location
}

if ([string]::IsNullOrWhiteSpace($gateway) -or [string]::IsNullOrWhiteSpace($key)) {
    throw "Could not read Terraform outputs. Run 'terraform apply' in infra/ first."
}

Write-Host "Submitting $Count orders, $Parallel at a time, to $gateway" -ForegroundColor Cyan

$started = Get-Date

# Deliberately spans the notification threshold in both directions, so the
# burst also exercises the subscription filter rather than only the queue.
$results = 1..$Count | ForEach-Object -ThrottleLimit $Parallel -Parallel {
    $unitPrice = if ($_ % 3 -eq 0) { 750.00 } else { 45.00 }
    $body = @{
        customerId   = "LOAD-$($_.ToString('0000'))"
        customerName = "Load Test $_"
        items        = @(@{ sku = "WIDGET-01"; quantity = 1; unitPrice = $unitPrice })
    } | ConvertTo-Json -Depth 5

    try {
        $r = Invoke-WebRequest -Uri "$using:gateway/orders" -Method Post `
            -Headers @{ 'Ocp-Apim-Subscription-Key' = $using:key } `
            -ContentType 'application/json' -Body $body -UseBasicParsing
        [pscustomobject]@{ Status = [int]$r.StatusCode }
    }
    catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        [pscustomobject]@{ Status = $code }
    }
}

$elapsed = (Get-Date) - $started

Write-Host ''
Write-Host ("Submitted {0} orders in {1:n1}s ({2:n1}/s)" -f $Count, $elapsed.TotalSeconds, ($Count / $elapsed.TotalSeconds)) -ForegroundColor Green
$results | Group-Object Status | Sort-Object Name | ForEach-Object {
    $label = switch ($_.Name) {
        '202' { '202 accepted' }
        '401' { '401 unauthorized' }
        '429' { '429 rate limited (paid tier only)' }
        '0'   { 'no response' }
        default { $_.Name }
    }
    Write-Host ("  {0,-36} {1}" -f $label, $_.Count)
}

Write-Host ''
Write-Host 'The API returned while work was still queued. That gap is the point:' -ForegroundColor Yellow
Write-Host 'watch the queue drain in demo/queries.kql query 5.'
