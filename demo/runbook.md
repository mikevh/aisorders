# Presenter runbook

Six scenarios, roughly ten minutes. Written so someone who did not build this
can run it.

Companion files: [`demo.http`](./demo.http) for requests, [`queries.kql`](./queries.kql)
for telemetry. Spec references (§) point at [SPEC.md](../SPEC.md).

---

## Before you start

**Warm the gateway.** APIM Consumption cold-starts. Send one throwaway order a
few minutes before you present, or your opening beat is a five-second pause.

**Decide the processing delay.** The default 250 ms means orders complete almost
instantly — good for scenarios 1–3, useless for scenario 6, where you want to
watch a queue fill. If you plan to run the burst:

```
cd infra
terraform apply -var processing_delay_ms=3000
```

**Have three things open:**

| | |
|---|---|
| Tab 1 | The demo UI, or `demo.http` in your editor |
| Tab 2 | Log Analytics workspace → Logs, with `queries.kql` pasted in |
| Tab 3 | Application Insights → Application Map |

**Check it is alive.** Submit one order and confirm it reaches `Completed`. If
it does not, stop and fix it now rather than in front of people.

---

## The one-sentence framing

> An order arrives at a gateway, gets put on a queue, and is processed by
> something that has never heard of the caller — and we can still follow one
> order through all of it.

That is the whole demo. Everything below is evidence for it.

---

## 1 · Happy path

**Do:** Submit a $749.97 order. Show the response, then poll the status URL two
or three times.

**Say:** The API returned `202`, not `200`. It has not done the work — it has
accepted responsibility for doing it. The `Location` header says where the
answer will appear.

**Watch for:** The status walking `Accepted → Processing → Completed` as you
poll. Polling twice in quick succession usually catches it mid-flight.

**If someone asks** why not just do the work synchronously: that is scenario 6.

---

## 2 · Fan-out and filtering

**Do:** Submit the "below threshold" preset ($50), then the "above threshold"
one ($5,000). Both complete. Run query 6 in `queries.kql`.

**Say:** One event was published. Two subscribers are listening. The audit
subscriber sees everything; the notification subscriber has a SQL filter and
only sees completed orders above $500.

**Watch for:** The small order produces one row, the large one produces two.

**The point worth landing:** the publisher does not know either subscriber
exists. Adding a third changes no code that already runs.

**If someone asks** where the filter reads from — message *properties*, not the
body. Filters cannot see message bodies at all. Getting that wrong is silent:
the subscription simply receives nothing, with no error anywhere (§6.2).

---

## 3 · Poison message → dead letter → replay

The most valuable four minutes in the demo. Do not rush it.

**Do:** Submit with `simulateFailure: true` (the UI checkbox). Poll the status
repeatedly for about a minute.

**Say:** The processor throws every time. Service Bus redelivers, five times,
then gives up and moves the message to the dead-letter queue.

**Watch for:** `attemptCount` climbing to 5, `status` at `Retrying`, the failure
reason attached — **and then nothing changing, ever again.**

**Stop here and point at that.** The order row is stuck at `Retrying / 5`. The
message is in the dead-letter queue. Nothing is coming to fix it, because
nothing is looking. That gap is not a bug in the demo; it is the reason
dead-letter monitoring exists. An order your system silently gave up on looks
exactly like an order still in progress.

**Then:** Run query 5 to show DLQ depth going to 1. Now hit replay.

**Watch for:** `drained 1, resubmitted 1`, and the order reaching `Completed`.

**Be honest about this bit:** replay clears the injected failure flag, standing
in for the remediation an operator would perform first. Replaying an unchanged
poison message just poisons it again — you can demonstrate exactly that with
`?remediate=false`. Say so; the audience will respect it more than a demo that
pretends replay is magic.

---

## 4 · Malformed message

**Do:** `./scripts/inject-malformed.ps1`, then watch the DLQ.

**Say:** This one never went through the gateway. It was written straight onto
the queue, which is what a misbehaving upstream producer looks like.

**Watch for:** It dead-letters like the last one, but **no order row exists for
it at all** — it never passed through the code that creates rows.

**Then:** Hit replay again. `drained 1, resubmitted 0`. A message that cannot be
deserialized is discarded rather than looped back, because replaying it would
only return it to the dead-letter queue.

**The contrast to draw:** scenario 5 is bad input the gateway catches. This is
bad input the gateway never sees. Both end up in the same place, and only one
of them costs five function invocations.

---

## 5 · Gateway rejection

**Do:** From `demo.http`, send the no-key and bad-key requests. Then the valid
key with an invalid body.

**Say:** The first two are refused by the gateway. The third reaches the
function, which rejects it on business rules.

**Watch for:** Run query 7. The 401s appear as gateway rows with **no matching
function invocation**. Compute was never touched, never scaled, never billed.

**Read the room before promising rate limiting.** On the Consumption tier there
is none — `rate-limit`, `rate-limit-by-key`, `quota`, and `quota-by-key` are all
rejected at deploy time (§5.1.1). If someone asks, the honest answer is that
this tier does not support it and a paid tier does; set `apim_sku_name` and the
policy appears automatically.

---

## 6 · Burst and scale-out

**Do:** Confirm you raised `processing_delay_ms` first. Then
`./scripts/load-test.ps1 -Count 100`. Run query 5 while it goes.

**Say:** A hundred orders, submitted faster than they can be processed.

**Watch for:** Queue depth spiking and then draining. The API stayed responsive
throughout — every one of those returned `202` immediately.

**Close on the Application Map.** It shows the whole topology, drawn from
telemetry rather than a diagram anyone maintained.

**Then land the ending:** pick any order ID, paste it into query 1, and show
every stage of that one order — gateway, producer, queue hop, processor, both
subscribers — as a single correlated transaction across six spans.

---

## Rough edges to name before someone else does

These are deliberate. Saying them first is stronger than being caught.

| | |
|---|---|
| **The stuck order row** (§5.3) | Nothing updates it after dead-lettering. That is the lesson of scenario 3, not an oversight. |
| **`PartitionKey = "ORDER"`** (§8) | A single fixed partition is a hot-partition antipattern. It buys a clean point-read by order ID for a demo; real systems partition by something with cardinality. |
| **The subscription key in the browser** (§9.4) | It ships to the client, so anyone with the site URL can call the API. Acceptable for a destroyable demo environment; never present it as a pattern. |
| **One remaining secret** (§9.3) | APIM authenticates to the Function App with a host key. Everything else uses managed identity. The keyless alternative needs an Entra app registration and tenant permissions, which would stop most people deploying this at all. |
| **Topology declared twice** (§12.1) | The Service Bus emulator cannot create entities at runtime, so `local/config.json` mirrors `infra/servicebus.tf`. They will drift. |

---

## Afterwards

```
./scripts/teardown.ps1
```

Service Bus Standard's base charge is the only meaningful cost; destroyed, the
environment costs nothing. If you kept the containers running:

```
cd local && docker compose down
```
