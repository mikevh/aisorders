# Work Breakdown — Azure Integration Services Demo MVP

Companion to [SPEC.md](./SPEC.md). Section references (§) point there.

**Sequencing principle:** reach a deployed, end-to-end walking skeleton as early as
possible (W15), then widen it. The alternative — all infrastructure, then all code — hides
integration failures like identity-based connections or RBAC propagation until the very
end, when they are most expensive.

**Sizes:** `S` ≈ under an hour · `M` ≈ a few hours · `L` ≈ half a day or more.

---

## Phase 0 — De-risk

Resolve the §17 unknowns before they can invalidate written code.

### W01 · Verify tooling and subscription access · `S`
**Depends on:** —
**Do:** Confirm Azure CLI login, target subscription, and that the account holds Contributor
*and* User Access Administrator (or Owner). Confirm Terraform ≥ 1.9, .NET SDK, Functions
Core Tools v4, Node.js, Docker Desktop.
**Done when:** `az account show` returns the intended subscription and a scratch role
assignment can be created and deleted successfully.
**Note:** The role-assignment check is the one that matters — Contributor alone silently
passes every other check and then fails at W09.

**✅ Verified 2026-08-20.** Subscription `MSDN via Attunix` (`7e62b73d-a99f-4410-891c-b19daae8fc92`),
tenant `3737fc9d-8896-426a-a2be-8a0c825a8158`, signed in as `michaelv@attunix.com` — a guest
(`#EXT#`) principal holding **Owner** at subscription scope. Owner subsumes User Access
Administrator, so W09 is clear and no scratch write test was required.

All seven resource providers already registered: `ApiManagement`, `ServiceBus`, `Web`,
`Storage`, `OperationalInsights`, `Insights`, `ManagedIdentity`. West US 2 available.

| Tool | Version |
|---|---|
| Azure CLI | 2.88.0 |
| Terraform | 1.15.8 *(installed during W01)* |
| .NET SDK | 8.0.424, 9.0.317, 10.0.400 |
| Functions Core Tools | 4.12.1 *(installed during W01)* |
| Node.js | 24.12.0 |
| Docker | 29.7.2, daemon running, Linux containers |

SWA CLI is deliberately not installed — `npx @azure/static-web-apps-cli` covers W30.

**Carry-forward:** this is an MSDN subscription with a monthly credit and spending limit.
The ~$10–15/mo Service Bus base charge fits comfortably, but a deployment left running will
eventually trip the limit — reinforcing the destroy-between-demos assumption in §16.

### W02 · Version and availability spike · `S`
**Depends on:** W01
**Do:** Resolve §17 risks 1–3. Determine the newest .NET isolated version the Functions host
supports; confirm whether Flex Consumption is available in West US 2; identify the
`azurerm` provider version where Flex Consumption and APIM Consumption resources are stable.
**Done when:** A short decision note is appended to SPEC.md §17 recording the .NET target,
the Function App hosting choice, and the provider version to pin.

**✅ Verified 2026-08-20.** Full note in SPEC.md §17.1. Summary: target `dotnet-isolated` 10
(not .NET 8 — its Functions support ends 2026-11-10); Flex Consumption is available in
West US 2 so the `Y1` fallback is withdrawn; pin `azurerm ~> 5.2`. Risks 1–3 closed.

Two follow-ons landed in §4: Flex Consumption mandates both an `azurerm_service_plan`
(`FC1`, Linux) and a blob container for deployment packages. And in provider v5,
`azurerm_storage_table` / `azurerm_storage_container` take `storage_account_id`, not
`storage_account_name` — relevant to **W06**.

---

## Phase 1 — Walking skeleton

Goal: one order travels from an HTTP call to a processed row in Table Storage, on real
Azure infrastructure, with no gateway and no topic yet.

