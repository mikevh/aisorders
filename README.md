# aisorders

A deployable demonstration of **Azure API Management**, **Service Bus**, **Azure Functions**,
and **Application Insights** working together as one system, provisioned entirely with
Terraform.

An order is submitted through the gateway, buffered on a queue, processed asynchronously,
fanned out to filtered topic subscribers, and traceable end to end as a single Application
Insights transaction. Failures retry, dead-letter, and can be replayed on demand.

> **Status: under construction.** The specification and work breakdown are complete; the
> implementation has not started. There is nothing to deploy yet. Setup instructions below
> cover only what has been verified — the full deploy-and-run guide arrives with W37.

## Documents

| Document | What it covers |
|---|---|
| [SPEC.md](./SPEC.md) | Architecture, resource inventory, API and message contracts, identity model, demo runbook |
| [WORKITEMS.md](./WORKITEMS.md) | 38 sequenced work items with dependencies and acceptance checks |

## Progress

- [x] **W01** — environment and tooling verified
- [x] **W02** — runtime, region, and provider versions pinned
- [x] **W03** — repository scaffold
- [x] **W04** — Terraform root module: resource group, naming, tags
- [x] **W05** — Log Analytics and workspace-based Application Insights
- [x] **W06** — storage account, demo tables, deployment container
- [x] **W07** — Service Bus namespace and orders queue
- [x] **W08** — Function App on Flex Consumption with system-assigned identity
- [x] **W09** — role assignments for the managed identity
- [x] **W10** — .NET isolated worker scaffold, models, serializer defaults
- [x] **W11** — client factory, repositories, messaging, typed config
- [x] **W12** — SubmitOrder
- [x] **W13** — ProcessOrder (minimal)
- [x] **W14** — function deploy script
- [x] **W15** — ⚑ walking skeleton running end to end on Azure
- [x] **W16–W21** — ⚑ API Management: gateway path verified end to end
- [x] **W22–W26** — status endpoint, fan-out, dead-letter replay
- [x] **W27** — telemetry enrichment
- [ ] W28–W29 — local development against the Service Bus emulator
- [ ] W30–W35 — web UI, request collection, presenter runbook
- [ ] W36–W38 — validation, documentation, clean-room rebuild

## Prerequisites

Versions below are the ones verified in W01, not minimums unless stated.

| Tool | Version | Notes |
|---|---|---|
| Azure CLI | 2.88.0 | Authenticated, with a subscription selected |
| Terraform | 1.15.8 | ≥ 1.9 required |
| .NET SDK | 10.0.400 | Runtime target is `dotnet-isolated` 10 |
| Azure Functions Core Tools | 4.12.1 | Needed from W14 onward |
| Node.js | 24.12.0 | Static Web Apps CLI runs via `npx` |
| Docker Desktop | 29.7.2 | Local development only |

**Permissions.** You need Contributor *and* User Access Administrator, or Owner, on the
target subscription. Contributor alone passes every other check and then fails when the
role assignments are created. Verify before starting.

## Layout

```
infra/      Terraform root module and APIM policy XML
src/        AisDemo.Functions — .NET isolated worker
web/        Static demo UI
local/      docker-compose stack for the Service Bus emulator and Azurite
scripts/    Deploy, load-test, and teardown scripts
demo/       Request collection, KQL queries, presenter runbook
```

## Cost

Designed to be torn down between demos. Service Bus Standard's fixed base charge dominates
at roughly $10–15/month if left running; everything else sits inside free tiers at demo
volume. Destroyed, it costs nothing.
