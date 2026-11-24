// ============================================================================
// Module:  storage.bicep
// Purpose: Azure Storage Account with blob containers, tables, and queues
//          for the ArnBulletinGenerator application.
// ============================================================================

// ---------- User-Defined Types ----------

@description('Configuration for a blob container.')
type containerConfig = {
  @description('Name of the blob container.')
  name: string
}

@description('Configuration for a storage table.')
type tableConfig = {
  @description('Name of the storage table.')
  name: string
}

@description('Configuration for a storage queue.')
type queueConfig = {
  @description('Name of the storage queue.')
  name: string
}

// ---------- Parameters ----------

@description('Azure region for the storage account.')
param location string

@description('Name of the storage account. Must be 3-24 characters, lowercase letters and numbers only.')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Storage account SKU.')
param skuName string = 'Standard_LRS'

@description('Tags to apply to all resources.')
param tags object = {}

@description('Resource ID of the Log Analytics workspace for diagnostic settings.')
param logAnalyticsWorkspaceId string = ''

@description('Public network access. Set to Disabled for prod when a private endpoint is provisioned.')
@allowed(['Enabled', 'Disabled'])
param publicNetworkAccess string = 'Enabled'

@description('Blob soft-delete retention in days. 30 minimum for production; 90 for audit-compliance environments.')
@minValue(7)
@maxValue(365)
param blobSoftDeleteDays int = 30

@description('Enable blob point-in-time restore on this storage account. Requires versioning + change feed.')
param enableBlobRestore bool = false

@description('Blob restore retention in days (must be < blobSoftDeleteDays).')
@minValue(1)
@maxValue(365)
param blobRestoreDays int = 7

@description('Lifecycle: move broadcasts to Cool after this many days of no access. Set to 0 to disable.')
param broadcastTierToCoolDays int = 30

@description('Lifecycle: move broadcasts to Archive after this many days. Set to 0 to disable.')
param broadcastTierToArchiveDays int = 180

// Retention delete rules — audio and audit rules are PLACEHOLDERS pending ARN's
// exact figures (CC-010). 0 = keep forever (rule omitted) for those optional
// rules. The `published` blob index tag is stamped by the application: false at
// upload, true when the bulletin airs (CC-002). LLM call captures have an
// explicit diagnostic retention below.

@description('Delete aired bulletin audio (blob tag published=true) this many days after last modification. 0 = keep forever. Floor when set: 90 days (CC-002 legal minimum).')
@minValue(0)
param publishedAudioDeleteAfterDays int = 0

@description('Delete never-aired candidate audio (blob tag published=false) this many days after last modification. 0 = keep forever. Candidates are optional-keep per CC-002.')
@minValue(0)
param unpublishedAudioDeleteAfterDays int = 0

@description('Delete audit evidence snapshots (audit-exports/) this many days after last modification. 0 = keep forever. Set from the confirmed multi-year audit figure (CC-010), e.g. 1095+.')
@minValue(0)
param auditExportsDeleteAfterDays int = 0
@description('Delete LLM call captures after this many days. These captures are the diagnostic record for a bulletin; dev measured only a two-hour effective window, which prevented root-causing older jobs. Fourteen days covers a sprint of investigation while bounding storage cost.')
@minValue(1)
param llmCaptureRetentionDays int = 14


// ---------- Variables ----------

var containers containerConfig[] = [
  { name: 'deployment' }
  { name: 'broadcasts' }
  { name: 'synthesis-history' }
  { name: 'audit-exports' } // daily NDJSON snapshots of the radioJobs table (scripts + approval evidence; CC-002)
  { name: 'config-imports' } // pending/resolved config-bundle import envelopes (two-person prod approval evidence)
  { name: 'llm-call-captures' } // per-call LLM request/response records used to diagnose bulletin generation
]


