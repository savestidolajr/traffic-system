---
title: "Operations Runbook"
description: "Incident response, Azure health checks, release handling, park and resume procedures, secret rotation, and audit retrieval for the ARN Information Bulletin Platform"
tags: [operations, runbook, alerts, incident-response, on-call]
purpose: "runbook"
status: "current"
last_verified: 2026-09-03
review_after: 2026-10-03
volatility: "medium"
source_of_truth: "Azure Portal resources, Azure Monitor rules, and this runbook"
verified_by:
  - "Azure resource inventory and operational procedure"
  - "2026-09-03: secret rotation table reconciled against the live vaults. OfficialFeeds--QldApiKey and Here--MapApiKey do not exist yet and are marked as creation steps; AiSettings--AzureAI--ApiKey exists but is inert and must stay empty so managed-identity auth is preserved"
aliases:
  - runbook
  - ops runbook
  - on-call guide
  - incident response
common_queries:
  - "An alert fired. What do I do?"
  - "How do I deploy or roll back?"
  - "How do I rotate a secret?"
  - "A bulletin did not air. Why?"
  - "How do I retrieve audit evidence?"
see_also:
  - "docs/operations/as-built.md"
  - "docs/operations/notifications.md"
  - "docs/operations/disaster-recovery.md"
  - "docs/operations/slo.md"
---

# Operations Runbook

This runbook is for the ARN technology person responding to an incident. The
instructions use Azure Portal blades and Azure CLI commands. The environment
inventory page lists the console addresses and resource names.

## First five minutes of any incident

1. Check the anonymous health endpoint for the affected environment:

   ```bash
   curl -sS https://ibp-aue-{env}-fna-app01.azurewebsites.net/api/health
   ```

   Replace `{env}` with `dev`, `tst`, or `prd`. A response of HTTP 200 means
   that the critical dependencies are reachable. HTTP 503 means that storage or
   Key Vault is unhealthy. The HERE, ElevenLabs, and Azure OpenAI checks are
   reachability probes and may be unhealthy while the application still
   responds.

2. In Azure Portal, open **Resource groups**, choose
   `IBP-AUE-{ENV}-RSG-APP01`, then open
   `IBP-AUE-{ENV}-FNA-APP01`. On **Overview**, record the Function App state,
   host status, and latest restart time.

3. Open **Monitor**, then **Workbooks**, and select the environment's
   **Operations Overview** workbook. Set the time range wide enough to include
   the reported event.

4. Open **Monitor**, then **Logs**, select
   `IBP-AUE-{ENV}-LAW-APP01`, and run this query:

   ```kusto
   FunctionAppLogs
   | where TimeGenerated > ago(30m)
   | where Level in ('Error','Warning')
   | order by TimeGenerated desc
   ```

   Log ingestion can take one to five minutes. An empty result inside that
   window is not evidence that no failure occurred.

5. Open the console and record the station, bulletin ID, slot, status, and
   listener-facing outcome. Do not repeatedly submit the same bulletin while
   the first response is still being investigated.

## What the listener experiences during failures

The ARN Information Bulletin Platform is fail-safe. Generation is deterministic-first:
the conversational workflow moves through writing, composition, and editing stages
using the Azure OpenAI script generation model, then deterministic content, style,
safety, and slot-fit gates decide what can proceed. The following table tells you
what reaches air when a generation attempt cannot complete.

| Failure | On-air effect |
|---|---|
| A bulletin generation attempt fails | The watchdog covers the slot from the same station/day fallback chain: the most recent eligible clear-road bulletin, then standby audio. The failed draft remains available for diagnosis; the platform does not create an unbounded retry storm. |
| The candidate set cannot fill the slot | The deterministic slot-filling loop grows the permitted candidate menu and re-drafts at a corrected target when needed. It retains the fetched evidence and uses the fallback chain if the slot still cannot be filled. |
| Curation is enabled but no operator acts | The job waits only until its slot deadline; the watchdog then auto-proceeds. An absent operator never turns a scheduled slot into dead air. |
| HERE, an official feed, Azure OpenAI, or ElevenLabs is unavailable | The affected source is recorded in diagnostics and safe fallback content continues to air. Do not restart repeatedly; check the vendor and network path, then escalate a sustained fault. |
| An operator rejects a bulletin | The console offers regeneration with operator feedback or the fallback path. Only a bulletin that is actually published as `Delivered` is eligible to teach Station Lessons; rejected or unpublished edits do not change station guidance. |

## Alert catalogue and first response

Open **Monitor**, **Alerts**, then **Alert rules** to search by the Function App
name and alert suffix. An alert rule existing in the resource group does not
prove that an email, SMS, or Teams card was delivered. Check **Alerts**,
**Alert history**, and the configured **Action groups** separately.