### W03 · Repo scaffold · `S`
**Depends on:** W02
**Do:** Create the directory tree from §11. Add `.gitignore` covering `terraform.tfstate*`,
`.terraform/`, `bin/`, `obj/`, `local.settings.json`. Initialize git.
**Done when:** The tree matches §11 and `git status` is clean with no state or secrets tracked.

### W04 · Terraform root module · `M`
**Depends on:** W03
**Do:** `providers.tf` (pinned per W02, local state), `variables.tf` (§15.1), `main.tf` with
resource group, `random_string` suffix, and the shared tag map.
**Done when:** `terraform apply` creates the resource group with correct tags; `terraform
destroy` removes it cleanly.

**✅ Verified 2026-08-20.** `rg-aisdemo-wus2` created in West US 2 with all four tags,
destroyed cleanly (`az group exists` → `False`), and re-applied to leave a base for W05.

All resource names live in a single `local.names` map rather than being built ad hoc in each
file, and fixed entity names in `local.entities` — the latter is the half of the §12.1
duplication that Terraform owns, carrying a pointer to `local/config.json`.

Two `.gitignore` corrections fell out: `.terraform.lock.hcl` is now **committed** so provider
versions are reproducible, and extensionless `tfplan` is ignored.

### W05 · Monitoring resources · `S`
**Depends on:** W04
**Do:** Log Analytics workspace (30-day retention) and workspace-based Application Insights.
**Done when:** Both exist and App Insights reports the workspace as its backing store.

**✅ Verified 2026-08-20.** `log-aisdemo-mrx0e` (PerGB2018, 30-day retention) and
`appi-aisdemo-mrx0e` reporting `ingestionMode: LogAnalytics` with `WorkspaceResourceId`
pointing at the workspace.

Two notes. The component's own `RetentionInDays` reads 90, but that field is vestigial for a
workspace-based instance — the workspace's 30 days is what actually governs, so don't be
misled by it. And `az monitor app-insights` needs a CLI extension that prompts on first use,
which hangs a non-interactive shell; `az resource show --resource-type
Microsoft.Insights/components` returns the same data with no extension.

### W06 · Storage account and tables · `S`
**Depends on:** W04
**Do:** Storage account plus the `Orders` and `AuditLog` tables (§8), and the `deployments`
blob container that Flex Consumption requires (added by W02).
**Done when:** Both tables are visible in Storage Browser.

**✅ Verified 2026-08-20.** `staisdemomrx0e` — Standard_LRS, TLS 1.2, HTTPS-only, public blob
access disabled. Tables `Orders` and `AuditLog` and container `deployments` all confirmed
present via `az storage ... --auth-mode login`.

Used `storage_account_id` on both the tables and the container, per the v5 change recorded in
§17.1. Published examples overwhelmingly use `storage_account_name`, which no longer works.

**Left deliberately open:** `allowSharedKeyAccess` is `true`. Disabling it would strengthen
the no-secrets story, but Terraform's table and container resources reach the storage data
plane with the account key unless the provider is switched to `storage_use_azuread`. Not
required by the spec, and a change that would need its own verification — noted rather than
smuggled in here.

### W07 · Service Bus namespace and queue · `S`
**Depends on:** W04
**Do:** Standard namespace and the `orders` queue with `max_delivery_count = 5`,
`lock_duration = PT1M`, dead-lettering on expiration. Topic deferred to W23.
**Done when:** The queue exists with the specified settings and its DLQ is addressable.

**✅ Verified 2026-08-20.** `sb-aisdemo-mrx0e` (Standard, TLS 1.2, Active) and queue `orders`
reporting `maxDeliveryCount: 5`, `lockDuration: PT1M`, dead-lettering on expiration enabled.

Standard tier carries the fixed monthly base charge that dominates this demo's cost — the
reason §16 assumes teardown between demos.

**Scope added during W07:** a diagnostic setting routing `AllMetrics` from the namespace into
the Log Analytics workspace. §10 lists "dead-letter queue depth over time" as a demo query,
but that is a Service Bus platform metric, not App Insights data — without this setting it is
visible only in the portal's Metrics explorer and is not queryable in KQL at all, so **W34 had
a query that could not run**. Metrics only; the available log categories cover management-plane
operations rather than per-message activity. Verified enabled and pointed at the workspace.