var tables tableConfig[] = [
  { name: 'radioJobs' }
  { name: 'radioJobWorkIndex' }
  { name: 'jobReadIndex' }
  { name: 'jobTimeline' }
  { name: 'jobPayloads' }
  { name: 'jobLedger' }
  // Per-job structured generation progress document (PK Progress / RK jobId) plus the
  // per-market stage-duration EMA (PK Typical / RK market). Served by
  // GET /api/jobs/{jobId}/progress on a 2s console poll; see the 2026-09-02 decision.
  { name: 'radioJobProgress' }
  { name: 'radioSchedules' }
  { name: 'voiceCalibrations' }
  { name: 'radioIncidentState' }
  { name: 'priorityRoads' }
  { name: 'operatorAssignments' }
  // Station-scoped future lessons (operator-taught + inferred). Created lazily by
  // TableRepositoryBase since day one; the previously declared 'marketFeedback' name was
  // never read or written by any code. Renaming here does not delete existing tables.
  { name: 'futureLessons' }
  { name: 'synthesisHistory' }
  // Per-fetch deterministic-parser coverage and unknown-vocabulary ledger. Bounded by a 180-day
  // retention enforced on the write path (TableStorageSourceParseReportRepository), not by a timer.
  { name: 'sourceParseReports' }
  { name: 'activityLog' }
  { name: 'radioBookends' }
  { name: 'radioBulletinPlaybooks' }
  { name: 'radioDeliveryStyles' }
  { name: 'editorialSettings' }
  { name: 'eventVerdicts' }
  { name: 'fillerAudioCache' }
  { name: 'fillerStatements' }
  { name: 'guidance' }
  { name: 'incidentPriorities' }
  { name: 'mapAttributesCache' }
  { name: 'radioPronunciationDictionaries' }
  { name: 'spokenDurationSamples' }
  { name: 'verifiedPronunciations' }
  { name: 'radioVoiceCalibrations' }
  { name: 'radioVoiceProfiles' }
  { name: 'safetyTerms' }
  { name: 'safetyTermIndex' }
  { name: 'incidentLandscape' }
  { name: 'radioJobClaims' }
]

var queues queueConfig[] = [
  { name: 'broadcast-queue' }
  // Dead-letter target for the BroadcastPoisonQueue listener. Without it the
  // listener 404-polls every 60s until the host lazily creates the queue on
  // the first poison move (observed in sandbox validation 11 Jun 2026).
  { name: 'broadcast-queue-poison' }
  { name: 'regeneration-queue' }
  // Same reason as broadcast-queue-poison: RegenerationPoisonQueueTrigger listens
  // on this name, and QueueDepthMetricsTrigger reports its depth. Missing until
  // 2026-07-31, so the listener 404-polled and the depth metric had nothing to read.
  { name: 'regeneration-queue-poison' }
  { name: 'lesson-learning-queue' }
  // Lesson-learning failures are retained for inspection after explicit retry routing.
  // This queue is measured for health but is never an autoscale input.
  { name: 'lesson-learning-queue-poison' }
  { name: 'activity-audit' }
  { name: 'activity-audit-poison' }
  { name: 'delivery-queue' }
  { name: 'delivery-queue-poison' }
]

// ---------- Storage Account ----------

@description('Storage account for the ArnBulletinGenerator application.')
resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: skuName
  }
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    publicNetworkAccess: publicNetworkAccess
    networkAcls: {
      defaultAction: publicNetworkAccess == 'Disabled' ? 'Deny' : 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ---------- Blob Service & Containers ----------

@description('Blob service for the storage account. Soft delete, versioning, and change feed support point-in-time restore.')
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: blobSoftDeleteDays
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: blobSoftDeleteDays
    }
    isVersioningEnabled: enableBlobRestore
    changeFeed: enableBlobRestore ? {
      enabled: true
      retentionInDays: blobSoftDeleteDays
    } : { enabled: false }
    restorePolicy: enableBlobRestore ? {
      enabled: true
      days: blobRestoreDays
    } : null
  }
}

// Lifecycle management — tiering moves audio in `broadcasts/` through tiers
// automatically; optional audio/audit delete rules stay off (0) until ARN
// confirms retention figures. LLM call captures always receive their explicit
// diagnostic retention rule. Resource is skipped only when no rule is active.
// Archive tier is NOT supported on ZRS/GZRS accounts (only LRS/GRS/RA-GRS), so
// skip tierToArchive for zone-redundant SKUs (test/prod use ZRS) — the policy
// would otherwise fail with FeatureNotSupportedForAccount. Cool IS supported.
var archiveTierSupported = !contains(['Standard_ZRS', 'Standard_GZRS'], skuName)

var tieringRules = (broadcastTierToCoolDays > 0 || broadcastTierToArchiveDays > 0) ? [
  {
    name: 'broadcasts-tiering'
    enabled: true
    type: 'Lifecycle'
    definition: {
      filters: {
        blobTypes: ['blockBlob']
        prefixMatch: ['broadcasts/']
      }
      actions: {
        baseBlob: {
          tierToCool: broadcastTierToCoolDays > 0 ? {
            daysAfterModificationGreaterThan: broadcastTierToCoolDays
          } : null
          tierToArchive: (broadcastTierToArchiveDays > 0 && archiveTierSupported) ? {
            daysAfterModificationGreaterThan: broadcastTierToArchiveDays
          } : null
        }
      }
    }
  }
] : []

var publishedAudioDeleteRules = publishedAudioDeleteAfterDays > 0 ? [
  {
    name: 'broadcasts-published-delete'
    enabled: true
    type: 'Lifecycle'
    definition: {
      filters: {
        blobTypes: ['blockBlob']
        prefixMatch: ['broadcasts/']
        blobIndexMatch: [{ name: 'published', op: '==', value: 'true' }]
      }
      actions: {
        baseBlob: {
          delete: { daysAfterModificationGreaterThan: publishedAudioDeleteAfterDays }
        }
      }
    }
  }
] : []