| Alert suffix | Meaning | First response |
|---|---|---|
| `-errors-alert` | One or more HTTP 5xx responses were recorded. | In **Monitor**, **Logs**, run the recent-errors query above. Then inspect the affected route and the **Top exceptions** panel in **Operations Overview**. |
| `no-executions` | The Function App has not recorded an execution in the alert window. | In **Resource groups**, open the Function App and check **Overview**. A deliberately parked environment is expected to be quiet, so confirm the environment state before treating this as an outage. |
| `health-availability` (Sev 1) | The `/api/health` web test failed from two or more of its three probe locations. | Run the health check directly and record the HTTP status and named check results. Confirm the web test and its rule are enabled (parking disables both), then inspect private endpoint, DNS or network changes if the Function App itself is responding. |
| `ai-throttling` | Azure OpenAI or another Cognitive Services dependency returned repeated 429 responses. | Open the Azure OpenAI resource, then **Resource health** and **Metrics**. Check the **Dependency health (HERE / ElevenLabs / Azure OpenAI)** panel in **Operations Overview** for the affected window and route sustained failures to the vendor. |
| `poison-queue` | A queue message exhausted its retry attempts. | Open the console and inspect the affected bulletin's logs and status history. Correct the cause, then use the console's regeneration action. |
| `synthesis-failures` | ElevenLabs audio synthesis failed repeatedly. | Open **Operations Overview** and check the **Dependency health (HERE / ElevenLabs / Azure OpenAI)** panel for ElevenLabs status and the **TTS characters synthesised by market** panel for usage, then verify the Key Vault key and route a vendor outage to the vendor. |
| `bulletin-failure-rate` | The total bulletin failure rate exceeded its threshold in the alert window. | Open **Operations Overview** and review the **Bulletins emitted over time** and **Bulletins by market** panels for the affected window, then follow the first-response steps for a bulletin that failed to air. |
| `dependency-unhealthy` | An external dependency reported unhealthy or unreachable. | Open **Operations Overview** and check the **Dependency health (HERE / ElevenLabs / Azure OpenAI)** panel for the affected window, then route a sustained provider fault to the vendor. |
| `kv-access-denied` | Key Vault refused a secret read by an authenticated caller. | Open the Function App **Identity** blade, record the system-assigned identity, then open the Key Vault **Access control (IAM)** blade and confirm the identity has **Key Vault Secrets User**. If the role is present, the rule can also fire when the vault's network rules refuse an authenticated identity, so confirm the Function App reaches the vault over the approved private link. |

Alert coverage is intentionally coarser than the service-level objectives. The
HTTP error alert counts raw 5xx responses, the synthesis alert covers only one
generation stage, and no alert computes the rolling error-budget formulas. No
alert fires on the generation latency objective. After any change to bulletin
generation, open **Operations Overview** and check the **Pipeline stage latency
(P50 / P95 / P99 seconds)** panel, then run the latency query on the
[SLO page](slo.md).

## Deploy and roll back

Code releases run through Azure DevOps pipeline **108** (the deployment pipeline) from the `master` branch in Azure DevOps.
Queue a deliberate manual `master` run; an automatic push deploys nothing.
Queuing a run needs Queue builds access to pipeline 108 from an ARN-native account; a guest account cannot queue a run.
The `test` stage waits for approval on the `test` environment and the `production` stage waits for approval on the `production` environment.
Approving a run needs Approver permission on that environment. The pipeline owner queues releases and the deployment approver approves them; see the [support model](support-model.md) for both roles.
The queue parameters are:

| Parameter | Values / default | Effect |
|---|---|---|
| `targetEnvironment` | `none` (default), `dev`, `test`, or `prod` | Selects the compiled deployment path: `dev` → `Deploy_Dev`; `test` → `Dev` then `Test`; `prod` → `Dev` then `Test` then `Prod`. |
| `deployInfrastructure` | `true` (default) | Runs Bicep and autoscale deployment. Set `false` only for an application-only deployment; Functions, the web console, the documentation site, and post-deploy smoke still run. |
| `enableEvalGate` | `false` (default) | Adds the golden evaluation gate when the pipeline owner requires it (see the [support model](support-model.md)). |

All compiled stages consume the same build artifacts; later stages do not
recompile. Deploy stages are deliberate manual `master` runs. The `test` stage waits for approval on the `test` environment and the `production` stage waits for approval on the `production` environment:

| Target | Required queue parameters and gates |
|---|---|
| Development | Set `targetEnvironment=dev`. Leave `deployInfrastructure=true` for a full release, or set it to `false` for an application-only release. Monitor the run and the Function App **Deployment Center** before asking for a bulletin check. |
| Test | Set `targetEnvironment=test`; the run compiles Dev then Test from the same artifacts. Wait for approval on the `test` environment. Both test stations generate on schedule with playout disabled, so a release changes generation but delivers nothing until ARN arms a station (verified 2026-09-03). |
| Production | Set `targetEnvironment=prod`; the run compiles Dev, Test, then Prod from the same artifacts. Wait for approval on the `test` and `production` environments. Production is frozen at the last known-good fallback and must not be changed as part of routine pilot testing (verified 2026-09-03). |

With the default `targetEnvironment=none`, the run compiles and packages only.
Therefore an automatic push cannot deploy anything. An operator with Azure
Portal access but no Queue builds access to pipeline 108 cannot deploy application
code directly; ask the pipeline owner to queue the run (see the [support model](support-model.md)). Do not recreate a
release from a local copy or a hand-written package.

The station safety rule is independent of the pipeline gate: only one
environment may arm a station at a time. Test and production write to the
shared `ibpauetststaftp01/informationbulletins` drop, so do not arm production
while test owns the station. Development delivery is disabled. Confirm the live
schedule and delivery settings in the console before and after a release.