Note for W34: azurerm v5 replaced the old `metric`/`log` blocks with `enabled_metric` and
`enabled_log`. Also see the new §10.1 on the two telemetry schemas — queries must target
**workspace scope**, since `AzureMetrics` is unreachable from the App Insights blade.

### W08 · Function App · `M`
**Depends on:** W05, W06, W07
**Do:** Function App per the W02 hosting decision, Linux, .NET isolated, system-assigned
identity, with the §15.2 app settings including identity-based connection settings.
**Done when:** The app is running, reports the correct runtime, and exposes a principal ID.

**✅ Verified 2026-08-20.** `asp-aisdemo-mrx0e` (FC1, Linux) and `func-aisdemo-mrx0e` —
state `Running`, HTTPS-only, runtime `dotnet-isolated` 10, deployment via `blobContainer` at
`https://staisdemomrx0e.blob.core.windows.net/deployments` authenticated as
`SystemAssignedIdentity`, scale 2048 MB / max 40 instances, principal ID
`764433f9-f3dd-444a-8c79-44d5cc4c6c12`.

**⚠ Platform injects a storage key — see SPEC.md 9.2.1.** Azure adds an
`AzureWebJobsStorage` setting holding a connection string *with an account key*, which
Terraform neither manages nor reports as drift. The host resolves it ahead of
`AzureWebJobsStorage__accountName`, so the identity is bypassed until it is removed.
Confirmed that deleting it sticks and survives later applies. Deletion is assigned to **W14**
because the host needs the W09 role assignments in place before it can reach storage without
the key.

### W09 · RBAC role assignments · `M`
**Depends on:** W08
**Do:** The five role assignments in §9.2, scoped to the namespace and storage account. Add a
`time_sleep` after them to absorb propagation delay (§17 risk 4).
**Done when:** All five appear under Access control (IAM) on their scopes.
**Note:** This is the item most likely to fail for permission reasons. If W01 was skipped, it
fails here.

**✅ Verified 2026-08-20.** All five assignments present for principal
`764433f9-f3dd-444a-8c79-44d5cc4c6c12`: Service Bus Data Sender and Data Receiver on
`sb-aisdemo-mrx0e`; Storage Blob Data Owner, Queue Data Contributor, and Table Data
Contributor on `staisdemomrx0e`. No permission trouble — W01's Owner finding held.

Assignments took ~40s each to create, and `time_sleep.rbac_propagation` adds a further 60s
before apply completes. That minute is deliberate: without it the first invocation after an
apply can 403 for no visible reason, which is a miserable thing to debug mid-demo. Sixty
seconds is empirical, not a guarantee.

Blob access is **Data Owner** rather than Contributor because the Functions host manages blob
leases for singleton locks.

With these in place the identity can reach storage unaided, which is the precondition for
W14 deleting the platform-injected `AzureWebJobsStorage` key (SPEC.md 9.2.1).

### W10 · .NET solution scaffold · `M`
**Depends on:** W03
**Do:** `AisDemo.Functions` isolated-worker project, `Program.cs` with DI and App Insights
wiring, and the `Models/` types — `OrderSubmission`, `OrderRecord`, `OrderEvent`, the status
enum (§5.3).
**Done when:** The project builds and `func start` runs with zero functions registered.

**✅ Verified 2026-08-20.** Builds clean on `net10.0` with `TreatWarningsAsErrors`, and
`func start` reaches "No job functions found" — host up, nothing registered yet. The only
unhealthy probe is `azure.functions.webjobs.storage`, expected until Azurite arrives in W28.

