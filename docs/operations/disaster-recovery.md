---
title: "Disaster Recovery"
description: "Current recovery position, resilience limits, data protection and the actions required to establish regional recovery for the ARN Information Bulletin Platform."
tags: [operations, dr, disaster-recovery, high-availability]
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2027-02-01
volatility: "low"
source_of_truth: "Azure resource inventory, storage protection settings and environment recovery controls"
verified_by:
  - "Azure resource and storage protection review"
aliases:
  - disaster recovery
  - DR
common_queries:
  - "What happens if Australia East goes down?"
  - "How does ARN restore the platform?"
see_also:
  - "docs/operations/as-built.md"
  - "docs/operations/runbook.md"
  - "docs/operations/known-limitations.md"
---

# Disaster Recovery

## Current recovery position

The core application and data plane run in one Azure region, Australia East. No
secondary Function App, application storage account, active-passive failover or
automated cross-region redundancy is deployed.

The console Static Web Apps resources use a separate Azure location, but there
is no documented backend failover route. A separate console location therefore
does not provide platform or data-plane disaster recovery, and console
availability during a regional outage is not guaranteed.

Application storage is same-region only:

| Environment or service | Current redundancy | Recovery implication |
|---|---|---|
| Development application storage | Standard_LRS | No zone or regional protection |
| Test and production application storage | Standard_ZRS | Zone protection within Australia East, no regional protection |
| Test and production playout delivery drop, `ibpauetststaftp01`, share `informationbulletins` | Standard_LRS | Shared Azure Files drop for both environments, delivered over the Azure Files REST transport, verified from the live app settings of both Function Apps on 2026-09-03. No separate production drop account exists and no automatic regional failover is configured |

## Impact of a regional outage

If Australia East is unavailable:

- Function App processing and the application Queue, Table and Blob data plane
  are unavailable.
- Test and production ZRS storage remains limited to the affected region.
- Development storage and the shared delivery drop have LRS protection only.
- Test and production share one Azure Files delivery drop in `ibpauetststaftp01`, share `informationbulletins`, verified 2026-09-03. Loss of that account stops delivery in both environments until manually restored or reconnected.
- No automated regional failover exists.

## Current mitigations and required actions

1. Azure Monitor alerts and availability tests cover Function App errors, Key
   Vault access denials, missing executions, throttling, poison queues and
   synthesis failures. Teams alert routing is live in all three environments. Verified 2026-09-03, each live action group carries one email receiver belonging to the delivery representative plus the Teams webhook. The committed deployment parameters set `alertEmailReceivers` to empty, so the next deployment of any environment removes that email receiver and leaves Teams as the only path. SMS and phone
   notifications remain deferred under ARN's recorded election. Review receivers
   and test routing as part of the normal monitoring cycle.
2. A manual rebuild can recreate the platform in a secondary region, but this
   is not failover. The recovery sequence is to deploy the resource pattern,
   restore available Blob data and audit exports, configure secrets and
   authentication, recreate private endpoints and DNS, and reconnect playout
   delivery.
3. There is no deployed Table Storage point-in-time restore, backup-vault
   restore or equivalent backup for application tables. A rebuild can restore
   infrastructure, but it cannot restore schedules, synthesis history or other
   Table Storage state without a separate export.
4. The platform exports bulletin evidence daily to the `audit-exports` Blob
   container. These exports support investigation and evidence retention. They
   are not a backup of schedules, synthesis history or the other Table Storage
   data.

## Recovery plan for ARN

If regional recovery becomes a required service capability, ARN platform
operations should:

1. Select a secondary Azure region and deploy a separate instance there.
2. Choose and implement cross-region protection for Tables, Queues and Blobs,
   with a documented restore procedure.
3. Provide a console and API failover route through Traffic Manager or Front
   Door.
4. Implement active-passive operation with automated health checks and a tested
   operator runbook.
5. Define recovery point and recovery time objectives against the actual data
   and playout paths, including the shared delivery-drop account.
6. Exercise the recovery procedure at a scheduled interval and record the
   observed result.

## Data protection

No Cosmos DB account is deployed. Persistent application state is in Azure Table
Storage. Table Storage has no configured point-in-time restore or retention
policy for the application tables. No Azure Backup, Recovery Services or Data
Protection resource is deployed for this state.