To roll back, queue the last successful pipeline 108 run for the affected
environment with `targetEnvironment` set to `dev`, `test`, or `prod`, retaining
the required approvals. Keep `deployInfrastructure=true` unless an
application-only rollback is explicitly intended; with `false`, Bicep and
autoscale are skipped while Functions, the web console, the documentation site, and smoke still run. Record
the run, commit, and target environment. Confirm the Function App **Deployment
Center**, **Activity log**, health endpoint, and one controlled bulletin or
fallback outcome. Re-check the station arming rule before and after: only one
environment may arm a given station, no station is armed today, and production
remains frozen unless ARN explicitly authorises a change.
Infrastructure changes are applied by the same pipeline; do not alter
unrelated resources while investigating an incident.

The application loads secrets from Key Vault at startup over its approved private
endpoint, using its managed identity. To rotate a secret, write a new Key Vault
version with `az keyvault secret set` and restart the Function App. Do not re-save
Configuration or application settings after the rotation; secrets are not supplied
through application-setting references.

## Park and resume for cost control

Parking stops the Function App and reduces the App Service plan to B1. Resume
returns the plan to P0v3, starts the Function App, and restores autoscale and
metric alerts. Parking also removes the staging deployment slot because Basic
plans do not support slots; the next full deployment recreates it. Parked
resources still incur charges for private endpoints, Static Web Apps, Key Vault,
storage, and the B1 plan.

### Portal procedure

1. Open **Resource groups**, then `IBP-AUE-{ENV}-RSG-APP01`.
2. Open **Monitor**, **Alerts**, **Alert rules**. Before stopping the app,
   disable metric and scheduled query rules. Also disable the availability test
   while parked. Re-enable the rules and test after resume.
3. Open the Function App `IBP-AUE-{ENV}-FNA-APP01`. On **Overview**, choose
   **Stop** for park. Confirm the state changes before continuing.
4. Open **Deployment slots** for the Function App. If the `staging` slot is
   present, delete it during park; the next full deployment recreates it.
5. Open **Monitor**, **Autoscale**, select the App Service plan, and disable
   autoscale while parked. Re-enable the autoscale setting after resume.
6. Open the App Service plan `IBP-AUE-{ENV}-PLN-APP01`. For park, open
   **Scale up (App Service plan)**, choose **B1**, and apply. If Azure reports
   a capacity error, retry the downgrade until it succeeds rather than leaving
   the plan on its previous SKU.
7. For resume, choose **P0v3** and apply, then start the Function App from
   **Overview**. Confirm it reaches **Running** before continuing.
8. Run the health endpoint and confirm HTTP 200 or an intentional HTTP 503
   before treating resume as complete. Re-enable autoscale, then re-enable the
   alert rules and availability test.

The staging slot is not recreated by resume itself. It is recreated by the next
full deployment, and is not required for normal running.

### Azure CLI procedure

Before running the CLI commands for park, use **Monitor → Alerts → Alert rules**
to disable the environment's metric and scheduled-query rules, and separately
disable its availability test. This silence-first step must happen before
stopping the app. Re-enable those rules and the availability test only after
resume health checks.

Set the subscription and resource-group values before running the commands:

```bash
SUBSCRIPTION_ID=<subscription-id>
RG=IBP-AUE-{ENV}-RSG-APP01
PLAN=IBP-AUE-{ENV}-PLN-APP01
FNA=IBP-AUE-{ENV}-FNA-APP01

# Park: stop the app before removing its staging slot.
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

# Resume: the next full deployment recreates the staging slot removed above.
az appservice plan update --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$PLAN" --sku P0v3
az functionapp start --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA"
az functionapp show --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "$FNA" --query state -o tsv
az monitor autoscale update --subscription "$SUBSCRIPTION_ID" \
  --resource-group "$RG" --name "${PLAN}-autoscale" --enabled true
```

After the resume commands, use the Portal to re-enable each alert rule and the
availability test because each must be checked individually. Run the health
check and confirm the Function App state is `Running`. Record the final plan SKU,
Function App state, autoscale state, alert state, and availability-test state in
the incident.

## Autoscale

The running plan uses an autoscale profile named
`IBP-AUE-{ENV}-PLN-APP01-autoscale`.

| Setting | Value |
|---|---|
| Minimum instances | 1 |
| Maximum instances | 6 |
| Scale out | Storage queue QueueMessageCount above 10, add one instance, five-minute cooldown |
| Scale in | The same metric below 3, remove one instance, ten-minute cooldown |

In Azure Portal, open **Monitor**, **Autoscale**, choose the App Service plan,
and confirm the profile and rules. A B1 plan must not have an active autoscale
profile. If a running P0v3 plan has no profile, restore the profile before
investigating generation latency as an application defect.

The Function App reads secrets from Key Vault at startup over the private
endpoint, using its managed identity. Key Vault names use `--` for the
configuration-section separator. For every runtime secret, write a new version
with `az keyvault secret set`, restart the Function App, check the health
endpoint, and run one controlled bulletin while checking its source diagnostics.
No application-settings rewrite is required.

