// ============================================================================
// Module:  private-endpoints.bicep
// Purpose: Provision private endpoints for Storage (blob/table/queue),
//          Key Vault, and AI Services. Optional private DNS zone wiring.
//
//          Deployed into the network RG (IBP-AUE-{ENV}-RSG-NTW01) per ARN's
//          naming CSV — target services live in other RGs, so targets are
//          passed by resource ID.
//
// Naming:  ARN convention IBP-AUE-{ENV}-PEP-APP## (instance number per PE):
//            {namePrefix}01 — storage blob
//            {namePrefix}02 — storage table
//            {namePrefix}03 — storage queue
//            {namePrefix}04 — key vault
//            {namePrefix}05 — ai services
//            {namePrefix}06 — delivery drop storage blob (optional)
//            {namePrefix}07 — delivery drop storage file (optional)
//
// Inputs:
//   - subnetId:      target subnet for the private endpoint NICs
//                    (must have privateEndpointNetworkPolicies = Disabled)
//   - <service>Id:   resource IDs of the existing services to attach to
//   - *DnsZoneId:    optional. When supplied, a privateDnsZoneGroup is
//                    created and the PE registers an A record there.
//                    When empty, DNS wiring must be added out-of-band
//                    (ARN's privatelink zones are hub-managed — pending Yong).
//
// Notes:
//   - Each storage subresource (blob/table/queue) requires its own PE.
//   - Private DNS zones are typically pre-provisioned in a hub VNet RG and
//     shared across workloads. Reference them by full resource ID.
// ============================================================================

@description('Azure region for the private endpoints.')
param location string

@description('Name prefix for the private endpoints, e.g. "IBP-AUE-PRD-PEP-APP". Instance numbers 01-05 are appended.')
param namePrefix string

@description('Resource ID of the subnet that hosts the private endpoint NICs.')
param subnetId string

@description('Resource ID of the storage account to expose privately.')
param storageAccountId string

@description('Resource ID of the Key Vault to expose privately.')
param keyVaultId string

@description('Resource ID of the AI Services account to expose privately.')
param aiServicesId string

@description('Resource ID of the SFTP-enabled delivery drop storage account (BlobDrop delivery transport). Empty skips the endpoint. Blob subresource only — the app writes bulletin blobs; the playout side pulls over the account\'s public SFTP endpoint (firewall IP rules).')
param deliveryDropStorageAccountId string = ''

@description('Resource ID of privatelink.file.core.windows.net zone, for the delivery drop file share (AzureFilesDrop transport). Empty skips the file endpoint entirely — without in-VNet DNS the PE would resolve nowhere useful.')
param deliveryDropFileDnsZoneId string = ''

@description('Resource ID of privatelink.blob.core.windows.net zone. Leave empty to skip DNS wiring.')
param blobDnsZoneId string = ''

@description('Resource ID of privatelink.table.core.windows.net zone. Leave empty to skip DNS wiring.')
param tableDnsZoneId string = ''

@description('Resource ID of privatelink.queue.core.windows.net zone. Leave empty to skip DNS wiring.')
param queueDnsZoneId string = ''

@description('Resource ID of privatelink.vaultcore.azure.net zone. Leave empty to skip DNS wiring.')
param keyVaultDnsZoneId string = ''

@description('Resource ID of privatelink.cognitiveservices.azure.com zone. Leave empty to skip DNS wiring.')
param cognitiveServicesDnsZoneId string = ''

@description('Resource ID of privatelink.openai.azure.com zone. Leave empty to skip DNS wiring.')
param openAiDnsZoneId string = ''

@description('Resource ID of privatelink.services.ai.azure.com zone. AI Foundry (AIServices) accounts answer on *.services.ai.azure.com — without this zone config the in-VNet app cannot resolve the Foundry agent endpoint. Leave empty to skip DNS wiring.')
param servicesAiDnsZoneId string = ''

@description('Tags to apply to all resources.')
param tags object = {}

// ============================================================================
// Storage private endpoints (one per subresource)
// ============================================================================

var storageSubResources = [
  {
    name: 'blob'
    instance: '01'
    dnsZoneId: blobDnsZoneId
  }
  {
    name: 'table'
    instance: '02'
    dnsZoneId: tableDnsZoneId
  }
  {
    name: 'queue'
    instance: '03'
    dnsZoneId: queueDnsZoneId
  }
]

resource storagePrivateEndpoints 'Microsoft.Network/privateEndpoints@2023-11-01' = [
  for sr in storageSubResources: {
    name: '${namePrefix}${sr.instance}'
    location: location
    tags: tags
    properties: {
      subnet: {
        id: subnetId
      }
      privateLinkServiceConnections: [
        {
          name: '${sr.name}-connection'
          properties: {
            privateLinkServiceId: storageAccountId
            groupIds: [
              sr.name
            ]
          }
        }
      ]
    }
  }
]

