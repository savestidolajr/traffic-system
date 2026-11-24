---
title: "Accounts and Access"
description: "Role-based ownership for the Azure subscription, identity services, deployment controls, vendor accounts and operational endpoints used by the ARN Information Bulletin Platform."
tags:
  - "operations"
  - "access"
  - "identity"
  - "accounts"
aliases:
  - "access and accounts"
  - "account inventory"
  - "access ownership"
purpose: "reference"
status: "current"
last_verified: 2026-09-03
review_after: 2026-10-03
volatility: "medium"
source_of_truth: "Azure subscription, Microsoft Entra ID, Azure DevOps and vendor account ownership records"
verified_by:
  - "Environment access review"
  - "2026-09-03: runtime secret inventory corrected against the development vault; recorded that the test and production vaults refuse workstation callers so rotation must run from inside the virtual network"
common_queries:
  - "Which ARN role owns each platform asset?"
  - "What access must ARN assign?"
  - "What breaks when an owner is missing?"
  - "What are the console URLs?"
see_also:
  - "docs/operations/as-built.md"
  - "docs/operations/dependencies.md"
  - "docs/operations/disaster-recovery.md"
  - "docs/operations/support-model.md"
---

# Accounts and Access

The role, not an individual, is the durable owner. ARN reviews each role
at least quarterly and after any staff or supplier change, and removes
identities that no longer need access.

## Azure ownership

| Asset and purpose | Accountable ARN role | What ARN must do | If nobody owns it |
|---|---|---|---|
| Azure subscription and the three environment resource groups, which contain the platform | Subscription administrator | Assign at least two ARN administrators with just-in-time Owner access, and retain a separate Contributor path for routine operations | No ARN principal can change resources, grant permissions or recover an environment |
| Pipeline service principals, which deploy infrastructure and application changes | Pipeline service principal owner | Assign an owner for each environment identity, record credential and certificate expiry, and review role assignments at least quarterly | A credential can expire without notice and deployments stop |
| Entra application registrations, which provide console sign-in and API roles | Entra application owner | Assign an ARN owner for each environment registration, review redirect URLs and app roles, and renew secrets before expiry. The Entra SWA `AadClientSecret` is resolved during Bicep deployment; it is not a runtime Function App secret | Sign-in configuration cannot be changed and operators may lose console access |
| `IBP-AUE-DEV-KYV-APP01`, `IBP-AUE-TST-KYV-APP01` and `IBP-AUE-PRD-KYV-APP01`, which hold runtime secrets | Key Vault data-plane operator | Assign ARN data-plane operators, keep public access disabled in test and production, and rotate secrets through the [operations runbook](runbook.md) | Secret rotation and incident recovery have no executor |
| Function App and delivery managed identities, which read secrets and Azure data | Subscription administrator | Keep the required Storage, Key Vault and Azure AI role assignments in place and review them after any environment change | Bulletin generation, audio synthesis or delivery loses access to its data |
| Azure Monitor workbooks, alerts and action groups | Subscription administrator | Keep Teams alert routing operating in all three environments, and review receivers as part of the normal monitoring cycle. Verified 2026-09-03, each live action group carries one email receiver belonging to the delivery representative plus the Teams webhook. The committed deployment parameters set `alertEmailReceivers` to empty, so the next deployment of any environment removes that email receiver and leaves Teams as the only path | Service failures can continue without an operational notification |

Runtime Function Apps load their secrets at startup over the private endpoint. The authoritative runtime secret inventory is the secret table in the deployed environment inventory, verified 2026-09-03 against the development vault. Do not rotate the inert or placeholder entries as routine work.

Public network access is disabled on the test and production vaults, which is the intended posture. The practical consequence is that neither vault can be listed or edited from a workstation: an owner calling `az keyvault secret list` from outside the network is refused by the vault firewall. Only the Development vault answers a direct call. Rotation in test or production must run from inside the VNet, so confirm that access path — jump host, private-link-connected client, or a pipeline job — before the first rotation is needed.

The subscription is `5e5f8695-2207-4572-8e80-1065a1b0c135`. Resource and Key Vault
names are listed in [the deployed environment inventory](as-built.md).

## Azure DevOps ownership

| Asset and purpose | Accountable ARN role | What ARN must do | If nobody owns it |
|---|---|---|---|
| Azure DevOps organisation, Information Bulletin project and source repository | DevOps project administrator | Assign at least two ARN project administrators, retain ARN access to the default branch and review membership quarterly | Access removal, branch policy changes and recovery requests have no ARN approver |
| Build and deployment definition plus its Azure service connections | DevOps project administrator | Assign an ARN owner for each service connection, rotate credentials through the owning identity and review permissions | A service connection can fail during a release or retain excessive access |
| Test and production deployment environments | Deployment approver | Name at least two ARN approvers for each environment and keep approval rules current | A prepared release waits indefinitely for an approval |
| Deployment automation credentials | Pipeline service principal owner | Store credentials in the approved secret store, set an expiry owner and remove tokens no longer needed under ARN control | Automated deployment stops when a token expires or the external identity is removed |

## Vendor and service accounts