| Secret or connection | Rotation procedure |
|---|---|
| `OfficialFeeds--QldApiKey` | **Does not exist yet** (verified 2026-09-03) — this is a creation step, not a rotation. Obtain a dedicated QLDTraffic key from TMR, write the first Key Vault version, restart, then verify QLDTraffic is `Ok` in the job source diagnostics. Never treat the published demo key as production capacity. |
| `EmergencyFeeds--ApiKey` | Phase 1 DataQuoll is disabled, console-only, and has no writer or on-air path. Rotate this only after Phase 2 is approved and the Bicep setting/key has been provisioned; then restart and verify the feed diagnostic without enabling broadcast use implicitly. |
| `Here--ApiKey` | Replace the HERE platform key in the vendor console, write the new Key Vault version, restart, check health, and verify one HERE-backed bulletin. |
| `Here--MapApiKey` | **Exists in all three vaults since 2026-09-23 and is, for now, a copy of `Here--ApiKey`** (the server key), so every signed-in operator's browser receives the paid server key. That was an accepted interim step to restore the station maps; replace it with a dedicated browser key restricted to the three console hostnames and enabled for Raster Tile, Maps API for JavaScript, Traffic Vector Tile and Geocoding & Search. Write the new version, restart, then open a station's coverage editor and confirm tiles load and an address search returns results. Without this secret every console map shows **Map unavailable** and address search is disabled; there is no free-map fallback and no Bicep parameter, because the Function App reads it from Key Vault at startup. When `Here--ApiKey` is rotated while this copy stands, rotate `Here--MapApiKey` with it. |
| `AiSettings--AzureAI--ApiKey` | **Exists but is inert — leave it empty.** The app setting is empty in all three environments and the platform treats an empty or placeholder key as absent, authenticating with the Function App managed identity. Writing a real value would move Azure AI authentication off managed identity onto a key that then needs its own rotation. See the managed-identity note below. |
| `AiSettings--ElevenLabs--ApiKey` | Replace the ElevenLabs key, write the new version, restart, check health, and synthesize one controlled bulletin. |
| `Webhook--EndpointUrl` | Replace the Logic App trigger URL, write the new version, restart, then confirm a test card and successful Logic App run. |
| Entra `AadClientSecret` | Create an overlapping replacement in **Microsoft Entra ID → App registrations**, write `AadClientSecret` to Key Vault, and redeploy the SWA AAD configuration (or perform the approved direct SWA setting update). Test fresh sign-in and role/API access before revoking the old secret. |
| Teams connection | Follow the re-consent procedure on the [Teams notifications page](notifications.md), using an ARN-native account. This OAuth credential is held by the connection resource, not Key Vault. |

Azure AI access for the raw model uses the Function App managed identity and
the Cognitive Services User role; there is no Azure OpenAI API key to rotate.
If that access fails, check the role assignment rather than creating a secret.

Test and production Key Vaults deny public network access (verified 2026-09-03). Run the write from a jump host, a private-link-connected client, or a pipeline job.
A workstation outside the network is refused by the vault firewall. The Key Vault data-plane operator holds the secret access and the subscription administrator holds the network control; see the [support model](support-model.md).
A subscription administrator may temporarily enable public access only
for a controlled write, then must disable it immediately. Retain the previous
secret version until the validation succeeds; revoke it at the vendor or Entra
side only after the new version is proven. Never paste values into tickets,
logs, chat, or this runbook.

## Audit evidence retrieval

The console **Activity** page records configuration changes, identities, and
UTC timestamps. For a bulletin, open the console record and capture its
approval identity, status history, scheduled slot, and audio.

For bulk evidence, open the environment storage account in the resource group,
then **Data storage**, **Containers**, and the `broadcasts` container.

Lifecycle rules DO delete bulletin audio on test and production (verified live
2026-08-12): published audio is deleted after 90 days, never-aired audio after
30 days, and audit exports after 1095 days, with a tiering rule moving audio to
cool storage after 30 days. Development has no lifecycle policy at all, so its
recordings remain until someone removes them.
Deleted blobs can be recovered within the soft-delete window, which is 90 days
in test and production and 30 days in development, and test and production also
support point-in-time restore for the last 30 days. Log Analytics retention is
30 days in every environment and is managed in the workspace **Usage and
estimated costs**, **Data retention** blade; export rows you need to keep for
longer than that.

### Recovering a deleted recording (verified procedure)

Exercised end to end for the first time on 2026-08-12, against development. Until
then this was a documented capability that nobody had ever performed, in any
environment. The four steps below are the ones that were actually run, in order,
and each one's observable result:

1. Confirm the recording is really gone from a normal listing — `az storage blob
   list` returns it no longer.
2. Confirm it is recoverable rather than purged, by listing again with deleted
   blobs included (`--include d`): the entry reappears with `deleted: True`. If it
   does not appear here, the soft-delete window has passed and this procedure will
   not help.
3. Restore it with `az storage blob undelete`, naming the same container and blob.
4. Verify: list the container again — the blob is present without `--include d` —
   then download it and check the contents, not just the file name.
On the drill, a blob deleted at step 1 was fully restored with identical contents
at step 4. Use `--auth-mode login` so the operation runs as your own identity and
appears in the audit trail; do not use an account key. Test and production deny
public network access (verified 2026-09-03), so run this from a jump host, a private-link-connected client, or a pipeline job, or during a controlled access window.
A workstation outside the network is refused. The subscription administrator holds the network control and the Key Vault data-plane operator holds the data access; see the [support model](support-model.md). This is the same constraint that applies to Key Vault writes.

