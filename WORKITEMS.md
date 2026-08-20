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

### W05 · Monitoring resources · `S`
**Depends on:** W04
**Do:** Log Analytics workspace (30-day retention) and workspace-based Application Insights.
**Done when:** Both exist and App Insights reports the workspace as its backing store.

### W06 · Storage account and tables · `S`
**Depends on:** W04
**Do:** Storage account plus the `Orders` and `AuditLog` tables (§8).
**Done when:** Both tables are visible in Storage Browser.

### W07 · Service Bus namespace and queue · `S`
**Depends on:** W04
**Do:** Standard namespace and the `orders` queue with `max_delivery_count = 5`,
`lock_duration = PT1M`, dead-lettering on expiration. Topic deferred to W23.
**Done when:** The queue exists with the specified settings and its DLQ is addressable.

### W08 · Function App · `M`
**Depends on:** W05, W06, W07
**Do:** Function App per the W02 hosting decision, Linux, .NET isolated, system-assigned
identity, with the §15.2 app settings including identity-based connection settings.
**Done when:** The app is running, reports the correct runtime, and exposes a principal ID.

### W09 · RBAC role assignments · `M`
**Depends on:** W08
**Do:** The five role assignments in §9.2, scoped to the namespace and storage account. Add a
`time_sleep` after them to absorb propagation delay (§17 risk 4).
**Done when:** All five appear under Access control (IAM) on their scopes.
**Note:** This is the item most likely to fail for permission reasons. If W01 was skipped, it
fails here.

### W10 · .NET solution scaffold · `M`
**Depends on:** W03
**Do:** `AisDemo.Functions` isolated-worker project, `Program.cs` with DI and App Insights
wiring, and the `Models/` types — `OrderSubmission`, `OrderRecord`, `OrderEvent`, the status
enum (§5.3).
**Done when:** The project builds and `func start` runs with zero functions registered.

### W11 · Services layer · `M`
**Depends on:** W10
**Do:** The client-factory abstraction from §12.1 — resolves a connection string locally or
`fullyQualifiedNamespace` + `DefaultAzureCredential` on Azure — plus a table repository over
`Orders` and typed configuration binding.
**Done when:** Unit-free smoke check: the factory returns a working sender against a real
namespace using developer credentials.
**Note:** Keep the local-versus-Azure branch inside this one file. It is the mitigation for
the emulator's SAS-only constraint, and it only works if nothing else duplicates the branch.

### W12 · SubmitOrder · `M`
**Depends on:** W11
**Do:** HTTP `POST /api/orders`. Validate payload, compute `orderTotal`, write the `Accepted`
row, enqueue with `orderId` and `correlationId` application properties, return `202` with
`Location`.
**Done when:** A direct call to the function URL returns `202`, an `Accepted` row appears, and
a message lands on the queue.

### W13 · ProcessOrder, minimal · `M`
**Depends on:** W12
**Do:** Queue trigger. Deserialize, set `Processing`, then `Completed`. No delay, failure
injection, business rules, or event publishing yet — those arrive in W24.
**Done when:** A submitted order reaches `Completed` without manual intervention.

### W14 · deploy-functions.ps1 · `S`
**Depends on:** W08, W13
**Do:** Read Terraform outputs, run `func azure functionapp publish`.
**Done when:** The script deploys from clean and is safely re-runnable.

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