| Asset and purpose | Accountable ARN role | What ARN must do | If nobody owns it |
|---|---|---|---|
| HERE account and production traffic data entitlement | Vendor account owner | Hold the vendor account in ARN's name, retain an ARN administrator, monitor quota and renew the commercial entitlement | Traffic evidence becomes unavailable or quota changes cannot be actioned |
| ElevenLabs workspace, account subscription and station voices | Vendor account owner | Assign at least two ARN workspace administrators, retain voice access and monitor subscription and character quota | Audio synthesis or a station voice can become unavailable without an ARN recovery path |
| Azure OpenAI and Azure AI Services resources | Subscription administrator | Retain the Cognitive Services User assignment for the Function App identity and monitor service quota | Script generation or the automated audio check cannot complete |
| DataQuoll emergency-services feed account and licence | Vendor account owner with ARN technology lead | Hold the subscription in ARN's name, retain the placeholder in Key Vault until ARN obtains a DataQuoll key, and verify state-agency commercial terms before enabling use | Emergency data cannot be retrieved or may be used outside its licence |
| QLDTraffic authority feed and shared demonstration key | Vendor account owner with ARN technology lead | Monitor the shared demonstration key's limits; record throttling or feed failure and fall back to HERE | Queensland authority requests can be rate-limited by the shared demonstration key |
| DataVic open endpoint | ARN platform operations | Retain the keyless endpoint configuration and monitor its availability and terms | Victorian official traffic coverage falls back to HERE |
| Microsoft Teams notification channel and its Power Automate connection | Microsoft 365 service owner | Assign an ARN owner for the channel and connection, renew authorisation with an ARN identity and test a card in each environment | Operators miss review and deadline notifications |
| Zetta playout target, delivery share and playout-side identity | ARN broadcast engineering | Own the Azure Files drop target and share. The Function App writes the approved WAV with its managed identity; confirm that Zetta ingests it and the file disappears. The platform has no on-air receipt, so verify airing through the station process. Check live `/api/schedules` for active delivery; verified 2026-09-03, test has both stations enabled with playout disabled on both so it generates bulletins and delivers none, production has both schedules disabled so it generates and airs nothing, and Dev never delivers | A delivery, ingest or station-process fault is not actioned |

## Console endpoint ownership

| Endpoint or control | Accountable ARN role | What ARN must do | If nobody owns it |
|---|---|---|---|
| Development console, `https://ain-dev.arn.com.au` | Entra application owner | Keep the development application registration and Operator/Admin role assignments current; use the live ARN domain for operators | Development sign-in and role testing cannot be maintained |
| Test console, `https://ain-test.arn.com.au` | Entra application owner | Keep the test application registration, redirect URLs and role assignments current; use the live ARN domain for operators | Pilot operators cannot sign in or test approval flows |
| Production console, `https://ain.arn.com.au` | Entra application owner | Keep the production application registration, redirect URLs and role assignments current; use the live ARN domain for operators | Production operators cannot sign in or publish approved bulletins |
| ARN DNS records and domain ownership | ARN DNS administrator | Maintain the live records and future DNS changes; SWA binding and managed TLS are already validated | Future DNS changes have no executor |

## Ownership checks

The subscription administrator should run this review at least quarterly and
after every staff or supplier change:

1. Confirm that two ARN identities hold each role in this page.
2. Confirm that Key Vault data-plane access works in all three environments
   without exposing secret values.
3. Confirm that the Entra application owner can assign Operator and Admin roles.
4. Confirm that the deployment approver can approve a test deployment and that
   production approval remains restricted.
5. Send a test Teams notification and verify one alert receiver.
6. Record the owner and renewal date for HERE, ElevenLabs and Zetta accounts.

The [external dependency register](dependencies.md) explains the operational
effect of each service being unavailable. The [recovery page](disaster-recovery.md)
records the limits of the current resilience position.

## Break-glass: restoring access when the usual person is unavailable

Break-glass access requires at least two ARN-controlled eligible
administrators and an ARN-owned pipeline identity. Review these controls
quarterly and after personnel changes.

### What a second person needs, in order

1. **Sign-in to the consoles.** An Operator or Admin app role on each
   environment's application registration. Without a role the console
   authenticates the person and then refuses every page. Expect a few minutes of
   refusals immediately after an environment is resumed from a parked state, even
   for someone correctly assigned - that is warm-up, not a lost assignment.
2. **Read production diagnostics.** Reader on the subscription is enough for
   Azure Monitor, Log Analytics and the alert history, which covers most incident
   questions without any write capability.
3. **Elevated rights, on demand only.** Eligibility for the elevated subscription
   role rather than a standing assignment, activated for a bounded window and
   revoked afterwards. Two people must hold this eligibility so activation is
   never blocked on one person.
4. **Deploy.** Membership of the deployment pipeline's environments and variable
   groups, plus an identity that is not blocked by tenant guest policy. External
   guest identities are blocked from Azure DevOps by organisation policy,
   so an ARN-native account is required.
5. **Data-plane access when genuinely needed.** Key Vault secrets and storage
   data roles are separate from control-plane rights and must be granted
   explicitly. Test and production deny public network access, so these only work
   from inside the virtual network or during a controlled, time-boxed access
   window that is closed again immediately.

### Credential handling

The pipeline credential must live in a vault, not a file in someone's home
directory. Rotate it when a credential holder leaves or compromise is suspected,
and review expiry and ownership quarterly.
Prefer an automation identity over a personal token for anything a machine runs.

### Proving it works

The check is not that the roles exist. It is that a second named person, without
help, can sign in to the production console and see the jobs list, read
production logs for an incident window, and complete a deployment to development.
Until someone has actually done all three, this remains untested - which is the
condition this page exists to prevent.