Use **Monitor**, **Logs** to export the relevant Log Analytics rows. Restrict
the export to the incident's UTC window and environment. Grant legal or
support access through an ARN-managed Entra group rather than sharing a secret
or a storage account key.

![The activity log](screenshot:24-activity.desktop.png)

*Activity (`/activity`): the console audit trail of who changed which
configuration and when.*

![Data transfer](screenshot:25-config-transfer.desktop.png)

*Data transfer (`/config-transfer`): exports and imports a station's complete
configuration for backup or promotion between environments.*

## Operational conditions to check

| Condition | Action |
|---|---|
| An alert rule has no useful receiver | Verify the existing Teams action-group route and its interim email backstop. SMS and phone are a recorded ARN deferral, not outstanding work. |
| Production or test playout is unreachable | The Azure Files drop writes the approved WAV over the Azure Files REST API with the Function App managed identity (`Storage File Data Privileged Contributor`) to `ibpauetststaftp01/informationbulletins`. Confirm delivery is enabled for the environment and station, then inspect Function App delivery logs. No station is armed today, so nothing is delivered until ARN arms one; production is frozen. There is no delivery credential to rotate. |
| The Azure Files write returns `409 SharingViolation` | Zetta's autoloader may still have the same filename open. The writer retries only this error twice, after approximately 300 ms and 900 ms; it then fails the publish so an operator can retry. Do not broaden this retry to unrelated 409s. Zetta normally deletes the file within seconds after ingest, so a file that remains in the share is not an ingest receipt. |
| A bulletin is published but not visible in the tray | Confirm the schedule's station and slot in the console, and inspect whether the watchdog rewrote a covered slot at its cutoff. Capture the job ID and the delivery log before retrying. |
| The console uses a default Azure Static Web Apps hostname | The ARN custom domains are live: `ain.arn.com.au`, `ain-test.arn.com.au` and `ain-dev.arn.com.au`. Use the environment's live ARN domain; enabling `swaEnterpriseEdge=true` remains infrastructure work. |
| A private endpoint or DNS change is suspected | In the resource group, open **Private endpoints** and **Private DNS zones**, then compare connection status with the time of the first alert. |

The health check does not validate official traffic feeds or DataQuoll. Use the
job's source diagnostics and coverage report (and, after a feed is restored, the
validity probe) to distinguish `Skipped` (intentionally disabled) from `Failed`
(enabled but errored). DataQuoll phase 1 is console-only and never airs.

For escalation roles and response times, use the [support model](support-model.md).

## ARN Self-Service Operations Guide

### Purpose and authority

ARN Platform Operations owns routine station configuration, editorial settings,
feed source switches, release evidence, and first-line incident response.
Vendor owners issue vendor credentials; the Entra application owner owns
identity credentials; ARN broadcast engineering owns Zetta and the drop share.
All production changes require an ARN ticket and a second approver. Never put a
secret value in a ticket, chat, command transcript, screenshot, or log. Record
UTC timestamps, the actor, the environment, the reason, and the validation
evidence.

The platform's default is to air something safe. A feed outage must not be
papered over by inventing traffic facts. Official-feed data is corroboration
and enrichment; DataQuoll phase 1 is diagnostic, console-only data and never
reaches the writer or air.

### Before every change

1. Identify the subscription, environment (`DEV`, `TST`, or `PRD`), market ID,
   station, and scheduled slot. Confirm whether the change is global, market,
   or station scoped.
2. Check the on-air queue and confirm no in-flight slot will be invalidated.
   Do not arm a second environment while another environment owns the station.
3. Export the affected configuration with `GET /api/config/export` and retain
   the export as controlled evidence. Review it; do not blindly import it.
4. Capture the current version, effective setting, source status, and rollback
   value. Choose the smallest reversible change.
5. Apply and validate in DEV, then TST, then PRD. No station is armed for
   playout today: both TST stations generate with playout disabled, PRD's
   schedules are disabled and it is the frozen fallback, and DEV has
   `Zetta__Enabled=false` and can never deliver.

### Operating guardrails

- Pipeline 108 is the release authority for the `master` branch in Azure DevOps. An automatic push builds and packages
  only; queue a deliberate manual `master` run with
  `targetEnvironment=dev`, `test`, or `prod` for deployment. Queuing needs Queue builds access to pipeline 108 from an ARN-native account.
  `deployInfrastructure`
  defaults to `true`; setting it to `false` skips only Bicep/autoscale while
  Functions, the web console, the documentation site, and smoke still run. The `test` stage waits for approval on the `test` environment and the `production` stage waits for approval on the `production` environment; approving needs Approver permission on that environment. The pipeline owner and deployment approver are listed in the [support model](support-model.md).
- Only one environment may have a station's **Send to playout** switch on. TST
  and PRD share `ibpauetststaftp01/informationbulletins`; a duplicate filename
  is an operational collision, not environment isolation.
- Read live state with `GET /api/schedules`; never rely on an old screenshot or
  a copied note. Calendar assignments take precedence over a plain cron.
- Use source diagnostics and `/api/coverage` for official feeds. `/api/health`
  does not prove official-feed or DataQuoll availability.
