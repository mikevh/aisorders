#Requires -Version 7
<#
.SYNOPSIS
    Puts a malformed message straight onto the orders queue. Demo scenario 14.4.

.DESCRIPTION
    Bypasses API Management entirely, which is the whole point: this is the
    class of bad input the gateway never sees. It fails at deserialization
    inside ProcessOrder rather than in business logic, retries five times, and
    dead-letters.

    Contrast with the gateway rejections in demo.http, which are refused before
    compute is touched. Same outcome for the caller, completely different place
    in the system, and only one of them costs you five function invocations.

    Requires Service Bus data-plane rights. Subscription Owner is NOT enough:
    Azure RBAC separates Actions from DataActions and Owner covers only the
    former. Terraform assigns the operator Data Sender and Data Receiver for
    exactly this reason.

    Recover with:  POST {gateway}/admin/replay
    Unreadable messages are discarded rather than resubmitted, since replaying
    something that cannot be deserialized just returns it to the dead-letter
    queue.

.PARAMETER Body
    Message body to send. Defaults to JSON that is well-formed but carries no
    usable order.

.EXAMPLE
    ./scripts/inject-malformed.ps1
    ./scripts/inject-malformed.ps1 -Body 'this is not json at all'
#>
[CmdletBinding()]
param(
    [string]$Body = '{"thisIsNot":"an order","orderId":null}'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$infraDir = Join-Path $repoRoot 'infra'

Push-Location $infraDir
try {
    $fqdn = (terraform output -raw servicebus_fqdn 2>$null)
}
finally {
    Pop-Location
}

if ([string]::IsNullOrWhiteSpace($fqdn)) {
    throw "Could not read servicebus_fqdn. Run 'terraform apply' in infra/ first."
}

$token = az account get-access-token --resource https://servicebus.azure.net --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Could not acquire a Service Bus token. Run 'az login'."
}

$uri = "https://$fqdn/orders/messages"
Write-Host "Injecting a malformed message directly onto the queue at $fqdn" -ForegroundColor Cyan
Write-Host "  body: $Body"

try {
    Invoke-WebRequest -Uri $uri -Method Post `
        -Headers @{ Authorization = "Bearer $token" } `
        -ContentType 'application/json' -Body $Body -UseBasicParsing | Out-Null
}
catch {
    $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    if ($code -eq 401) {
        throw "401 from Service Bus. The signed-in identity lacks the Data Sender role - subscription Owner does not grant data-plane access. Re-run 'terraform apply' to assign it."
    }
    throw
}

Write-Host ''
Write-Host 'Sent. ProcessOrder will now fail to deserialize it, retry five times,' -ForegroundColor Green
Write-Host 'and dead-letter it. No order row exists for it, because it never came'
Write-Host 'through SubmitOrder - which is itself worth pointing out on stage.'