var unpublishedAudioDeleteRules = unpublishedAudioDeleteAfterDays > 0 ? [
  {
    name: 'broadcasts-unpublished-delete'
    enabled: true
    type: 'Lifecycle'
    definition: {
      filters: {
        blobTypes: ['blockBlob']
        prefixMatch: ['broadcasts/']
        blobIndexMatch: [{ name: 'published', op: '==', value: 'false' }]
      }
      actions: {
        baseBlob: {
          delete: { daysAfterModificationGreaterThan: unpublishedAudioDeleteAfterDays }
        }
      }
    }
  }
] : []

var auditExportsDeleteRules = auditExportsDeleteAfterDays > 0 ? [
  {
    name: 'audit-exports-delete'
    enabled: true
    type: 'Lifecycle'
    definition: {
      filters: {
        blobTypes: ['blockBlob']
        prefixMatch: ['audit-exports/']
      }
      actions: {
        baseBlob: {
          delete: { daysAfterModificationGreaterThan: auditExportsDeleteAfterDays }
        }
      }
    }
  }
] : []
var llmCaptureDeleteRules = [
  {
    name: 'llm-call-captures-delete'
    enabled: true
    type: 'Lifecycle'
    definition: {
      filters: {
        blobTypes: ['blockBlob']
        prefixMatch: ['llm-call-captures/']
      }
      actions: {
        baseBlob: {
          delete: { daysAfterModificationGreaterThan: llmCaptureRetentionDays }
        }
      }
    }
  }
]


var lifecycleRules = concat(
  tieringRules,
  publishedAudioDeleteRules,
  unpublishedAudioDeleteRules,
  auditExportsDeleteRules,
  llmCaptureDeleteRules
)

resource lifecyclePolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2024-01-01' = if (length(lifecycleRules) > 0) {
  parent: storageAccount
  name: 'default'
  properties: {
    policy: {
      rules: lifecycleRules
    }
  }
}

@description('Blob containers for application data.')
resource blobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = [
  for container in containers: {
    parent: blobService
    name: container.name
    properties: {
      publicAccess: 'None'
    }
  }
]

// ---------- Table Service & Tables ----------

@description('Table service for the storage account.')
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2024-01-01' = {
  parent: storageAccount
  name: 'default'
}

@description('Storage tables for application state.')
resource storageTables 'Microsoft.Storage/storageAccounts/tableServices/tables@2024-01-01' = [
  for table in tables: {
    parent: tableService
    name: table.name
  }
]

// ---------- Queue Service & Queues ----------

@description('Queue service for the storage account.')
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2024-01-01' = {
  parent: storageAccount
  name: 'default'
}

@description('Storage queues for async processing.')
resource storageQueues 'Microsoft.Storage/storageAccounts/queueServices/queues@2024-01-01' = [
  for queue in queues: {
    parent: queueService
    name: queue.name
  }
]

// ---------- Diagnostic Settings ----------

@description('Streams storage account logs and metrics to Log Analytics.')
resource storageDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  name: 'storageDiagnostics'
  scope: storageAccount
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: []
    metrics: [
      {
        category: 'Transaction'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
      {
        category: 'Capacity'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
    ]
  }
}

@description('Streams blob data-plane access logs (read/write/delete) to Log Analytics. Scoped to the blob service — these categories are not available at the account level.')
resource blobDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  name: 'blobDiagnostics'
  scope: blobService
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'StorageRead'
        enabled: true
      }
      {
        category: 'StorageWrite'
        enabled: true
      }
      {
        category: 'StorageDelete'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'Transaction'
        enabled: true
      }
    ]
  }
}

// ============================================================================
// Backup Strategy
// ============================================================================
// Data in this storage account should be protected against accidental deletion
// or corruption. The following strategy is recommended:
//
// 1. Table Storage point-in-time restore — Enable soft delete and
//    point-in-time restore on the storage account to recover tables
//    (radioJobs, radioSchedules, voiceCalibrations, radioIncidentState)
//    within the retention window.
//
// 2. Azure Backup — Alternatively, configure Azure Backup for the storage
//    account via a Backup Vault. This provides centralized backup
//    policy management and longer retention.
//
// 3. Periodic export — Export schedules and voice calibration configuration
//    periodically to a durable location (e.g., the deployment container or
//    an external archival store) as a supplement to platform backups.
// ============================================================================

// ---------- Outputs ----------

@description('Name of the storage account.')
output name string = storageAccount.name

@description('Resource ID of the storage account.')
output id string = storageAccount.id