- Treat a Key Vault version change as incomplete until restart, health,
  controlled bulletin, and source/audio checks pass. Retain the prior version
  until validation is complete.
- The additive job read models (`Jobs__Persistence__*`, `Jobs__Ledger__*`,
  `Jobs__WorkIndex__*`, `Schedules__DueIndex__*`) are switched off in every
  environment and must stay off until that environment's backfill has run and
  been compared. An index holding rows only for recent jobs answers a history
  query with recent jobs alone. On 3 September 2026 that turned a 776-bulletin
  test history into 15 visible bulletins until the switches were restored;
  nothing was deleted, but the console looked emptied.

### Third-party secret and API key management

The application reads Key Vault at startup over the private endpoint, using its
managed identity. It does not use application-setting references for secrets.
Do not re-save application settings after a secret write.

Set the target explicitly. Test and production deny public network access (verified 2026-09-03). For TST and PRD, run from a jump host, a private-link-connected client, or a pipeline job. A workstation outside the network is refused by the vault firewall.

```bash
# Choose exactly one environment; all three environments use this subscription.
SUBSCRIPTION_ID=5e5f8695-2207-4572-8e80-1065a1b0c135
ENVIRONMENT=dev
case "$ENVIRONMENT" in
  dev) RG=IBP-AUE-DEV-RSG-APP01; KV=IBP-AUE-DEV-KYV-APP01; FNA=IBP-AUE-DEV-FNA-APP01 ;;
  test) RG=IBP-AUE-TST-RSG-APP01; KV=IBP-AUE-TST-KYV-APP01; FNA=IBP-AUE-TST-FNA-APP01 ;;
  prod) RG=IBP-AUE-PRD-RSG-APP01; KV=IBP-AUE-PRD-KYV-APP01; FNA=IBP-AUE-PRD-FNA-APP01 ;;
  *) echo "Set ENVIRONMENT to dev, test, or prod" >&2; exit 1 ;;
esac

az account set --subscription "$SUBSCRIPTION_ID"
az keyvault secret set --vault-name "$KV" --name "<secret-name>" --value "<new-value>"
az functionapp restart --resource-group "$RG" --name "$FNA"
curl -sS "https://ibp-aue-<env>-fna-app01.azurewebsites.net/api/health"
```

Use an environment variable or a secure prompt in place of
`<new-value>`; never put a real value in shell history. For each item below:
issue or obtain the replacement first, write a new version, restart, check
health, run one controlled bulletin, inspect audio and source diagnostics,
retain the old version until success, then revoke the old vendor or Entra
credential. Record the new version ID, not its value.

1. **QLDTraffic** — obtain a dedicated TMR key and write
   `OfficialFeeds--QldApiKey`. The published QLDTraffic demo key is globally
   shared and rate-limited; it is not an acceptable production substitute.
   Confirm QLDTraffic reports `Ok` in the job diagnostics.
2. **DataQuoll emergency services** — `EmergencyFeeds--ApiKey` is reserved
   for an approved Phase 2 deployment. Phase 1 is disabled, console-only,
   absent from current deployment Bicep, and has no on-air path. Do not create
   or enable this secret as a side effect of Phase 1 operations. If Phase 2
   has been approved and IaC has provisioned the key, rotate it and confirm
   only the intended diagnostic path.
3. **HERE platform** — replace the vendor credential and write
   `Here--ApiKey`. Validate traffic, routing, weather, or fuel only for the
   capability changed. Do not rotate a valid key to treat a provider outage.
4. **HERE map** — replace the map credential and write `Here--MapApiKey`.
   Open the station map and verify tiles or geocoding after restart.
5. **ElevenLabs** — replace the provider key and write
   `AiSettings--ElevenLabs--ApiKey`. Synthesize one controlled bulletin and
   compare the resulting audio to its script.
6. **Power Automate / Teams webhook** — write the replacement trigger URL as
   `Webhook--EndpointUrl`, restart, submit one test notification, and confirm
   both the Teams card and Logic App run.
7. **Microsoft Entra** — create an overlapping app-registration secret, write
   `AadClientSecret`, then redeploy the SWA AAD configuration with
   `configureSwaAadKeyVaultSettings=true` (or use the approved direct SWA
   setting hotfix). Test a fresh logout/login, role resolution, and protected
   API access before revoking the old secret. This is the one rotation that
   also requires SWA configuration handling.
8. **Azure OpenAI** — the Azure OpenAI script generation model authenticates through
   managed identity. There is no Azure OpenAI API key to rotate. If calls fail,
   inspect the identity's Cognitive Services User role, private DNS, and
   provider health.
9. **Keyless official feeds** — Victoria/DataVic currently has no API key.
   Do not invent a secret or add an app setting; investigate the feed's
   published access policy instead.

If the private path is unavailable, stop and ask the subscription administrator for access (see the [support model](support-model.md)). The Key Vault data-plane operator holds the secret access and the subscription administrator holds the network control. A temporary public-network window is an exception: enable
access only for the controlled write, verify the write, disable it
immediately, and document the window.

### Onboarding new markets and stations

1. In **Admin → Stations**, create the market/station with its stable market
   ID, display name, timezone, and ARN owner. Assign the operator group with
   the least privilege required.