**Telemetry decision: OpenTelemetry mode.** The Core Tools template now defaults to it —
`host.json` sets `telemetryMode: OpenTelemetry` and the worker uses
`Azure.Monitor.OpenTelemetry.Exporter` rather than the classic App Insights SDK. Kept, as
Microsoft's forward path. **Live Metrics is the open question**: it ships in the Azure Monitor
Distro, not the plain exporter, and §14.6 calls for it. To be verified empirically at W15; if
absent, that scenario falls back to queue depth spike/drain from the `AzureMetrics` data W07
already routes to the workspace.

**⚠ The exporter breaks local development if attached unconditionally.**
`UseAzureMonitorExporter()` throws `A connection string was not found` at startup when
`APPLICATIONINSIGHTS_CONNECTION_STRING` is unset, and the worker process dies — the host never
starts, so §12's fully-local path would be impossible. `Program.cs` now attaches the exporter
only when the setting is present.

**Table Storage has no decimal type.** Supported EDM types are string, bool, DateTime, double,
Guid, Int32, Int64, and binary. Domain logic works in `decimal` and converts to `double` only
at the entity boundary, which is documented on `OrderEntity`. Imperceptible for two-decimal
demo values; real money would use integer minor units.

**Note for W28:** `local.settings.json` is git-ignored, so a fresh clone has none and
`func start` will fail. W28 must commit a template alongside the compose stack.

### W11 · Services layer · `M`
**Depends on:** W10
**Do:** The client-factory abstraction from §12.1 — resolves a connection string locally or
`fullyQualifiedNamespace` + `DefaultAzureCredential` on Azure — plus a table repository over
`Orders` and typed configuration binding.
**Done when:** Unit-free smoke check: the factory returns a working sender against a real
namespace using developer credentials.
**Note:** Keep the local-versus-Azure branch inside this one file. It is the mitigation for
the emulator's SAS-only constraint, and it only works if nothing else duplicates the branch.

**✅ Verified 2026-08-20.** `AzureClientFactory` resolves a connection string locally or
`DefaultAzureCredential` when deployed, and is the only file that branches on which. Alongside
it: `DemoOptions` (typed config), `OrderRepository` and `AuditRepository`, and `OrderMessaging`
for queue send, topic publish, and DLQ receive. Builds clean; all registered in `Program.cs`.

Live send verified by acquiring a `https://servicebus.azure.net` token and posting to the
queue — HTTP 201, queue depth 1, probe then drained back to 0.

**⚠ Subscription Owner does not grant Service Bus data-plane access — new scope added.**
Azure RBAC separates `Actions` from `DataActions`; Owner's wildcard covers only the former.
The first send attempt as Owner returned **HTTP 401**, and succeeded only after assigning the
Data Sender role explicitly.

This is not merely a testing inconvenience. **Scenario 14.4 has the presenter inject a
malformed message directly onto the queue**, bypassing the gateway, and 14.3 involves
inspecting the dead-letter queue — neither is possible without operator data-plane roles. So
`rbac.tf` now also assigns Data Sender and Data Receiver to
`data.azurerm_client_config.current.object_id`, whoever runs Terraform.

Note the asymmetry: Storage does grant data access to Owner via the account key path, so
Storage Explorer works without extra roles. Service Bus has no such fallback once keyless.

### W12 · SubmitOrder · `M`
**Depends on:** W11
**Do:** HTTP `POST /api/orders`. Validate payload, compute `orderTotal`, write the `Accepted`
row, enqueue with `orderId` and `correlationId` application properties, return `202` with
`Location`.
**Done when:** A direct call to the function URL returns `202`, an `Accepted` row appears, and
a message lands on the queue.

**✅ Verified 2026-08-20** against real Azure resources, host run locally. `POST /api/orders`
returned **202** with camelCase body, `Location: /orders/{id}`, and an echoed
`x-correlation-id`; the `Accepted` row appeared with `OrderTotal 749.97` exact; queue depth
went to 1. An empty `items` array returned **400** `application/problem+json`. Test data
cleaned up afterwards.

