# aisorders

A deployable demonstration of **Azure API Management**, **Service Bus**,
**Azure Functions**, and **Application Insights** working as one system,
provisioned entirely with Terraform.

An order is submitted through the gateway, buffered on a queue, processed
asynchronously, fanned out to filtered topic subscribers, and traceable end to
end as a **single Application Insights transaction spanning six spans**.
Failures retry, dead-letter, and can be replayed on demand.

```
client ──► API Management ──► SubmitOrder ──► orders queue ──► ProcessOrder
                                    │                              │
                                    ▼                              ▼
                             Table Storage                 order-events topic
                                                            │            │
                                          notifications (filtered)     audit
```

| Document | What it covers |
|---|---|
| [SPEC.md](./SPEC.md) | Architecture, resources, contracts, identity model |
| [WORKITEMS.md](./WORKITEMS.md) | The 38-item build log, with what each step actually turned up |
| [demo/runbook.md](./demo/runbook.md) | Presenter script for the six scenarios |

---

## Prerequisites

| Tool | Verified with | Notes |
|---|---|---|
| Azure CLI | 2.88.0 | `az login`, with a subscription selected |
| Terraform | 1.15.8 | ≥ 1.9 required |
| .NET SDK | 10.0.400 | Runtime target is `dotnet-isolated` 10 |
| Azure Functions Core Tools | 4.12.1 | v4 |
| Node.js | 24.12.0 | The Static Web Apps CLI runs through `npx` |
| PowerShell | 7+ | The scripts are `#Requires -Version 7` |
| Docker Desktop | 29.7.2 | Local development only |

**Permissions.** You need **Owner**, or Contributor *and* User Access
Administrator, on the target subscription. Contributor alone passes every other
check and then fails when the role assignments are created.

Note that **subscription Owner does not grant Service Bus data-plane access** —
Azure RBAC separates `Actions` from `DataActions`. Terraform assigns the
operator the Data Sender and Data Receiver roles, which scenario 14.4 needs.

---

## Deploy

```bash
az login
az account set --subscription <id>

cd infra
cp example.tfvars terraform.tfvars   # then edit it
terraform init
terraform apply
```

`terraform apply` provisions infrastructure only. Application code and the UI
deploy separately, so either can be redeployed without touching the other:

```powershell
./scripts/deploy-functions.ps1   # builds and publishes the six functions
./scripts/deploy-web.ps1         # generates config.js, deploys the UI
```

Then open the site:

```bash
cd infra && terraform output -raw static_web_app_url
```

A cold apply takes under ten minutes. Roughly a minute of that is a deliberate
pause after the role assignments — they are eventually consistent, and without
it the first invocation can fail with a 403 for no visible reason.

### Run the demo

- **Browser** — the Static Web App URL above
- **Editor** — [`demo/demo.http`](./demo/demo.http), filling in the two variables from `terraform output`
- **Scripts** — `./scripts/load-test.ps1`, `./scripts/inject-malformed.ps1`

Telemetry queries live in [`demo/queries.kql`](./demo/queries.kql). Run them
from the **Log Analytics workspace**, not the Application Insights blade — the
same data carries different table and column names in each, and the queue-depth
metric is only reachable from the workspace.

### Tear down

```powershell
./scripts/teardown.ps1
```

---

## Local development

Runs fully locally: Service Bus emulator, its required SQL Edge companion, and
Azurite. No Azure resources, no credentials.

```bash
cd local
docker compose up -d
cp local.settings.example.json ../src/AisDemo.Functions/local.settings.json

cd ../src/AisDemo.Functions
func start
```

Everything except the APIM scenarios works this way, filtered fan-out included.

Two constraints come with the emulator, both in §12.1:

- **Topology is declared twice.** The emulator cannot create entities at
  runtime, so `local/config.json` mirrors `infra/servicebus.tf`. Each file
  points at the other; change one, change the other.
- **Local auth differs.** The emulator accepts only a SAS connection string,
  while the deployed app uses managed identity with no secrets.
  `Services/AzureClientFactory.cs` is the single place that difference lives.

---

## Cost

Designed to be destroyed between demos. **Service Bus Standard's fixed base
charge is the only meaningful line item** — roughly $10–15/month if left
running. API Management Consumption (1M free calls), Functions Flex
Consumption, Static Web Apps Free, and the first 5 GB/month of Application
Insights ingestion are all effectively free at demo volume.

Destroyed, it costs nothing.

---

## Layout

```
infra/      Terraform root module and APIM policy XML
src/        AisDemo.Functions - .NET isolated worker, six functions
web/        Static demo UI, no framework
local/      Compose stack for the Service Bus emulator and Azurite
scripts/    Deploy, load-test, inject, and teardown
demo/       Request collection, KQL queries, presenter runbook
```

---

## Things worth knowing before you change anything

Each of these cost a debugging cycle to find. `WORKITEMS.md` has the full
account.

**Rate limiting does not exist on APIM Consumption.** Not just `rate-limit` —
`rate-limit-by-key`, `quota`, and `quota-by-key` are all rejected at deploy
time. Set `apim_sku_name` to a paid tier and the policy appears automatically.

**The Functions host reserves the `admin` route segment.** A function routed
there registers cleanly, appears in the host's own function listing, and
returns 404 to everything. The replay function uses `dlq/replay`, with APIM
rewriting the public path.

**Azure injects a storage account key.** Creating the Function App adds an
`AzureWebJobsStorage` connection string that the host resolves *ahead of* the
identity-based setting, silently bypassing the managed identity. Terraform
neither manages it nor reports drift. `deploy-functions.ps1` deletes it.

**`WEBSITE_INSTANCE_ID` is not set on Flex Consumption.** Anything using it to
detect "running in Azure" will be wrong. Credential selection keys on
`IDENTITY_ENDPOINT` instead.

**Always clean before publishing.** A stale `functions.metadata` from an
incremental build once deployed four of six functions with a successful build
*and* a successful publish, and nothing reported a problem.

**Service Bus subscription filters read message properties, not bodies.**
Setting a field only in the JSON payload means the filter matches nothing —
silently, with no error anywhere.