2. Configure the timezone, cron clocks, and calendar assignments. Calendar
   precedence is deliberate; use `GET /api/schedules` to inspect the computed
   next run and confirm the schedule is disabled until the configuration is
   reviewed.
3. In **Voices**, preview the approved voice, save or select the versioned
   voice profile, and assign it to the station. Set rotation to `single` when
   the station has one approved voice; do not infer a provider voice ID from
   its display name.
4. Select the station's delivery style and configure its pronunciation
   dictionary. Preview aliases with the platform-wide dictionary layer
   included, then verify the exact dictionary version is assigned.
5. In the coverage map, configure traffic coverage and optional circle-only
   weather and fuel overrides. Confirm the map, not an address-search guess,
   represents the licensed area.
6. Set content toggles and required content (traffic, weather, fuel), and
   choose source switches. A source switch is inherited Global → Market →
   Station; disable only the failing source at the narrowest safe scope.
7. Add and preview bookends, station standby statements, and a real static fallback
   audio asset. Confirm the station's target duration and that fallback text
   is appropriate for the station.
8. Configure the Zetta market code and filename convention with ARN broadcast
   engineering. The file is written to the share root, and Zetta deletes it
   after ingest. Never invent a code from the market ID.
9. Keep the **Send to playout** setting off while validating configuration. Promote
   configuration through DEV and TST. Before arming, confirm no other
   environment owns the station; then enable **Send to playout** in one
   environment only. DEV can never be the delivery environment.
10. Trigger through the broadcast scheduler or the approved CLI path, review, approve, and
    publish one controlled bulletin. Confirm the filename, share write,
    Zetta deletion, job status, and `GET /api/markets/{marketId}/playout-tray`.
    Do not treat a successful API response alone as ingest proof.

For a promotion, use **Config transfer**: export, review, run the import with
`dryRun=true`, inspect the plan, then apply the UPSERT-only plan with a second
approver. A transfer does not grant playout ownership; re-check the
one-environment arming rule after import.

### Feed outage and WAF block playbooks

#### Traffic SA: 403 WAF block

**Detect:** Job source diagnostics show Traffic SA `Failed` with HTTP 403 or
CloudFront `Request blocked`; `/api/health` may still be healthy. Confirm the
pattern from the Function App egress before changing code or credentials.

**Contain:** Go to **Bulletin Rules → Limits**, select the narrowest affected
scope (Station, then Market, then Global only if necessary), and disable the
Traffic SA source. Save and confirm the source changes to `Skipped`, allowing
HERE-only coverage. Do not claim that `Skipped` means the authority recovered.

**Escalate:** Ask SA DIT to allowlist the Azure egress or provide an approved
static NAT path. Preserve timestamps, response status, and affected market
IDs. Do not repeatedly restart the Function App or rotate a valid SA-free
configuration.

**Restore:** After allowlisting, re-enable the source at the same scope, run
`POST /api/metrics/validity-probe`, and generate one controlled bulletin.
Confirm the source is `Ok`, the bulletin facts remain grounded, and record the
job evidence.

#### QLDTraffic: 429 rate limit

**Detect:** Source diagnostics or App Insights show QLDTraffic HTTP 429. The
official-feed client has bounded short retries, but operators must not create
parallel manual retries or a retry storm.

**Contain:** Stop repeated triggers. At **Bulletin Rules → Limits**, disable
QLDTraffic at the narrowest affected scope if the next slot would otherwise
wait on it; HERE remains the safe traffic source. Request and provision a
dedicated TMR key, then rotate `OfficialFeeds--QldApiKey` using the procedure
above. Never restore the published demo key as a permanent fix.

**Restore:** Re-enable the source after the dedicated key is active, run the
validity probe, and generate one controlled Cairns bulletin. Check that the
job lists QLDTraffic evidence and that the source is `Ok`; retain the 429
trace and new key version in the ticket.

#### HERE platform incident or intermittent failure

**Detect:** Check the HERE status page, dependency panel, request traces,
timeouts, and affected capabilities. Distinguish a platform outage from a
private DNS, route, or managed-identity failure.

**Contain:** Do not rotate a valid HERE key or restart-loop the Function App.
Allow the bounded provider resilience policy and the platform's deterministic
fallback to cover slots. Escalate a sustained outage to HERE and ARN with
request timestamps and correlation IDs.

**Restore:** Confirm provider recovery, run the validity probe, generate one
controlled bulletin, and compare its source diagnostics and audio to the
captured incident. Only then close the incident.

#### DataQuoll: phase 1 diagnostic limits

DataQuoll is disabled by default and phase 1 is console-only. It has no writer
member, no on-air path, and no road-impact field that can safely be fused into
traffic. A failed or absent DataQuoll fetch must never block a bulletin.

Use the Adjust panel's emergency services list and the source diagnostics to
inspect it. Do not promote emergency records into script guidance, create a road
closure from coordinates, or add Bicep and a secret without an approved Phase 2
decision. Escalate vendor/API errors with the source ID `dataquoll`; restore
diagnostic collection only after the feed is confirmed and record that no
audio behaviour changed.

### Bulletin tuning and editorial quality controls

