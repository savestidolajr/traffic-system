// ============================================================================
// Module:  workbooks.bicep
// Purpose: Provision the Operations Overview workbook — the single operational
//          dashboard for the ArnBulletinGenerator platform.
//
// History: this module shipped on 2026-06-01 with five single-panel workbooks
// (Broadcast Health, Pipeline Latency, Queue Status, External Dependencies,
// Cost Tracking). Operations Overview arrived on 2026-06-24 and subsumed every
// one of their panels, but the five were never removed — so each environment
// carried six workbooks where one was the dashboard and five were duplicate
// lenses an operator could not even re-scope (they hardcoded `timeContext`
// and declared no time parameter). Two of them had also drifted into being
// misleading: Broadcast Health's description cited panels it did not contain,
// and Queue Status implied a poison RATE it never calculated (doc-audit MON-01).
// Deleted from IaC and from all three environments on 2026-08-10.
//
// A workbook rather than a `Microsoft.Portal/dashboards` resource, deliberately:
//   - one `TimeRange` parameter re-scopes all 13 queries (dashboard tiles each
//     pin their own time range, with no page-level parameter),
//   - `fallbackResourceIds` lets the identical JSON deploy to dev/test/prod
//     (dashboard tiles hardbake resource IDs into every tile's inputs),
//   - the KQL lives in git and diffs (dashboard Log Analytics tiles are pinned
//     chart snapshots, re-pinned by hand from the Logs blade),
//   - markdown section headers, tables and per-item widths have no dashboard
//     equivalent.
// The one thing a dashboard does better — pinning to portal Home — is a
// navigation concern, not a content one.
//
// Every panel that splits customMetrics by customDimensions depends on the
// dimension NAMES declared in ApplicationInsightsBroadcastMetrics.SafeTrack.
// Drop them there and these panels silently collapse to one unlabelled series.
// ============================================================================

@description('Azure region for the workbook resource (workbooks live wherever the linked app insights does).')
param location string

@description('Resource ID of the Application Insights / Log Analytics workspace these workbooks query.')
param sourceResourceId string

@description('Prefix applied to the workbook display name (e.g. "arn-prod").')
param namePrefix string

@description('Tags to apply.')
param tags object = {}

// Consolidated single-pane operations dashboard (SoW "operational dashboards" —
// system health + bulletin processing status). Workbook resource NAMES must be
// GUIDs (an Azure constraint on Microsoft.Insights/workbooks), so the name is a
// deterministic guid() and the human-readable name lives in displayName.
resource operationsOverview 'Microsoft.Insights/workbooks@2022-04-01' = {
  name: guid('${namePrefix}-operations-overview')
  location: location
  kind: 'shared'
  tags: tags
  properties: {
    displayName: '${namePrefix} — Operations Overview'
    description: 'Single-pane operational dashboard: bulletin pipeline health, Zetta delivery, queues, dependencies, availability, errors, and cost.'
    category: 'workbook'
    serializedData: loadTextContent('workbooks/operations-overview.workbook')
    sourceId: sourceResourceId
    version: 'Notebook/1.0'
  }
}

@description('Resource ID of the Operations Overview workbook.')
output operationsOverviewId string = operationsOverview.id
