---
title: "Command Quick Reference"
description: "Executable Azure CLI and KQL checks for health, alerts, releases, park and resume, secret rotation, and service-level measurements"
tags:
  - "operations"
  - "runbook"
  - "commands"
  - "kql"
aliases:
  - "quick reference"
  - "commands"
  - "cheat sheet"
  - "kql"
  - "curl health"
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-11-01
volatility: "medium"
source_of_truth: "Azure Portal resources, Azure Monitor, and this quick reference"
verified_by:
  - "Operational procedure"
common_queries:
  - "How do I check platform health?"
  - "How do I park an environment?"
  - "What is the recent-errors query?"
see_also:
  - "docs/operations/runbook.md"
  - "docs/operations/dashboard-guide.md"
---

# Command Quick Reference

Use `{env}` only in service hostnames and replace it with `dev`, `tst`, or
`prd`. Use `{ENV}` only in Azure resource names and replace it with `DEV`,
`TST`, or `PRD`. Replace `<subscription-id>` with the ARN Azure subscription
ID. The [operations runbook](runbook.md) provides the response sequence around
these commands.

## Health check

Run this anonymous check before investigating an alert or a failed bulletin:

```bash
curl -sS https://ibp-aue-{env}-fna-app01.azurewebsites.net/api/health
```

Interpret the response by dependency. `storage` and `keyVault` are critical.
An unhealthy critical dependency means the Function App cannot run. `hereApi`,
`elevenLabs`, and `azureOpenAI` are non-critical reachability probes. Their
detail is generic, such as `reachable`, `HTTP 405`, or `unreachable`, and does
not expose URLs or account names. HTTP 200 is healthy, HTTP 503 is degraded,
and HTTP 403 with an "Unavailable" page means the Function App is stopped.
The probe does not validate vendor keys. Use Log Analytics traces for deeper
vendor diagnosis.

## Recent errors

Open **Monitor**, **Logs**, select
`IBP-AUE-{ENV}-LAW-APP01`, and run this query when recent worker failures need
inspection:

```kusto
FunctionAppLogs
| where TimeGenerated > ago(30m)
| where Level in ('Error','Warning')
| order by TimeGenerated desc
```

Log ingestion can lag by one to five minutes. An empty result inside that
window is not evidence of silence.

## Deploy, park and resume

Application releases run through the Azure DevOps deployment pipeline. In
Azure DevOps, open **Pipelines**, select the platform deployment pipeline,
choose the environment, and queue the run. A Portal-only operator cannot
deploy application code directly. Ask the release owner to queue the pipeline
when a release or rollback is required.

Parking and resuming are resource operations and can be performed from Azure
Portal or Azure CLI. Before parking, disable the environment's alert rules and
availability test in **Monitor**. Keep that silence-first order before stopping
the Function App. Parking removes the staging deployment slot because Basic
plans do not support slots; the next full deployment recreates it. The commands
below follow that sequence, including disabling autoscale before the plan
downgrade and retrying a capacity error.

```bash
SUBSCRIPTION_ID=<subscription-id>
RG=IBP-AUE-{ENV}-RSG-APP01
PLAN=IBP-AUE-{ENV}-PLN-APP01
FNA=IBP-AUE-{ENV}-FNA-APP01

# In Portal first: disable alert rules and the availability test.
# Park: stop the app, remove staging, disable autoscale, then downgrade.
az functionapp stop --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA"
if az functionapp deployment slot show --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA" --slot staging >/dev/null 2>&1; then
  az functionapp deployment slot delete --subscription "$SUBSCRIPTION_ID" \
    --resource-group "$RG" --name "$FNA" --slot staging
fi
az monitor autoscale update --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "${PLAN}-autoscale" --enabled false
for attempt in 1 2 3 4 5 6; do
  if az appservice plan update --subscription "$SUBSCRIPTION_ID" \
    --resource-group "$RG" --name "$PLAN" --sku B1; then
    break
  fi
  if [ "$attempt" -eq 6 ]; then
    echo "The plan downgrade did not succeed after capacity-error retries." >&2
    exit 1
  fi
  sleep 10
done

# Resume: the next full deployment recreates the removed staging slot.
az appservice plan update --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$PLAN" --sku P0v3
az functionapp start --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA"
az functionapp show --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA" --query state -o tsv
curl -sS https://ibp-aue-{env}-fna-app01.azurewebsites.net/api/health
az monitor autoscale update --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "${PLAN}-autoscale" --enabled true
```