| Data | Current protection | Recovery point position | Recovery time position |
|---|---|---|---|
| Bulletin records | Daily `audit-exports` snapshots of selected bulletin records | Evidence export only, no table restore | Manual rebuild and data recreation |
| Station schedules | Table Storage with no configured backup or point-in-time restore | No supported table restore | Manual recreation from an independent record |
| Synthesis history | Same-region storage redundancy only, LRS in development and ZRS in test and production | No supported regional restore | Manual rebuild and data recreation |
| Audio files | Blob soft delete in all environments: 30 days in development and 90 days in test and production; 30-day point-in-time Blob restore in test and production | Deleted blobs can be recovered within 30 days in development or 90 days in test and production; point-in-time restore is available within 30 days in test and production | Manual rebuild of application access, then Blob recovery |
| Test and production playout drop | Azure Files share `informationbulletins` in `ibpauetststaftp01`, Standard_LRS and seven-day share-delete retention, delivered over the Azure Files REST transport, verified from the live app settings of both Function Apps on 2026-09-03. No separate production drop account exists | Deleted share content only within the configured retention window; no regional restore or automatic failover is configured and RPO is undefined | Manual share recreation and playout reconnection; ARN broadcast engineering must define the required RPO/RTO and exercise recovery |

The deployed controls do not establish an RPO or RTO for the application data or the shared playout drop. These targets are undefined. The measured position below is therefore the de facto recovery posture, and ARN must decide whether it meets the required broadcast service before setting formal targets.

## Measured recovery position

What the platform can actually achieve today, from the deployed configuration:

- **Blob data (audio and evidence).** Point-in-time restore to any point within the last 30 days in test and production, and soft-delete recovery for 90 days in test and production and 30 days in development. Blob restore is enabled with a 30-day window in the test and production parameter files, and the storage template enables versioning and change feed to support it.
- **Storage redundancy.** Zone-redundant storage in test and production, locally redundant in development. This is same-region protection only; there is no regional protection.
- **Retention.** Published audio is retained 90 days, unpublished audio 30 days, and audit-evidence text three years (1,095 days); audio tiers to cool storage after 30 days of no access. These figures are set in the test and production parameter files.
- **Bulletin records.** A daily snapshot at 16:00 UTC of bulletin records to the audit-exports container. These exports support investigation and evidence retention; they are not a table restore.
- **Return from parked state.** One documented resume script restores the plan, starts the application, waits up to two minutes for the Running state, verifies the health endpoint and re-enables autoscale, alerts and the availability test. A production park was measured at 49 seconds, so a park-resume cycle is a minutes-scale operation, not a rebuild.
- **Full rebuild.** Infrastructure is recreated by the same template deployment the delivery pipeline runs; the pipeline's build and validation stages complete in under ten minutes (measured wall time of roughly nine minutes). The manual steps that follow, such as re-seeding secrets, recreating private endpoints and DNS, and reconnecting playout delivery, take as long as access and evidence permit. No end-to-end rebuild has been timed, so treat the whole procedure as unmeasured until ARN runs a recovery exercise.

## Restore procedure

To bring an environment back after loss of its compute or data plane, follow this order. The commands and portal paths are in the [command reference](quick-reference.md) and the [operations runbook](runbook.md).

1. **Restore access and confirm state.** A subscription administrator confirms the subscription is active and identifies what was lost: infrastructure, application state, or both. An environment that lost nothing needs only the resume step.
2. **Redeploy the environment.** Deploy the environment's template and application release through the delivery pipeline, or through the documented deployment script for development and test. This recreates the Function App, storage, Key Vault, plan and networking from the same template that built them.
3. **Restore Blob data.** Use the storage account's point-in-time restore to roll containers back to a point within the 30-day window in test and production, or recover individual deleted blobs within the soft-delete window (90 days in test and production, 30 days in development).
4. **Recreate application tables.** Table Storage has no point-in-time restore. Recreate station schedules, configuration and other table state from independent records; a station's complete configuration can be exported from one environment and imported into another through the console's data transfer feature. The daily audit exports are evidence and cannot be loaded back as state.
5. **Configure secrets and authentication.** Restore the runtime secrets in Key Vault, then restart the Function App so it reloads them. The console sign-in secret is a deployment-time input: update it and redeploy the Static Web App settings.
6. **Recreate private endpoints and DNS.** Recreate the private endpoints for storage, Key Vault and AI services together with their private DNS zones, and confirm the storage and Key Vault network access remains denied by default.
7. **Reconnect playout delivery.** Confirm the delivery drop share is reachable, the managed identity holds its drop roles, and the file private endpoint and DNS are in place, then complete one controlled delivery test.
8. **Verify and hand back.** Run the health endpoint, generate and approve one bulletin through to delivery, confirm alerts and dashboards are live, and record the observed result in the incident record.