Validation lives in the function rather than an APIM policy — §17 risk 7 left that policy's
tier support unverified, and scenario 14.4 injects malformed messages past the gateway
anyway, so the backend must handle bad input regardless.

**⚠ `DefaultAzureCredential` hangs 25 seconds off-Azure, then fails.** Its chain tries
ManagedIdentityCredential before AzureCliCredential, so on a developer machine it probes the
instance metadata endpoint at `169.254.169.254` — an address not routable outside Azure —
retries six times, then throws `AuthenticationFailedException` instead of falling through to
the developer's own `az login`. First observed as a 500 after a 24.9-second function
execution.

`AzureClientFactory.CreateCredential()` now excludes managed identity unless
`WEBSITE_INSTANCE_ID` is set, which the Functions platform injects. Same call afterwards:
**8.3 s cold, HTTP 202.** This matters beyond convenience — anyone pointing a local host at
real Azure to debug would otherwise hit an unexplained 25-second hang.

### W13 · ProcessOrder, minimal · `M`
**Depends on:** W12
**Do:** Queue trigger. Deserialize, set `Processing`, then `Completed`. No delay, failure
injection, business rules, or event publishing yet — those arrive in W24.
**Done when:** A submitted order reaches `Completed` without manual intervention.

**✅ Verified 2026-08-20** against real Azure, host run locally. Submitted an order, watched
`ProcessOrder` fire unprompted (261 ms), and confirmed the row reached `Completed` with
`AttemptCount 1`, a `ProcessedAt` timestamp, and `OrderTotal 251.0` exact. Queue drained to 0.

The trigger uses `Connection = "ServiceBusConnection"`, which resolves against
`ServiceBusConnection__fullyQualifiedNamespace` — the reason W08 named the setting that way.

Worth recording, since it was an open worry: the Functions **host** resolves trigger
credentials through its own chain, which `AzureClientFactory` cannot patch. It nonetheless
connected cleanly from a developer machine, falling through to CLI credentials rather than
stalling on the metadata endpoint the way the in-process `DefaultAzureCredential` did in W12.
So the two credential paths behave differently off-Azure, and only the in-process one needed
fixing.

Deserialization failure rethrows deliberately, so Service Bus retries and eventually
dead-letters — the mechanism scenario 14.4 depends on.

### W14 · deploy-functions.ps1 · `S`
**Depends on:** W08, W13
**Do:** Read Terraform outputs, run `func azure functionapp publish`. Also delete the
platform-injected `AzureWebJobsStorage` setting (SPEC.md 9.2.1) — this step is what makes the
no-secrets claim actually true, and it belongs here rather than in Terraform because the host
needs the W09 roles before it can reach storage without the key.
**Done when:** The script deploys from clean, is safely re-runnable, and
`az functionapp config appsettings list` shows no `AzureWebJobsStorage` entry afterwards.

### W15 · ⚑ Milestone: skeleton end-to-end · `S`
**Depends on:** W09, W14
**Do:** `terraform apply`, deploy functions, submit an order directly to the Function App.
**Done when:** The order reaches `Completed`, and App Insights shows the HTTP call and the
queue-triggered invocation stitched into one transaction.
**Why here:** This proves managed identity, identity-based bindings, RBAC propagation, and
trace correlation across the queue hop all work — the four things most likely to be wrong,
proven before any of them has dependents.

---

## Phase 2 — Gateway

### W16 · APIM service, API, operations, product · `M`
**Depends on:** W04
**Do:** Consumption-tier instance, `orders-api` at path `orders-demo`, the three operations
(§5), product `aisdemo-starter` published with subscription required.
**Done when:** The API is callable from the Azure portal test console with a subscription key.

### W17 · APIM backend wiring · `M`
**Depends on:** W16, W08
**Do:** Read the function host key via `azurerm_function_app_host_keys`, store it as a secret
named value, configure the backend and `set-backend-service`.
**Done when:** A call through the gateway reaches the Function App and returns its response.