resource storagePrivateDnsZoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = [
  for (sr, i) in storageSubResources: if (!empty(sr.dnsZoneId)) {
    parent: storagePrivateEndpoints[i]
    name: 'default'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: '${sr.name}-config'
          properties: {
            privateDnsZoneId: sr.dnsZoneId
          }
        }
      ]
    }
  }
]

// ============================================================================
// Key Vault private endpoint
// ============================================================================

resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: '${namePrefix}04'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'vault-connection'
        properties: {
          privateLinkServiceId: keyVaultId
          groupIds: [
            'vault'
          ]
        }
      }
    ]
  }
}

resource keyVaultPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (!empty(keyVaultDnsZoneId)) {
  parent: keyVaultPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'vault-config'
        properties: {
          privateDnsZoneId: keyVaultDnsZoneId
        }
      }
    ]
  }
}

// ============================================================================
// AI Services private endpoint
// ============================================================================

resource aiServicesPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: '${namePrefix}05'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'account-connection'
        properties: {
          privateLinkServiceId: aiServicesId
          groupIds: [
            'account'
          ]
        }
      }
    ]
  }
}

// AI Foundry (AIServices kind) accounts answer on three hostname families
// (*.cognitiveservices.azure.com, *.openai.azure.com, *.services.ai.azure.com);
// the zone group must register into every configured zone or in-VNet DNS
// NXDOMAINs the unregistered hostnames (the Foundry agent endpoint uses
// *.services.ai.azure.com — hit on test 2026-07-17).
var aiServicesDnsConfigs = concat(
  empty(cognitiveServicesDnsZoneId)
    ? []
    : [{ name: 'account-config', properties: { privateDnsZoneId: cognitiveServicesDnsZoneId } }],
  empty(openAiDnsZoneId)
    ? []
    : [{ name: 'openai-config', properties: { privateDnsZoneId: openAiDnsZoneId } }],
  empty(servicesAiDnsZoneId)
    ? []
    : [{ name: 'services-ai-config', properties: { privateDnsZoneId: servicesAiDnsZoneId } }]
)

resource aiServicesPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (!empty(aiServicesDnsConfigs)) {
  parent: aiServicesPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: aiServicesDnsConfigs
  }
}

// ============================================================================
// Delivery drop storage private endpoint (blob only, optional)
// ============================================================================
// The Function App writes approved bulletins into the SFTP-enabled drop
// account as blobs. The integration subnet routes only RFC1918 traffic
// (vnetRouteAllEnabled=false), so without this PE the write would leave via
// the public path and be denied by the drop account's firewall.

resource deliveryDropBlobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = if (!empty(deliveryDropStorageAccountId)) {
  name: '${namePrefix}06'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'delivery-drop-blob-connection'
        properties: {
          privateLinkServiceId: deliveryDropStorageAccountId
          groupIds: [
            'blob'
          ]
        }
      }
    ]
  }
}

resource deliveryDropBlobDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (!empty(deliveryDropStorageAccountId) && !empty(blobDnsZoneId)) {
  parent: deliveryDropBlobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'delivery-drop-blob-config'
        properties: {
          privateDnsZoneId: blobDnsZoneId
        }
      }
    ]
  }
}

// ============================================================================
// Delivery drop storage private endpoint (file share, optional)
// ============================================================================
// AzureFilesDrop writes the approved bulletin to an Azure Files share using the
// Files REST API over OAuth (port 443, NOT SMB/445). With vnetRouteAllEnabled
// the write leaves through the integration subnet, so it needs a file-subresource
// PE plus privatelink.file DNS in THIS VNet — the account's firewall denies the
// public path. Test reaches ARN's hand-built endpoints in the test VNet; prod
// needs its own pair because prod's VNet has no peering to test (30 Jul 2026).

resource deliveryDropFilePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' =
  if (!empty(deliveryDropStorageAccountId) && !empty(deliveryDropFileDnsZoneId)) {
    name: '${namePrefix}07'
    location: location
    tags: tags
    properties: {
      subnet: {
        id: subnetId
      }
      privateLinkServiceConnections: [
        {
          name: 'delivery-drop-file-connection'
          properties: {
            privateLinkServiceId: deliveryDropStorageAccountId
            groupIds: [
              'file'
            ]
          }
        }
      ]
    }
  }

resource deliveryDropFileDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' =
  if (!empty(deliveryDropStorageAccountId) && !empty(deliveryDropFileDnsZoneId)) {
    parent: deliveryDropFilePrivateEndpoint
    name: 'default'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: 'delivery-drop-file-config'
          properties: {
            privateDnsZoneId: deliveryDropFileDnsZoneId
          }
        }
      ]
    }
  }