Use **Bulletin Rules → Writer prompt** for station wording. Presenter, voice,
length, running-order, and station-lesson cards are versioned prompt
instructions with preview and history. A prompt card may change how the writer
phrases grounded facts, but it must not invent facts or override safety gates.
Workflow prompt changes are release-owned: ask the pipeline owner to apply
the on-disk prompt change through pipeline 108 from the `master` branch and restart the affected app (see the [support model](support-model.md)).

Use **Bulletin Rules → Limits** for numeric and mechanical controls: corridor
caps, continuity caps, drive-time thresholds, duration/pace limits, silence
padding, safe trim, and traffic source switches. These settings are resolved
Station → Market → Global and are applied to the job snapshot. They are not a
second writer prompt. Verify effective values through the editorial settings
API before changing them.

Guidance, banned terms, station standby statements, priority roads, pronunciation, and
delivery style each have their own governed surface. Change one concern at a
time, preview it, and retain the previous version. An existing job retains its
captured facts and settings; regenerate deliberately when a new setting should
apply. Never edit a generated script to conceal a source or limit failure.

Station Lessons are candidate governance, not an automatic instruction
channel. A lesson candidate is derived only from a published/delivered
operator edit, requires independent reinforcement, and can be dismissed or
superseded. Rejects, skipped publishes, and unpublished edits do not teach
the station. Review candidate wording, scope, evidence, and confidence before
activation.

### Diagnosing a failed generation from the job record (2026-09-03)

Check the job record and progress events for these now-fixed symptoms:

- **Lost optional desk items:** every empty incidents-desk reply was treated as fatal, even when the writer dropped only works that held nobody up. Must-brief losses remain fatal; droppable works continue.
- **Style gate:** the legacy at-most-two numeric-measurement check rejected whole scripts after the workflow gates had passed and missed hyphenated or “kay” forms. The cap and its legacy check are removed.
- `content-limited`: first distinguish natural speech from file length. A file padded to the target still fails when trailing silence exceeds the configured budget. Ceiling-only sentence trimming could turn a compressible overrun into an unfillable underrun, and the retry could then pair the pre-trim script with the trimmed take's duration. The fitter now rejects optional subsets below the natural-read floor and uses a full-script speed re-render instead; measured-duration revision receives the exact spoken take and grows small gaps with supplied facts in existing sentences.

For fast test iteration, list the tests, run classes in parallel with a 60 second cap, then narrow to individual tests. If a test host hangs, `dotnet-stack report` showed idle threads waiting on an async semaphore, identifying the permit deadlock.

### Quarterly maintenance and renewal checklist

Record an owner and next due date for each item:

- Inventory every Key Vault secret version and vendor subscription. Set
  reminders at 30, 14, and 7 days before Entra client-secret expiry; maintain
  overlap before revocation.
- Confirm Key Vault private endpoint approval, private DNS resolution, firewall
  posture, soft-delete/purge protection, and Function App managed-identity
  `Key Vault Secrets User` access. Confirm the application loads secrets from Key
  Vault at startup and that no plaintext secrets are in app settings.
- Review Entra client secrets/certificates, redirect URIs, app roles, group
  assignments, and the fresh-login/API smoke test. Confirm the SWA AAD
  configuration still uses the intended secret.
- Re-consent the Teams/Power Automate connection with an ARN-native account;
  send a test card and verify a Logic App run.
- Confirm all ARN console custom domains, DNS, certificate/TLS status, and
  renewal ownership. Test each environment hostname.
- Confirm Function App managed-identity roles for storage, AI, and the
  AzureFilesDrop target. Verify `ibpauetststaftp01/informationbulletins`,
  `Storage File Data Privileged Contributor`, Zetta deletion on ingest, and
  the current one-environment station arming.
- Review pipeline 108 service connections, variable groups, promotion
  approvals on the `test` and `production` environments, and the last successful rollback run. Confirm deployment uses a manual `master` run with
  `targetEnvironment` (`dev`, `test`, or `prod`) and that `deployInfrastructure`
  is understood before queueing; the `test` stage waits for approval on the `test` environment and the `production` stage waits for approval on the `production` environment.
- Review feed keys, official source diagnostics, `/api/coverage` trends,
  unknown-label worklist, HERE status, and DataQuoll's disabled phase-1
  boundary. Run one validity probe without enabling an unapproved source.
- Review backup/lifecycle evidence, DR assumptions, and undefined RPO/RTO
  decisions with ARN. Export this checklist and the validation evidence into
  the controlled operations record.

### Change-log worksheet

Copy this worksheet into the approved ticket; never paste secret values:

| Field | Entry |
|---|---|
| UTC started / completed | |
| Environment and subscription | |
| Market / station / slot | |
| Actor and second approver | |
| Change and reason | |
| Previous effective setting/version | |
| New setting/version or pipeline run | |
| Pre-change export location | |
| Validation job, source diagnostics, and audio evidence | |
| Rollback command/version | |
| UTC next review date | |

### Stop and escalate

Stop the change and contact the pipeline owner, platform support, or
broadcast engineering (see the [support model](support-model.md)) when a second approver is unavailable; the private path
is not proven; a change would arm two environments; a secret value may have
been exposed; the scheduled slot is inside its curation or fallback deadline;
the source response is ambiguous; a Zetta filename/code is missing; a
production rollback cannot be identified; or a requested DataQuoll change
would cross the phase-1 console-only boundary. Escalate rather than bypassing
the guardrail with a direct production edit.