### W18 · Core policies · `M`
**Depends on:** W17
**Do:** Correlation — generate a GUID, `set-header` with `exists-action="override"` so a
caller-supplied value is discarded (§5.1). CORS for the SWA origin and localhost. `on-error`
returning `application/problem+json` carrying the correlation ID.
**Done when:** A response echoes a gateway-generated correlation ID; a request sending its own
`x-correlation-id` is answered with a different one; a forced backend error returns
problem+json.

### W19 · Rate-limit policy · `S`
**Depends on:** W18
**Do:** `rate-limit-by-key` at operation scope on `POST /orders`, 10 per 60s, keyed on
`context.Subscription.Id`. The plain `rate-limit` policy is unavailable in this tier (§5.1).
**Done when:** The 11th call inside a window returns `429` and produces no Function invocation.

### W20 · Terraform outputs · `S`
**Depends on:** W16
**Do:** Gateway URL, subscription key, function app name, storage account name, SWA name and
deployment token, App Insights connection string. Mark secrets `sensitive`.
**Done when:** `terraform output` supplies everything the deploy scripts need.

### W21 · ⚑ Milestone: through-the-gateway end-to-end · `S`
**Depends on:** W19, W20
**Do:** Submit an order through APIM rather than directly.
**Done when:** `202` returns with a correlation ID, the order completes, and App Insights shows
one transaction spanning gateway and both functions.

---

## Phase 3 — Complete the messaging story

### W22 · GetOrder · `S`
**Depends on:** W11
**Do:** HTTP `GET /api/orders/{orderId}`, point-read `Orders`, return the §5.2 shape or `404`.
**Done when:** Polling through the gateway shows the status transition.

### W23 · Topic, subscriptions, filters · `S`
**Depends on:** W07
**Do:** `order-events` topic, `notifications` subscription with the SQL filter, `audit`
subscription with a catch-all (§6.3).
**Done when:** Both subscriptions exist and the filter rule reads back correctly.

### W24 · ProcessOrder, complete · `M`
**Depends on:** W13, W23
**Do:** Add the §7.1 behaviour — configurable delay, failure injection via `simulateFailure`
and `customerName == "FAIL"`, terminal business-rule rejection, the `Retrying` path with
attempt count and reason before rethrow, and event publication with `eventType`,
`orderTotal`, and `customerId` set as **application properties** so filters can see them.
**Done when:** A happy order completes and publishes; a poisoned order records `Retrying`,
increments `attemptCount`, and dead-letters after five attempts.
**Note:** Filters read message properties, not the body. Setting them only in the JSON payload
is the classic failure here, and it fails silently — the subscription simply receives nothing.

### W25 · NotificationHandler and AuditHandler · `M`
**Depends on:** W24
**Do:** Two subscription triggers. Notification simulates a send and emits a custom App
Insights event; audit appends to `AuditLog`.
**Done when:** A $50 order reaches only the audit handler; a $5,000 order reaches both.

### W26 · ReplayDeadLetter · `M`
**Depends on:** W24
**Do:** HTTP `POST /api/admin/replay?max=n`. Receive from the DLQ, resubmit to `orders`,
complete the DLQ message, mark rows `Replayed`, return the §5.2 count shape.
**Done when:** A dead-lettered order is drained, resubmitted, and reaches `Completed`, leaving
the DLQ empty.

### W27 · Telemetry enrichment · `S`
**Depends on:** W25, W26
**Do:** Add `orderId` and `correlationId` as custom dimensions across every function.
**Done when:** A Logs query filtering on one `orderId` returns every stage of that order.

---

## Phase 4 — Local development

### W28 · Emulator compose stack · `M`
**Depends on:** W24
**Do:** `local/docker-compose.yml` with the Service Bus emulator, its SQL Edge companion, and
Azurite. `local/config.json` mirroring the §6 topology. `local.settings.json` template.
**Done when:** `docker compose up` starts cleanly and the emulator reports its entities.
**Note:** Add the cross-referencing comment headers in both `config.json` and `servicebus.tf`
now (§12.1). It is the only drift mitigation in the MVP, and it is worthless if deferred.