After resume, use **Monitor**, **Alerts**, **Alert rules** to re-enable each
rule and the availability test. Run the health check and confirm the Function
App state is `Running`. The staging slot is not recreated by resume itself; it
returns with the next full deployment. Record the final plan SKU, Function App
state, autoscale state, alert state, and availability-test state.

Rollback is a rerun of the last successful Azure DevOps deployment pipeline
run for the environment. Confirm the run in **Pipelines**, then check
**Deployment Center**, the health endpoint, and one console bulletin.

## Alert diagnostics

Open **Monitor**, **Alerts**, **Alert rules** to see the alert history, rule
state, and action group. Use the following commands when CLI output is more
convenient:

```bash
# HTTP errors and no executions
az functionapp show --subscription <subscription-id> \
  --resource-group IBP-AUE-{ENV}-RSG-APP01 \
  --name IBP-AUE-{ENV}-FNA-APP01 --query state -o tsv
az monitor metrics alert show --subscription <subscription-id> \
  --resource-group IBP-AUE-{ENV}-RSG-APP01 \
  --name IBP-AUE-{ENV}-FNA-APP01-errors-alert -o json
az monitor metrics alert show --subscription <subscription-id> \
  --resource-group IBP-AUE-{ENV}-RSG-APP01 \
  --name IBP-AUE-{ENV}-FNA-APP01-no-executions -o json

# Azure OpenAI throttling
az monitor scheduled-query show --subscription <subscription-id> \
  --resource-group IBP-AUE-{ENV}-RSG-APP01 \
  --name IBP-AUE-{ENV}-FNA-APP01-ai-throttling -o json
az cognitiveservices usage list --subscription <subscription-id> \
  --location australiaeast

# Poison queue and synthesis failures
az monitor scheduled-query show --subscription <subscription-id> \
  --resource-group IBP-AUE-{ENV}-RSG-APP01 \
  --name IBP-AUE-{ENV}-FNA-APP01-poison-queue -o json
az monitor scheduled-query show --subscription <subscription-id> \
  --resource-group IBP-AUE-{ENV}-RSG-APP01 \
  --name IBP-AUE-{ENV}-FNA-APP01-synthesis-failures -o json

# Key Vault access denied
az monitor scheduled-query show --subscription <subscription-id> \
  --resource-group IBP-AUE-{ENV}-RSG-APP01 \
  --name IBP-AUE-{ENV}-FNA-APP01-kv-access-denied -o json

# Availability web test
az resource show --subscription <subscription-id> \
  --resource-group IBP-AUE-{ENV}-RSG-APP01 \
  --resource-type Microsoft.Insights/webtests \
  --name IBP-AUE-{ENV}-health -o json
```

After identifying the alert, follow the first-response table in the
[operations runbook](runbook.md). Alert rules are coarser than the service
levels. No alert fires on the generation latency objective, so open the
**Operations Overview** workbook and check the **Pipeline stage latency
(P50 / P95 / P99 seconds)** panel after any change to bulletin generation,
then run the query on the [SLO page](slo.md).

## Secret rotation

Rotate the vendor credential at its source, then write the new value directly
to Key Vault and restart the Function App. Test and production vaults normally
deny public network access; use a private network path. If an administrator
must use a temporary public path, enable it only for the write and disable it
immediately afterwards.

```bash
SUBSCRIPTION_ID=<subscription-id>
RG=IBP-AUE-{ENV}-RSG-APP01
KV=IBP-AUE-{ENV}-KYV-APP01
FNA=IBP-AUE-{ENV}-FNA-APP01

az keyvault secret set --subscription "$SUBSCRIPTION_ID" \
  --vault-name "$KV" --name "<secret-name>" --value "<new-value>"
az functionapp restart --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA"
```

If the vault is private, run the commands from an approved network path rather
than changing the vault policy. Confirm the health endpoint and complete one
controlled bulletin after the restart. The [dependencies register](dependencies.md)
lists each secret name and owner.

Teams re-consent must use an ARN-native account, never a guest identity from
another tenant. In Azure Portal, open the environment resource group, open the
Teams API connection, choose **Edit API connection**, choose **Authorise**,
select the ARN-native account, and save. Confirm **Connected** in the connection
overview and confirm a successful Logic App run in **Runs history**. The
[Teams notifications page](notifications.md) explains the consent failure mode.


## Zero-vendor-spend load testing

Use the Function App **Configuration**, **Application settings** blade to set
Providers__ExternalProviderMode to Fake for the load-test window. Restart
the Function App, run the approved load test against the environment, then
remove the setting and restart again:

```bash
SUBSCRIPTION_ID=<subscription-id>
RG=IBP-AUE-{ENV}-RSG-APP01
FNA=IBP-AUE-{ENV}-FNA-APP01

az functionapp config appsettings set --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA" \
  --settings Providers__ExternalProviderMode=Fake
az functionapp restart --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA"

# Run the approved load test against the environment here.

az functionapp config appsettings delete --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA" \
  --setting-names Providers__ExternalProviderMode
az functionapp restart --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA"
```

Do not leave fake-provider mode enabled outside the test window. Confirm that
the app setting is absent in **Configuration**, **Application settings**.


## Master `arn` CLI

Use the `arn` CLI as the operational front door. It targets the selected
environment and keeps health, logs, and job inspection consistent. Every
diagnostic below is read-only; each takes an explicit `--env` because the
default is `local` and the `local` path does not query a deployed
environment:

```bash
arn health --env test
arn logs --env test
arn job get <job-id> --env test
arn trigger --help
```

`arn job get` needs a verb: a bare `arn job <job-id>` binds the id as the
verb and exits 2. `arn logs` needs `--env dev|test|prod`: without it the
command prints the local-console pointer and exits 2. `arn health` without
`--env` reads the development environment because `local` maps to `dev`;
name the environment explicitly.

### Deliberate writes: triggering a bulletin

`arn trigger` is not a diagnostic. It generates a real bulletin in the
target environment (default market `sydney`, default `--via post` issues a
`POST /api/broadcast`). Run it only as a deliberate, authorised action, and
start with `arn trigger --help` to see the market, duration, voice, and
request-path options. The CLI uses the same authenticated API surface as
the console. Use the
Azure DevOps pipeline for releases; do not change application settings as an
ad-hoc deployment mechanism.

## Feed, delivery and poison diagnostics

Run these queries in the environment's Log Analytics workspace. The
`data.validity` metric records official-feed comparisons and HERE coverage;
`zetta.delivery.*` records playout outcomes.

### Official feed coverage

```kusto
customMetrics
| where timestamp > ago(24h) and name == "data.validity"
| extend market = tostring(customDimensions.market),
         measure = tostring(customDimensions.measure),
         value = todouble(customMeasurements.value)
| where measure startswith "official."
| summarize value = sum(value) by market, measure
| order by market asc, measure asc
```

### Official-feed validity probe

```kusto
customMetrics
| where timestamp > ago(24h) and name == "data.validity"
| extend market = tostring(customDimensions.market),
         measure = tostring(customDimensions.measure),
         value = todouble(customMeasurements.value)
| where measure in ("official.events", "official.incident_class",
                    "official.missing_from_here")
| summarize value = sum(value) by market, measure
| order by market asc, measure asc
```

The on-demand administrator probe is:
`POST /api/metrics/validity-probe`. The persisted coverage reports are
available at `GET /api/coverage` and `GET /api/coverage/unknown`.

### Zetta delivery

```kusto
customEvents
| where timestamp > ago(24h) and (name == "zetta.delivery.success"
                                  or name == "zetta.delivery.failure")
| project timestamp, name,
          market = tostring(customDimensions.market),
          reason = tostring(customDimensions.reason)
| order by timestamp desc
```

### Queue poison logs

```kusto
traces
| where timestamp > ago(24h)
| where message has "[PoisonQueue]"
   or message has "poison-queue"
| project timestamp, severityLevel, message,
          jobId = tostring(customDimensions.JobId)
| order by timestamp desc
```
## SLO measurement queries

Run these queries in **Monitor**, **Logs**, against
`IBP-AUE-{ENV}-LAW-APP01`. The [SLO page](slo.md) defines the signals and
targets.

### Broadcast success rate

```kusto
let window = ago(30d);
AppEvents
| where Name == "broadcast.bulletin.emitted" and TimeGenerated >= window
| summarize
    success = countif(tostring(Properties.success) == "true"),
    total   = count()
| extend slo = success * 1.0 / total
```

### API availability

```kusto
let window = ago(30d);
AppRequests
| where TimeGenerated >= window and Url has "/api/" and Url !has "/api/health"
| summarize
    healthy = countif(toint(ResultCode) < 500),
    total   = count()
| extend slo = healthy * 1.0 / total
```

### End-to-end generation P99

```kusto
let window = ago(7d);
AppMetrics
| where Name == "broadcast.stage.orchestration_total.duration_seconds"
  and TimeGenerated >= window
| extend value = Sum / ItemCount
| summarize p99 = percentile(value, 99)
```
