# Deployment — Information Bulletin Platform (IBP)

Infrastructure-as-code (Bicep) and deployment scripts for the ARN automated traffic-bulletin system. Deployments use **Azure Pipelines** (Azure DevOps) for CI/CD.

The template deploys at **subscription scope** across ARN's confirmed resource-group layout (see `docs/reference/azure-naming-convention.md`, agreed with the ARN technology lead 11 Jun 2026):

| Resource Group | Contents |
|---|---|
| `IBP-AUE-{ENV}-RSG-APP01` | Function App, plan, SWA, Logic App, App Insights, AI Services (FDY), Search (FIQ), action group, workbooks, alerts |
| `IBP-AUE-{ENV}-RSG-NTW01` | Private endpoints (`IBP-AUE-{ENV}-PEP-APP01..05`). VNets/subnets/NSGs are ARN-managed |
| `IBP-AUE-{ENV}-RSG-KYV01` | Key Vault |
| `IBP-AUE-{ENV}-RSG-STA01` | Storage account |
| `IBP-AUE-{ENV}-RSG-LAW01` | Log Analytics — **only** while logging stays IBP-side (see open items) |

Environments: `dev` → `DEV`, `test` → `TST`, `prod` → `PRD`. The Azure Pipelines deployment target enum is `none|dev|test|prod`.

## Playout delivery posture (24 Aug 2026)

`Zetta__Enabled` is `true` in **production and test**, both writing to `informationbulletins` on
`ibpauetststaftp01`. Development inherits the template's `false` default and has never delivered.

Production was re-enabled 13 Aug after the 2026-08-11 stale-restart incident, once two independent
guards were in place (the deadline checks in the poller/watchdog, and the refusal at the delivery
boundary in `ZettaFileDeliveryService.DeliverAsync`). Test was enabled 20 Aug so that new code can
air for a pilot station while production is held at the last known-good build as the fallback — see
`docs/canonical/decisions.md` (2026-08-20). The isolated `informationbulletins-test` share is no
longer written to.

**Test → Zetta is PROVEN and ARN-confirmed (24 Aug 2026).** Sydney/KIIS 90s job `f7321bd9`,
published 03:26:39 UTC with the station armed, logged `Audio delivered to playout system` and put
`[SYD]KIIS90s_TRAFF.wav` (1,940,444 bytes, 22.0s) at the root of the live `informationbulletins`
share; the file was gone by 03:27:30 and ARN confirmed Zetta ingested it. A pilot station can
therefore air from test with no deploy to production. Two things that delivery did NOT prove: the
drop file's sha256 against the approved audio (the check raced Zetta's ingest and got
`ResourceNotFound`), and whether Zetta ingested once or twice — see the unattended-delivery note
below. Full evidence in `docs/canonical/decisions.md` (2026-08-24).

**Both environments write the SAME file name to the SAME share root**, so exactly one environment
may have a station armed at a time. Arming is NOT in IaC: it is `MarketSchedule.PlayoutEnabled`,
surfaced as **Send to playout** per station in each environment's console, so ARN operators move a
station between environments unattended with no deployment. Delivery requires BOTH gates —
`Zetta__Enabled` for the environment and the station's own flag — and a held-back station still
generates, reviews and publishes, logging `Send to playout is off for this station` instead of a
delivery. Arming a station in both environments is a race that neither side can audit: Zetta
returns no on-air receipt, and the drop file's disappearance identifies a station's latest publish
rather than a specific job.

**An operator publishing is NOT the only way bytes reach the share.** When a scheduled slot is
never approved, `JobWatchdog` covers it rather than air silence: `DeliverCoveredSlotAsync` calls
`JobService.PublishJobAsync`, which publishes AND delivers a `WaitingApproval` job credited to
`system (deadline)`, reusing the last approved bulletin from that day. A covered slot's delivery
cutoff is also `ScheduledForUtc`, not `FallbackDeadlineUtc`, so it can still write after the normal
deadline. On 24 Aug this re-wrote the same file name 21 seconds after a manual publish (identical
bytes, same source blob). So an armed station with an enabled schedule delivers on its own cron
whether or not anyone is watching — arming is not a dormant setting.

The flag defaults TRUE and an absent Table column reads as true, so a deploy can never silently
take an already-airing station off air. `ConfigScheduleDto.PlayoutEnabled` is optional and absent
means "leave arming alone", so promoting a config bundle between environments does not move it.

Development sets no `zetta*` params at all, so it inherits `zettaEnabled = false` and cannot
deliver; `DeliverAsync` checks that gate before the deadline, the download and the market-code
resolution, so nothing else on the path is reached. `GET /api/map-config` returns the
environment's `PlayoutEnabled` so the console does not claim a bulletin reaches playout where
it cannot — on dev every station reads "This environment never sends to playout".
`zettaEnabled` and the market-code map stay in each environment's bicepparam (QT-001: a hand-set app
setting is wiped on the next deploy). A market absent from `zettaMarketCodesJson` fails delivery
loudly rather than inventing a file name, which is what keeps the other 12 markets off air.

## Sandbox validation (11 Jun 2026)

The full template + application stack was deployed end-to-end to a vendor personal subscription (`ENVIRONMENT=sbx`, `IBP-AUE-SBX-*`) and torn down the same day. Verified: multi-RG subscription-scope deploy (first run clean), all resource names against ARN's CSV, Easy Auth bearer + SWA-proxied auth, MI-only storage (host leases, queue triggers, tables, user-delegation SAS), Key Vault references, Azure OpenAI + ElevenLabs + HERE via a real bulletin (generate → 30.0s audio → approve → publish), App Insights custom metrics, LAW `FunctionAppLogs`, alerts/workbooks, SWA linked-backend `/api` proxying. Fixes that came out of it:

- **Easy Auth issuer** had a malformed hybrid `https://sts.windows.net/{tenant}/v2.0` — v1 issuer (no `/v2.0`) is required for default AAD app registrations (`function-app.bicep`).
- **SWA backend linking rewrites `authsettingsV2`** (full PUT) and drops `clientIdSettingName`, leaving the AAD provider without a client ID (every token rejected, `/.auth/login` 400). The template now passes `clientId` inline. Linking also flips `unauthenticatedClientAction` to `RedirectToLoginPage` — harmless (non-browser clients still get 401) but expect that drift.
- **Empty `defaultAuthorizationPolicy.allowedPrincipals.identities: []` is deny-all**, not "no restriction" — removed; role checks live in the worker's `AuthorizationMiddleware`.
- **Staging slot needs its own system identity + role assignments** — slots don't inherit the parent's identity, and identity-based `AzureWebJobsStorage` makes the slot host unable to start listeners without one (`function-app.bicep`, `roles-*.bicep` now take `principalIds`).
- **`broadcast-queue-poison` is now created by `storage.bicep`** — its listener otherwise 404-polls every 60s until the host lazily creates it.
- A runtime DI cycle in the app (`IDurationPredictor` mode `HistoryBased`) hung every queue/timer invocation until `functionTimeout`; fixed in `src/ArnBulletinGenerator.Infrastructure/DependencyInjection.cs` with a regression test.

## Operator console authentication (SWA → Function App)

The SWA proxies `/api/*` to the Function App via a **linked backend** and terminates auth with its **built-in** Entra provider (`/.auth/login/aad` → `identity.7.azurestaticapps.net`). It forwards the **SWA client-principal** format (`identityProvider`/`userId`/`userDetails`/`userRoles`/`claims`) — *not* the App Service Easy Auth blob (`auth_typ`/`claims[].typ`). `ClientPrincipal` + `AuthorizationMiddleware` accept both formats (see `docs/canonical/decisions.md`, 2026-06-19); without this every `/api/*` call 401s with the trace `principal auth_typ is not 'aad'`.

**Roles.** SWA custom auth has two separate role surfaces:

- The Entra token can carry AAD app-role claims (`claims[].typ == "roles"`, e.g. `Admin` / `Operator`). ARN controls these role definitions, group/user assignments, ID-token enablement, and tenant admin consent.
- SWA forwards only `userRoles` to the linked backend for normal `/api/*` calls. It does **not** automatically copy AAD app-role claims into `userRoles`.
- `deployment/static-web-app/staticwebapp.rbac.config.json` therefore configures `auth.rolesSource = "/api/GetRoles"`. `RolesSourceHttpTrigger` maps only the known Arn roles (`Operator`, `Admin`) from AAD token claims into SWA custom roles at sign-in. The Function App Easy Auth `excludedPaths` and `AuthorizationMiddleware` both bypass `/api/GetRoles`; SWA marks the rolesSource function as platform-internal once configured.
- **rolesSource token audience (`login.loginParameters`) — this is what makes self-registration work.** The rolesSource POST is the only moment SWA sends the caller's real Entra `oid` to the backend, and `SwaRolesSourceTokenValidator` persists nothing from a token it cannot cryptographically validate (`ValidAudiences = [<clientId>, api://<clientId>]`). With only OIDC scopes requested, Entra defaults the ACCESS token's resource to **Microsoft Graph** (`aud=00000003-0000-0000-c000-000000000000`), which this app can never validate — so from the validator's introduction (`06cf9f8a`, 2026-08-11) until 2026-08-13 **every sign-in on test and prod was discarded and no operator row was ever written**. The config therefore requests `<clientId>/.default` alongside the OIDC scopes, which makes Entra mint the token for THIS app. Because that value is per-environment and `loginParameters` entries are literals (SWA expands `clientIdSettingName`, but not the contents of `loginParameters`), the config is **rendered, never copied** — `deployment/static-web-app/render-rbac-config.py` injects the scope, reading `aadClientId` from the same `main.<env>.bicepparam` that provisions the Function App, so console and backend cannot disagree. All three deploy paths (`arn deploy web`, `deploy-stage.yml`, `deploy.sh`) call it. Measured against Entra 2026-08-13: the bare `<clientId>/.default` is accepted, while `api://<clientId>/.default` fails `AADSTS500011 invalid_resource` (these registrations expose no `identifierUris` and no scopes) — do not "tidy" it into an `api://` URI. The canonical JSON deliberately ships the OIDC scopes only, so a path that copies it instead of rendering degrades to "no self-registration" rather than to a broken sign-in.
- **Dev:** `devAuthFallbackRole = 'Admin'` (`main.dev.bicepparam` → app setting `Auth__DevFallbackRole`) grants every authenticated AAD caller `Admin` so the console is usable. Empty in test/prod.
- **Test/prod (RBAC):** test and prod use the custom Entra provider + rolesSource bridge. The provider config is auto-overlaid onto the test/prod SWA artifact both by the pipeline (`deployment/pipelines/deploy-stage.yml`) and by `deploy.sh` (Step 4, gated on `$ENVIRONMENT`) — dev/sbx keep the shared `web/public/staticwebapp.config.json` built-in provider via both paths. Before 2026-07-08 `deploy.sh` had no such overlay, so a manual `deploy.sh ENVIRONMENT=test` (Test's only deploy path — it's excluded from the pipeline) silently reverted Test's SWA to the built-in provider; bit prod once already on the pipeline-adjacent path (2026-07-07) and had to be patched by hand each time. ARN prerequisites: assign `Operator`/`Admin` app roles via the per-env Entra groups/users, add the SWA reply URL `https://<swa-host>/.auth/login/aad/callback`, enable ID tokens, provide a client secret, and grant tenant admin consent for the sign-in permissions.
- **SWA app-settings idempotency guard:** `staticWebAppSettings` (`static-web-app.bicep`) previously did a full property overwrite with only `VITE_API_BASE_URL`, so any redeploy silently wiped the `AAD_CLIENT_ID`/`AAD_CLIENT_SECRET` a human had set out-of-band — hit live on Test 2 Jul 2026 (`/.auth/login/aad` 404'd until manually restored). Fixed by making the module accept `aadClientId`/`aadClientSecret` as explicit params (empty by default, so dev/sandbox are unaffected); `main.bicep` resolves the secret once per deploy via the Key Vault `getSecret()` module-param function (ARM's control-plane "template deployment service" is Key Vault-trusted and bypasses `publicNetworkAccess=Disabled` — a plain SWA managed identity is **not** on that trusted-services list and cannot resolve `@Microsoft.KeyVault()` references at runtime with no VNet path, which an earlier attempt at this fix discovered the hard way). Requires the Key Vault's `enabledForTemplateDeployment=true` (`key-vault.bicep`), independent of RBAC/network settings. Set `configureSwaAadKeyVaultSettings=true` in test/prod bicepparams only.
- **Linked-backend auth drift guard:** creating/updating the SWA linked backend can rewrite the Function App's `authsettingsV2`. `deployment/modules/function-authsettings.bicep` reapplies the pinned per-env AAD audience after `linkedBackend`, while preserving the `azureStaticWebApps` provider required for proxied `/api/*` traffic.
- **Custom domain ⇒ Entra redirect URI (AADSTS50011):** binding a SWA custom domain does **not** touch the Entra app registration, and the SWA callback path is host-relative (`https://<host>/.auth/login/aad/callback`). So the moment a custom domain goes live, sign-in on it fails with `AADSTS50011: The redirect URI ... does not match the redirect URIs configured` while the default `*.azurestaticapps.net` hostname keeps working — hit live on Test 2026-07-31, minutes after `ain-test.arn.com.au` reached `Ready`. **Add the new callback to the per-env app registration as part of the same change, and keep the existing `*.azurestaticapps.net` URI** (removing it breaks the default hostname and the smoke tests that use it). This needs ARN identity: the vendor delivery account can `GET /applications` but `PATCH` returns `Authorization_RequestDenied`, and it holds no eligible Entra directory role. Dev needs its own entry (`https://ain-dev.arn.com.au/.auth/login/aad/callback`) only once dev moves off the anonymous built-in-provider config.
- **Azure Monitor alerts → Teams (`routeAlertsToTeams`):** the action groups carry no email receivers since the vendor stop-gap address was removed at handover, so email alerts reach nobody at ARN until ARN's platform owner supplies the on-call DL or the central `mgt-aue-ibp-agp-*` group ID. Setting `routeAlertsToTeams = true` adds the notification Logic App as a `TeamsAlerts` webhook receiver (`useCommonAlertSchema: true`), so alerts land in the same channel as the app's approval cards. **Live in all three environments** since 2026-08-07 (verified 2026-08-12: a `TeamsAlerts` webhook receiver is present on the dev, test and prod action groups, and `routeAlertsToTeams = true` in all three bicepparams). The URL is resolved in-template from the Logic App trigger callback — never put it in a bicepparam, it carries a `sig=`. Keep an email receiver configured once ARN nominates one: a Logic App inside the subscription cannot be trusted to report that the subscription is down. Two Logic App footguns found live 2026-07-31: the branch MUST discriminate on the top-level `schemaId`, not `data.essentials.alertRule` (Azure's own `createNotifications` test sends the full schema with `alertRule` **null**, which routes the test into the job-card branch and fails it), and `createArray()` with no arguments is rejected outright — use `json('[]')`.

### Production SWA auth bring-up (two-phase — REQUIRED for a greenfield prod)

`configureSwaAadKeyVaultSettings` drives `keyVault.getSecret('AadClientSecret')` in `main.bicep`, which ARM resolves at **parameter-binding time — before any resource in the template deploys**. So on a brand-new environment the deploy fails (`KeyVaultParameterReferenceSecretRetrieveFailed`) unless the Key Vault **and** the `AadClientSecret` secret already exist. `main.prod.bicepparam` therefore ships this `false`; bring prod up in two phases:

1. **Phase 1 — infra + secrets (flag `false`).** Run the deploy with `configureSwaAadKeyVaultSettings = false` (the committed default). This creates every resource, incl. the Key Vault with `enabledForTemplateDeployment: true` (set in `key-vault.bicep`, so — unlike Test's pre-existing vault — no manual CLI flip is needed). The SWA gets only `VITE_API_BASE_URL`; auth is not wired yet. Then seed the AAD client secret into Key Vault (alongside the HERE/ElevenLabs/AI keys — see `deploy.sh` Step 2, or manually):
   ```bash
   az keyvault secret set --vault-name IBP-AUE-PRD-KYV-APP01 --name AadClientSecret --value '<prod-client-secret>' -o none
   az keyvault secret set --vault-name IBP-AUE-PRD-KYV-APP01 --name AadClientId    --value '<prod-client-id>'     -o none
   ```
2. **Phase 2 — activate SWA auth (flag `true`).** Flip `configureSwaAadKeyVaultSettings = true` in `main.prod.bicepparam` and redeploy. `getSecret()` now resolves (vault + secret + template-deployment flag all present), the SWA gets `AAD_CLIENT_ID`/`AAD_CLIENT_SECRET`, and **every subsequent redeploy is idempotent** — it re-sets the identical values instead of wiping them. **Leave it `true` from here on**; reverting to `false` re-drops the SWA AAD settings and 404s `/.auth/login/aad`.

After Phase 2, verify with a fresh browser round-trip (not just `/api/health`): logout → `/.auth/login/aad` → `/.auth/me` shows `userRoles` includes `Admin`/`Operator` → `/api/jobs` returns 200. The pipeline smoke test (`smoke-test.sh`) only covers `/api/health` + auth-fail-closed, so the role-mapping chain needs this manual check per environment.

### Rotating the SWA AAD client secret

The SWA `AAD_CLIENT_SECRET` app setting holds a **plain copy** resolved at deploy time (SWA can't use a runtime `@Microsoft.KeyVault()` reference — it's not a Key Vault trusted service and has no VNet path to a network-locked vault). Unlike the Function App (runtime KV reference, auto-picks-up rotations), **the SWA value goes stale the moment the secret is rotated in Key Vault** — sign-in breaks when the old secret expires. On rotation:

1. Update the app registration's client secret and set the new value in Key Vault: `az keyvault secret set --vault-name IBP-AUE-<ENV>-KYV-APP01 --name AadClientSecret --value '<new>'`.
2. Re-run the deploy (`configureSwaAadKeyVaultSettings` already `true`) so `getSecret()` re-reads and rewrites the SWA setting — or, for a hotfix without a full deploy, set it directly: `az staticwebapp appsettings set -n IBP-AUE-<ENV>-SWA-APP01 -g IBP-AUE-<ENV>-RSG-APP01 --setting-names AAD_CLIENT_SECRET='<new>'`.
3. The Function App picks up the rotation automatically (its `AadClientSecret` is a runtime KV reference) — no redeploy needed there.

Track the client-secret expiry; a lapsed secret fails both the SWA login and the Function App Easy Auth validation.

## Open items (placeholder parameters)

These track unconfirmed ARN-side decisions. Each has a safe default so every environment deploys self-contained today; flip the parameter when the answer lands.

| Open item | Parameter | Placeholder behaviour | Pending |
|---|---|---|---|
| Log Analytics: central (MGT sub) vs IBP-side. ARN's CSV says `MGT-AUE-{ENV}-LAW-APP01` but their RG list still carries an IBP LAW RG | `logAnalyticsWorkspaceResourceId` | Empty → creates `IBP-AUE-{ENV}-LAW-APP01` in `RSG-LAW01`. Set the MGT workspace resource ID once confirmed (App Insights + diagnostics link cross-subscription; our deploy identity needs rights on that workspace) | ARN technology lead (question sent 11 Jun) |
| Central action groups `mgt-aue-ibp-agp-{Purpose}` | `actionGroupId` | Empty → creates `IBP-AUE-{ENV}-AIA-APP01` from `alertEmailReceivers`/`alertSmsReceivers` | ARN technology lead (resource IDs; prod DLs undefined) |
| Dedicated IBP subscription | — (deploy context) | **Confirmed (18/19 Jun)** — the ARN technology lead provisioning a dedicated IBP subscription; the vendor engineer was granted PIM-eligible Owner. Deploys into whatever subscription `az account`/the service connection targets. Subscription ID + DevOps service connection pending delivery | ARN technology lead ✓ |
| RG creation ownership (ARN creates + grants, or we create) | `createResourceGroups` | `true` → template creates the RGs. Set `false` when ARN pre-creates them | ARN technology lead |
| PE subnet + 5 privatelink DNS zone IDs | `privateEndpointSubnetId`, `privateDnsZone*Id` | `<placeholder>` values in test/prod params fail validation intentionally | ARN network team |
| AAD app registration (client ID) | `aadClientId` (tenant pinned) | Dev + test pin the ARN tenant (`91d5bc47-…`); the per-env `aadClientId` `<placeholder>` fails validation intentionally — auth is fail-closed | ARN identity team (per-env app registration) |
| SWA custom Entra provider for RBAC roles | `deployment/static-web-app/staticwebapp.rbac.config.json` (auto-overlaid onto test/prod by the pipeline) + SWA `AAD_CLIENT_ID`/`AAD_CLIENT_SECRET` settings | Test/prod ship the custom provider (token carries `Operator`/`Admin` roles); dev keeps the built-in provider via `deploy.sh` + `devAuthFallbackRole`. Login works once the SWA app settings are set | ARN identity team (per-env client ID + secret; add SWA redirect URI; assign Operator/Admin) |
| Audit-evidence reader AAD group (the audit-evidence stakeholders, CC-002) | `auditAccessGroupObjectId` | Empty → role assignments skipped; audit evidence readable only by the platform identities until the group lands | ARN technology lead (group creation) |
| Exact retention figures: aired audio (≥90d floor) + multi-year audit evidence | `publishedAudioDeleteAfterDays`, `unpublishedAudioDeleteAfterDays`, `auditExportsDeleteAfterDays` | `0` → no delete rules, everything kept forever (compliant over-retention). Set the day counts when CC-010 lands — lifecycle delete rules materialise automatically (aired vs never-aired audio split on the `published` blob index tag) | ARN legal (CC-010 email) |
| Telemetry retention while logging stays IBP-side | `logRetentionDays` | 30 days (Azure default). Moot if ARN central logging is confirmed | Tied to MGT-vs-IBP answer (ARN technology lead) |

## CI/CD Pipeline

The pipeline is defined in `azure-pipelines.yml`. Build and Package are
environment-agnostic and run concurrently. Deployment is a compile-time
selected **Dev → Test → Prod** promotion via the reusable stage template
`deployment/pipelines/deploy-stage.yml`.

### Pipeline queue parameters

Queue Pipeline 108 manually on `master` or `main` with these parameters:

| Parameter | Default | Allowed values | Effect |
|---|---|---|---|
| `targetEnvironment` | `none` | `none`, `dev`, `test`, `prod` | `none` compiles no deployment stage; `dev` compiles Dev; `test` compiles Dev → Test; `prod` compiles Dev → Test → Prod |
| `deployInfrastructure` | `true` | `true` / `false` | `true` runs Bicep and autoscale in each compiled stage; `false` skips only those jobs for an app-only promotion |
| `enableEvalGate` | `false` | `true` / `false` | Compiles the golden and scheduled evaluation stages |

Automatic pushes use the default `targetEnvironment=none`, so they compile
to Build and Package only and deploy nothing. `targetEnvironment` is the
deployment selector; there are no separate development-deploy or promotion
switches. The selected deploy stages still require the `master`/`main` branch
condition and, for Test and Production, their configured Azure DevOps
environment approvals.

`deployInfrastructure=false` is an app-only promotion, not a no-op: it omits
the Infrastructure job (subscription-scope Bicep and autoscale) while the
Function App, Static Web App, documentation site when enabled by the
environment variable group, and Smoke jobs still run. The same Build and
Package artefacts move through the compiled stages; nothing is rebuilt per
environment. The default `true` path includes Infrastructure and autoscale.

The parameter contract above is source-verified from `azure-pipelines.yml` on
2026-09-03. The historical green run noted below predates this contract and is
evidence only for the gates it actually exercised; it is not live proof of
every current queue combination.

**CI stages and gates** (Build/Package gates verified green 2026-06-29; the
current deployment parameter combinations are source-defined as above):

| Stage | Jobs / gates |
|---|---|
| **Build & Test** | Backend: NuGet cache → restore → **.NET vulnerability scan** (fails on High/Critical) → build → unit tests (+ coverage) → backend E2E (Functions host + fake providers) → **build-once** publish of the `functions` artifact. In parallel: **Bicep lint** + **secret scan** (gitleaks, `.gitleaks.toml` allowlist). |
| **Package** | Frontend: npm + Playwright cache → `npm ci` → **lint** → **unit tests** (vitest, TZ-pinned) → **npm audit** (high) → Playwright **E2E** (mock) → build → `web` artifact. |
| **Deploy_Dev** (`targetEnvironment=dev`, `test`, or `prod`) | Infrastructure when `deployInfrastructure=true`, then Function zip-deploy, SWA deploy, documentation site when enabled, and **post-deploy smoke test** (`smoke-test.sh`: `/api/health` + auth fail-closed). |
| **Deploy_Test** (`targetEnvironment=test` or `prod`) | Same artefacts and jobs as Dev, with the Test ADO environment approval. |
| **Deploy_Prod** (`targetEnvironment=prod`) | Same artefacts and jobs as Test, with the Production ADO environment approval. |
| **NotifyOnFailure** | Posts to `TEAMS_CICD_WEBHOOK` on any CI failure (no-op until the webhook is set). |

Triggers: PR + push to `master`/`main`, path-filtered, `batch: true`.
`Deploy_Test` depends on `Deploy_Dev`, and `Deploy_Prod` depends on
`Deploy_Test`. The custom-Entra-provider RBAC SWA config is rendered for
Test/Production (`applyRbacConfig: true` → `render-rbac-config.py
--param-file <bicepParamFile>`) and skipped for Dev. The documentation site
publishes from the same pipeline where `deployDocsSite` is `true` in the
environment's variable group (Dev + Production; Test is `false`) and
`DOCS_SWA_DEPLOYMENT_TOKEN` is set — both apps are live: prod
`red-pond-03b957f00`, dev `white-forest-0e4647200`.

### Pipeline Setup (one-time)

**1. Service connections** — one Azure Resource Manager service connection per environment, subscription-scoped (subscription-scope deployments create resource groups and role assignments), with **Owner** (or **Contributor + User Access Administrator**) on the target subscription — or on the pre-created IBP RGs with `createResourceGroups=false`. Reference its name via the `azureServiceConnection` variable in each env's variable group.

**2. Variable groups** — one per environment, attached by the deploy stage:

| Environment | Variable group | Must contain |
|---|---|---|
| Dev | `arn-traffic-dev` | `azureServiceConnection`, `aadClientId`, `aadTenantId`, `functionAppName` (`IBP-AUE-DEV-FNA-APP01`), `SWA_DEPLOYMENT_TOKEN` (link from `IBP-AUE-DEV-KYV-APP01`) |
| Test | `arn-traffic-test` | `azureServiceConnection`, `aadClientId`, `aadTenantId`, `functionAppName` (`IBP-AUE-TST-FNA-APP01`), `SWA_DEPLOYMENT_TOKEN` (link from `IBP-AUE-TST-KYV-APP01`) |
| Prod | `arn-traffic-variables` | `azureServiceConnection`, `aadClientId`, `aadTenantId`, `functionAppName` (`IBP-AUE-PRD-FNA-APP01`), `SWA_DEPLOYMENT_TOKEN` (link from `IBP-AUE-PRD-KYV-APP01`) |

Partner secrets (`Here--ApiKey`, `AiSettings--ElevenLabs--ApiKey`, `OfficialFeeds--QldApiKey`, `AadClientSecret`, `Zetta--*`) live in the environment Key Vault, not in pipeline variables. The Function App reads them **at runtime** through `AddAzureKeyVault` over the private endpoint — deliberately NOT through `@Microsoft.KeyVault(...)` app-setting references. See [Why there are no Key Vault references](#why-there-are-no-key-vault-references).

**3. ADO environments** — `dev`, `test` and `production` under Pipelines → Environments. Add the required approvers per environment, and **authorise** each environment + variable group for the pipeline (needs an ADO admin — a vendor guest account cannot self-grant). The `arn-traffic-dev`, `arn-traffic-test` and `arn-traffic-variables` groups must be authorised for their corresponding stages.

**4. (Optional) Failure alerts** — set a `TEAMS_CICD_WEBHOOK` pipeline/group variable (a Teams Incoming Webhook or Power Automate URL) to have the `NotifyOnFailure` stage post on CI failure; left empty it is a no-op. The post-deploy smoke test (`smoke-test.sh`) runs the unauthenticated checks automatically; set `ARN_SMOKE_BEARER` (+ `ARN_SMOKE_TRIGGER=1`) in the env variable group to also exercise authenticated endpoints / a synthetic broadcast.

## Manual Deployment (Alternative)

If CI/CD is unavailable, use `deploy.sh` for manual deployment:

```bash
# 1. Log in and pick the environment
az login
export SUBSCRIPTION="<your-subscription-id>"
export ENVIRONMENT="dev"          # dev | test | prod

# 2. Set API keys (stored in Key Vault during deploy)
export ELEVENLABS_API_KEY="<your-elevenlabs-key>"
export HERE_API_KEY="<your-here-key>"
export HERE_APP_ID="<your-here-app-id>"

# 3. Deploy everything (infra + code + frontend)
./deployment/scripts/deploy.sh
```

The script derives the RG names (`IBP-AUE-{ENV}-RSG-*`) and picks `main.{env}.bicepparam` from `ENVIRONMENT`; override with `BICEP_PARAMS_FILE` if needed.

### Selective deployment

```bash
./deployment/scripts/deploy.sh --skip-infra     # Code + frontend only
./deployment/scripts/deploy.sh --skip-code      # Infra + frontend only
./deployment/scripts/deploy.sh --skip-web       # Infra + code only
./deployment/scripts/deploy.sh --skip-infra --skip-web  # Code only
./deployment/scripts/deploy.sh --slot-swap      # Infra + code (staging slot swap) + frontend
```

**Footgun (fixed 2026-07-24):** `deploy.sh` used to forward its own `--skip-*`/`--slot-swap`
flags straight into the internal `az deployment sub what-if` call whenever infra wasn't
skipped — `az` rejected them as unrecognized arguments, so `--skip-web` alone (line 142)
failed outright. None of these flags were ever valid `az` arguments; the script now keeps
them local to its own flag parsing.

### Blue-green deployment (--slot-swap)

By default `deploy.sh` zip-deploys the function app code **directly** to the production slot (a short service disruption per deployment slot warm-up). The `--slot-swap` flag enables a **blue-green** path:

1. Zip-deploy to the **staging** slot (zero production traffic impact).
2. Poll the staging slot's `/api/health` for a successful response (up to 120s).
3. Swap the staging slot into production (instant traffic cutover).
4. Remove the staging slot (Bicep recreates it on the next `--slot-swap` deploy).

If the staging slot doesn't exist (previous deploy cleaned it up, or the environment is parked), `--slot-swap` falls back to direct zipdeploy automatically.

The staging slot is created by the Bicep template (`function-app.bicep`) when `deploymentSlotEnabled` is `true` (the default). It is removed by `park.sh` (Basic plans don't support slots) and by the slot-swap deploy itself.

### Autoscale configuration

When `deploy.sh` runs a full deployment (not `--skip-infra`), it automatically invokes `setup-autoscale.sh` to configure queue-length-based autoscaling on the App Service plan. The autoscale rules scale out on queue depth (>10 messages for 5 min) and scale in on queue drain (<3 messages for 10 min). The pipeline performs the same step after deployment. Autoscale starts disabled and is enabled by `resume.sh`.

## Parameter Files

One file per environment: `main.dev.bicepparam`, `main.test.bicepparam`, `main.prod.bicepparam`. Key parameters:

| Parameter | Description | Example |
|-----------|-------------|---------|
| `environment` | Environment (`dev`, `test`, `prod`) — drives every resource name | `prod` |
| `location` | Primary Azure region (new environments are `australiaeast`) | `australiaeast` |
| `createResourceGroups` | Template creates the RGs (false when ARN pre-creates) | `true` |
| `logAnalyticsWorkspaceResourceId` | External (MGT) workspace; empty creates IBP-side | `''` |
| `actionGroupId` | External (mgt-aue-ibp-agp-*) action group; empty creates local | `''` |
| `aiServicesLocation` | Region for Azure AI Services | `australiaeast` |
| `swaLocation` | Region for Static Web App ([limited availability](https://learn.microsoft.com/azure/static-web-apps/overview#regional-availability)) | `eastasia` |
| `tags` | Azure resource tags | `{ project: '...', ... }` |

The PoC (`arn-traffic-poc`, CAF naming, single RG, `australiasoutheast`) predates this layout and is frozen — its template lives in git history; it is documented in `docs/reference/poc-environment-inventory.md`.

## Resource Naming

Names follow ARN's convention `IBP-AUE-{ENV}-{TYPE}-APP01` (source of truth: `docs/reference/azure-naming-convention.md`):

| Resource | Type code | Example (prod) |
|----------|-----------|----------------|
| Function App | FNA | `IBP-AUE-PRD-FNA-APP01` |
| App Service Plan | PLN | `IBP-AUE-PRD-PLN-APP01` |
| Static Web App | SWA | `IBP-AUE-PRD-SWA-APP01` (name keeps `AUE` though the resource lives in East Asia) |
| Application Insights | AIN | `IBP-AUE-PRD-AIN-APP01` |
| Action Group | AIA | `IBP-AUE-PRD-AIA-APP01` |
| AI Services (Azure OpenAI) | FDY | `IBP-AUE-PRD-FDY-APP01` (custom subdomain lowercased — DNS label) |
| AI Search | FIQ | `ibp-aue-prd-fiq-app01` (**lowercase forced** — Azure AI Search rejects uppercase) |
| Logic App | LOA | `IBP-AUE-PRD-LOA-APP01` |
| Key Vault | KYV | `IBP-AUE-PRD-KYV-APP01` |
| Storage Account | STA | `ibpaueprdstaapp01` (lowercase, no hyphens, ≤ 24 chars) |
| Log Analytics | LAW | `IBP-AUE-PRD-LAW-APP01` (or ARN's `MGT-AUE-PRD-LAW-APP01` — open item) |
| Private Endpoints | PEP | `IBP-AUE-PRD-PEP-APP01..05` (blob, table, queue, vault, cognitive) |

## Architecture

```
main.bicep                   ← Subscription-scope orchestrator (RGs + modules)
main.{env}.bicepparam        ← Per-environment parameter values
modules/
  log-analytics.bicep        ← Log Analytics workspace (IBP-side only; skipped when ARN central)
  monitoring.bicep           ← Application Insights (workspace-based, ID supplied)
  storage.bicep              ← Storage account, blob containers, tables, queues   [STA01]
  key-vault.bicep            ← Key Vault (RBAC-enabled, secrets added post-deploy) [KYV01]
  ai-services.bicep          ← Azure AI Services + AI Search + model deployment   [APP01]
  function-app.bicep         ← App Service Plan + Function App (Linux, .NET 10)   [APP01]
  static-web-app.bicep       ← Static Web App for React/Vite frontend             [APP01]
  linked-backend.bicep       ← Links Function App as SWA backend (/api/* proxy)
  logic-app-notification.bicep ← Logic App + Teams API connection                 [APP01]
  action-group.bicep         ← Local action group (until ARN central IDs land)
  workbooks.bicep            ← Operations Overview workbook (single dashboard)
  alerts.bicep               ← 9 alert rules (3 metric + 6 log-search)
  private-endpoints.bicep    ← PEs for Storage/KV/AI, targets by resource ID      [NTW01]
  network.bicep              ← Self-built VNet + subnets + 5 privatelink DNS zones (createNetwork)  [NTW01]
  roles-storage.bicep        ← Function MSI roles on storage                      [STA01]
  roles-key-vault.bicep      ← Function MSI roles on Key Vault                    [KYV01]
  roles-ai-services.bicep    ← Function MSI roles on AI Services                  [APP01]
scripts/
  deploy.sh                  ← End-to-end deployment script (manual alternative)
  provision-foundry-agents.sh← AI Foundry agent provisioning
  setup-autoscale.sh         ← Post-deployment autoscale config
azure-pipelines.yml          ← Azure Pipelines CI/CD (repo root)
```

## Secrets Management

API keys are **never** stored in Bicep files or parameter files. The pipeline auto-populates Key Vault secrets:
- **AI Services key**: read directly from the deployed resource and stored in KV
- **All other secrets**: sourced from the `Arn-Prod-Secrets` variable group (linked to Key Vault)

The Function App reads secrets at startup through `AddAzureKeyVault`; do not add `@Microsoft.KeyVault(SecretUri=...)` app settings. Non-secret feed endpoints and switches are emitted by `modules/function-app.bicep` as:

| App setting | Purpose |
|-------------|---------|
| `OfficialFeeds__Enabled` | Master official-feed switch |
| `OfficialFeeds__CorroborateIncidents` | HERE/authority corroboration |
| `OfficialFeeds__InjectMissingOfficialIncidents` | Inject missing official incidents |
| `OfficialFeeds__MaxCorroborationDistanceMeters` | Spatial match distance |
| `OfficialFeeds__NswHazardsBaseUrl` | Live Traffic NSW endpoint |
| `OfficialFeeds__QldEventsUrl` | QLDTraffic events endpoint |
| `OfficialFeeds__VicDisruptionsUrl` | DataVic disruptions endpoint |
| `OfficialFeeds__WaTravelMapUrl` | Main Roads WA endpoint |
| `OfficialFeeds__SaTrafficUrl` | TrafficSA endpoint |
| `OfficialFeeds__TasAlertUrl` | TasALERT endpoint |
| `Broadcast__OfficialFeedFreshMinutes` | Routine stale-data age (15 minutes) |
| `Broadcast__OfficialFeedMaxStaleMinutes` | Hard stale-data ceiling (60 minutes) |

The QLD credential is the per-environment Key Vault secret `OfficialFeeds--QldApiKey`; the template deliberately emits a blank `OfficialFeeds__QldApiKey` setting so a deployed app can never fall back to the local demo key. Seed the secret in each environment's vault before enabling QLDTraffic.

Before deploying/restarting an environment, seed the behaviour-neutral fallback key deliberately (until TMR supplies a dedicated key). Key Vault public network access is disabled in Test/Prod, so run this from the same deployment runner/network path used for the existing `Here--ApiKey` and ElevenLabs seeding (private-endpoint connectivity or an approved temporary firewall path); a normal public workstation receives `ForbiddenByConnection`.

```bash
ENVIRONMENT=test # dev | test | prod
case "$ENVIRONMENT" in
  dev)  ENV_CODE=DEV ;;
  test) ENV_CODE=TST ;;
  prod) ENV_CODE=PRD ;;
  *) echo "Unsupported environment"; exit 1 ;;
esac
az keyvault secret set \
  --vault-name "IBP-AUE-${ENV_CODE}-KYV-APP01" \
  --name "OfficialFeeds--QldApiKey" \
  --value '3e83add325cbb69ac4d8e5bf433d770b' \
  --subscription "$SUBSCRIPTION" -o none
```

Replace the value with the environment's dedicated QLDTraffic key when ARN supplies one; no app-setting or code change is required.

`deployment/scripts/deploy.sh` performs this idempotently in its QLDTraffic seeding step: set `QLDTRAFFIC_API_KEY` to a dedicated value to replace the secret, or leave it unset to preserve an existing value and seed the published demo key only when the secret is absent. The script uses the same direct `az keyvault secret set` path and warns rather than pretending a locked vault was updated.

| Secret name | Source |
|-------------|--------|
| `AiSettings--AzureAI--ApiKey` | Auto-populated from deployed AI Services resource (in CI/CD) |
| `AiSettings--ElevenLabs--ApiKey` | `Arn-Prod-Secrets` variable group (linked to KV) |
| `Here--ApiKey` | `Arn-Prod-Secrets` variable group (linked to KV) |
| `OfficialFeeds--QldApiKey` | Per-environment QLDTraffic key, seeded in that environment's KV |
| `AadClientSecret` | `Arn-Prod-Secrets` variable group (linked to KV) |
| `SftpDelivery--Username` / `SftpDelivery--Password` | Safekeeping of the vendor-facing SFTP pull credentials for the blob drop (BlobDrop transport). NOT read by the app — it writes blobs via managed identity (`roles-delivery-drop.bicep`). Test: provisioned by ARN on `ibpauetststaftp01` |
| _(none)_ | AzureFilesDrop transport needs no stored secret either — the app writes to the Azure Files share via managed identity (`Storage File Data Privileged Contributor`, OAuth-over-REST, `roles-delivery-drop.bicep`); the playout side authenticates separately via its own on-prem AD identity against the AD DS-joined storage account (ARN-managed RBAC, not ours) |

For manual (`deploy.sh`) deployments, export the corresponding environment variables instead.

## Manual Post-Deployment Steps
The canonical register of manual and out-of-band setup/maintenance steps, including
the event that invalidates each one, is the [Manual Environment Steps Register](../docs/operations/manual-steps.md).

1. **Configure Teams notifications** (optional):
   Set `webhookEndpointUrl` via CI/CD variables or a secure parameter file if Power Automate Workflows notifications should be enabled; `key-vault.bicep` stores it as the secret `Webhook--EndpointUrl`. The Teams API connection requires one-time Portal authorisation per environment.

2. **Verify the app resolved its secrets** — the Function App loads the vault at startup, so the check is the health endpoint plus the absence of Key Vault `Forbidden` audit events, not an app-settings listing:
   ```bash
   az monitor log-analytics query -w <workspace-guid> --analytics-query \
     'AzureDiagnostics | where TimeGenerated > ago(1h) | where ResourceProvider == "MICROSOFT.KEYVAULT" | summarize by ResultSignature, CallerIPAddress'
   ```
   Every `SecretGet` should be `OK` from a private `10.x` caller. A `Forbidden` from a public IP means something reintroduced a platform Key Vault reference.

## Why there are no Key Vault references

App Service resolves `@Microsoft.KeyVault(...)` app settings in the **platform configuration layer**, which egresses from the site's public outbound IP and does not honour VNet integration. Test and prod vaults set `publicNetworkAccess='Disabled'`, so every such resolution is refused with *"Public network access is disabled and request is not from a trusted service nor via an approved private link"*.

Measured in prod on 2026-08-12: 7 denials per App Service reference-refresh cycle, one cycle a day, every day back to at least 13 July, plus a burst on every restart — 44 `Forbidden` against 100 `OK` over three days. Every denial came from the site's public IP; every success came from a private endpoint. It fired `IBP-AUE-<ENV>-FNA-APP01-kv-access-denied` indefinitely while changing nothing functionally, because `Program.cs` already loads the whole vault via `AddAzureKeyVault` over the private endpoint *after* `AddEnvironmentVariables`, so vault values win regardless.

Secrets therefore reach the app by name convention only: `--` maps to the configuration separator `:` (`Here--ApiKey` → `Here:ApiKey` → binds `HereOptions.ApiKey`). A new secret must be named to match its options section. Do not add `@Microsoft.KeyVault(...)` settings to `modules/function-app.bicep`.

## Deploying Infrastructure Only (Manual)

```bash
az deployment sub create \
  --location australiaeast \
  --template-file deployment/main.bicep \
  --parameters deployment/main.dev.bicepparam
```

## Self-served private environment (`createNetwork`)

When ARN's hub network isn't wired in, set `createNetwork=true` (see `main.test.bicepparam`) and `main.bicep` builds a self-contained private network in `RSG-NTW01` via `modules/network.bicep`: a VNet (`10.61.0.0/24`), a Function-integration subnet (delegated), a private-endpoint subnet, and the 5 privatelink DNS zones (linked to the VNet). Its outputs feed the Function App VNet integration + the private endpoints, so `privateEndpointSubnetId` / `virtualNetworkSubnetId` / `privateDnsZone*Id` stay empty. Prod keeps `createNetwork=false` and consumes ARN's hub-supplied IDs instead.

**The self-built VNet is an isolated island** (no hub peering, no central DNS, default internet egress). Confirm its CIDR with ARN before any future peering to the hub.

### Deploying a private env from outside the VNet

`dataPlanePublicNetworkAccess='Disabled'` is the end state, but an operator running `az` from outside the VNet can't write Key Vault secrets or enqueue against a locked data plane. Sequence:

1. Deploy with `dataPlanePublicNetworkAccess='Enabled'` (override a copy of the param) — PEs + VNet integration are created alongside the open data plane.
2. Self-grant data-plane RBAC (Owner → Key Vault Secrets Officer + Storage Blob/Queue/Table Data Contributor), set KV secrets, deploy code + SWA, run a queue E2E.
3. Flip storage/KV/AI to `Disabled` (`az storage account update` / `az keyvault update` / `az resource update`), restart the Function App, and confirm it still runs over the private endpoints (host `Running` + functions loaded proves it reaches storage via the PE).

Test (`IBP-AUE-TST-*`) was stood up this way on 29 Jun 2026.

## Parking / resuming an environment (idle cost control)

`deployment/scripts/park.sh` + `resume.sh` minimise an environment's idle cost without tearing it down. Per environment:

```bash
SUBSCRIPTION=<sub-id> ENVIRONMENT=test ./deployment/scripts/park.sh     # park (stop paying Premium)
SUBSCRIPTION=<sub-id> ENVIRONMENT=test ./deployment/scripts/resume.sh   # resume
```

**park** disables the env's alert rules + availability test **first**, then stops the Function App (so the scheduled poller doesn't run, avoiding AI/HERE/ElevenLabs pay-per-use) and scales the App Service plan from PremiumV3 (`P0v3`) down to Basic (`B1`), the dominant always-on cost. Basic plans don't allow deployment slots, so the staging slot is removed on the way down (only used for blue-green deploys; recreated by the next full deploy). **resume** scales back to `P0v3`, starts the app, and re-enables alerts.

Silencing comes first deliberately: every step of a park makes a live-looking environment unhealthy. The downgrade also retries 6× with 90s backoff, because Azure returns a transient "No available instances" for the target SKU under regional capacity pressure — it took six tries on 2026-07-30. Before both fixes, that run aborted at the verification step with prod stopped and its availability test still armed, and alerts had to be silenced by hand. If the downgrade still fails after six attempts the script exits with the app stopped and alerts already quiet; re-run it later.

Works for any environment (`dev|test|prod|sbx`). **Prod** normally runs 24/7, so `park.sh` prompts for explicit confirmation before parking it. Override the SKUs with `PARK_SKU` / `RESUME_SKU`.

**Residual cost while parked** (park is "minimise", not "$0"): the private endpoints (~$7/mo each), the Static Web App (Standard), Key Vault, storage, and the B1 plan still bill — roughly half the running cost. AI/OpenAI is pay-per-use so it's $0 while the app is stopped. For **near-zero** idle cost, tear the environment down instead — delete the 5 `IBP-AUE-{ENV}-RSG-*` groups and redeploy on demand (everything is IaC); note that recreating the SWA yields a new hostname, so the app-registration redirect URI must be re-pointed.

> A `--sku B1` park is reset to `P0v3` by a full `az deployment sub create` (the template pins the SKU). Park/resume are an operational lever, not an IaC state.

> Deploying into a parked environment also **re-arms its alert rules** — the template creates them `enabled: true`, so the stopped app trips `-no-executions` within 15 minutes. Observed on prod, 2026-08-10, after a narrow `alerts.bicep` module deploy. Either resume the environment or re-run park's silencing step afterwards.

## Adding a New Environment

1. Copy an existing `main.<env>.bicepparam` and update `environment`, `tags`, and notification settings (the `environment` value must be in the template's allowed list — extend `environmentCodes` in `main.bicep` for new codes)
2. Deploy: `ENVIRONMENT=<env> ./deployment/scripts/deploy.sh`