### W29 · Verify the local flow · `S`
**Depends on:** W28
**Do:** Run the full path locally — submit, process, fan out, dead-letter, replay.
**Done when:** Every scenario except the APIM-specific ones works with no Azure dependency.

---

## Phase 5 — Demo surface

### W30 · Static Web App and deploy script · `M`
**Depends on:** W20
**Do:** SWA free-tier resource; `deploy-web.ps1` injecting gateway URL and subscription key
into a generated `config.js`, then deploying via the SWA CLI with the token passed by
environment variable, never written to disk (§17 risk 8).
**Done when:** A placeholder page is live at the SWA URL with config injected.

### W31 · Web UI · `L`
**Depends on:** W30, W22
**Do:** Single page, no framework. Order submission form including the `simulateFailure`
toggle and an amount that can cross the $500 filter threshold; a status table polling
`GET /orders/{id}`; a DLQ replay button; a link into App Insights.
**Done when:** Every demo scenario except the rate limit is drivable from the browser.

### W32 · demo.http · `S`
**Depends on:** W21, W26
**Do:** Requests covering all six scenarios (§14), including direct queue injection of a
malformed message for 14.4.
**Done when:** The file runs top to bottom in VS Code REST Client against a live deployment.

### W33 · load-test.ps1 and teardown.ps1 · `S`
**Depends on:** W21
**Do:** Concurrent order burst for scenario 14.6; teardown wrapping `terraform destroy`.
**Done when:** The burst drives visible queue depth; teardown leaves no resources behind.

### W34 · queries.kql · `S`
**Depends on:** W27
**Do:** End-to-end trace by order ID, processing-lag percentiles, failure and retry counts by
function, DLQ depth over time. Documentation only — not provisioned (§10).
**Done when:** Each query returns useful rows against a live deployment.

### W35 · runbook.md · `M`
**Depends on:** W31, W32, W34
**Do:** Presenter script for all six scenarios — what to type, what to show, what to say,
including the intentional rough edges (§5.3 stuck row, §8 hot partition, §9.4 browser key).
**Done when:** Someone other than the author can run the demo from it unaided.

---

## Phase 6 — Validation

### W36 · Run all six scenarios · `M`
**Depends on:** W35
**Do:** Execute §14 end to end against a fresh deployment, confirming each "watch for".
**Done when:** All six behave as specified, including the Application Map showing APIM,
Functions, Service Bus, and Storage.

### W37 · README · `M`
**Depends on:** W36
**Do:** Clone to working demo without outside reference — prerequisites, permissions, deploy,
run, tear down, plus the local development path.
**Done when:** It satisfies the last §18 criterion.

### W38 · Clean-room rebuild · `S`
**Depends on:** W37
**Do:** `terraform destroy`, delete local state, rebuild from scratch following only the README.
**Done when:** Apply completes under 10 minutes, all scenarios pass, and destroy leaves an
empty resource group. Every §18 box is then checkable.

---

## Critical path

```
W01 → W02 → W03 → W04 → W08 → W09 → W15 → W17 → W21 → W24 → W26 → W35 → W36 → W38
```

Roughly 38 items, of which 5 are `L`-or-`M` clusters that dominate the effort: the Function
App and RBAC pair (W08–W09), ProcessOrder complete (W24), the web UI (W31), and the runbook
(W35).

## Parallelizable

- **W10–W12** (.NET scaffold through SubmitOrder) needs only W03, so it can run alongside all of Phase 1's Terraform.
- **W16** (APIM) needs only W04, so the ~5–10 minute Consumption-tier provision can start early rather than blocking Phase 2.
- **W31** (web UI) and **W32** (`demo.http`) are independent of each other.
- **W34** (queries) can be drafted any time after W27.
