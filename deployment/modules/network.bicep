// ============================================================================
// Module:  network.bicep
// Purpose: Self-contained private network for an IBP environment when ARN's
//          hub network is not (yet) wired in. Provisions a VNet with a
//          Function-integration subnet (delegated) + a private-endpoint subnet,
//          and the 7 privatelink private DNS zones (linked to the VNet) that
//          the private endpoints register A-records in.
//
//          Toggled by main.bicep's `createNetwork`. When false, main.bicep uses
//          the ARN-supplied subnet + DNS zone IDs instead (the hub model that
//          prod will use). This module lets Test stand up a faithful private
//          posture (private endpoints, data plane disabled) entirely self-served.
//
//          CIDR is deliberately an obscure /24 to minimise the chance of a
//          clash if ARN later peers this VNet to their hub — confirm with ARN
//          (Yong) before any hub peering.
// ============================================================================

@description('Azure region for the VNet and subnets.')
param location string

@description('Name of the virtual network.')
param vnetName string

@description('VNet address space (CIDR).')
param addressPrefix string = '10.61.0.0/24'

@description('Function App regional VNet-integration subnet (CIDR). Delegated to Microsoft.Web/serverFarms.')
param integrationSubnetPrefix string = '10.61.0.0/26'

@description('Private-endpoint subnet (CIDR). privateEndpointNetworkPolicies disabled so PE NICs can attach.')
param privateEndpointSubnetPrefix string = '10.61.0.64/27'

@description('Tags to apply to all resources.')
param tags object = {}

var integrationSubnetName = 'snet-integration'
var privateEndpointSubnetName = 'snet-pe'

// Standard Azure public-cloud privatelink zones for the services we expose.
var privateDnsZoneNames = [
  'privatelink.blob.core.windows.net'
  'privatelink.table.core.windows.net'
  'privatelink.queue.core.windows.net'
  'privatelink.vaultcore.azure.net'
  'privatelink.cognitiveservices.azure.com'
  // AI Foundry (AIServices kind) accounts answer on three hostnames; each
  // needs its own privatelink zone or in-VNet resolution NXDOMAINs (hit on
  // test 2026-07-17: *.services.ai.azure.com unresolvable from the app).
  'privatelink.openai.azure.com'
  'privatelink.services.ai.azure.com'
  // AzureFilesDrop playout delivery resolves the drop account's file endpoint
  // in-VNet (Files REST over OAuth, 443). Added 2026-07-30 when ARN confirmed
  // the shared drop share is also the production playout folder.
  'privatelink.file.core.windows.net'
]

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [addressPrefix]
    }
    subnets: [
      {
        name: integrationSubnetName
        properties: {
          addressPrefix: integrationSubnetPrefix
          delegations: [
            {
              name: 'webapp'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: privateEndpointSubnetName
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          // PE NICs cannot attach to a subnet with network policies enabled.
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource privateDnsZones 'Microsoft.Network/privateDnsZones@2020-06-01' = [
  for zoneName in privateDnsZoneNames: {
    name: zoneName
    location: 'global'
    tags: tags
  }
]

// Link every zone to the VNet so in-VNet resolvers (the Function App) resolve
// the privatelink records the PEs register. registrationEnabled stays false —
// these are privatelink resolution zones, not auto-registration zones.
resource dnsZoneLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = [
  for (zoneName, i) in privateDnsZoneNames: {
    parent: privateDnsZones[i]
    name: '${vnetName}-link'
    location: 'global'
    properties: {
      registrationEnabled: false
      virtualNetwork: {
        id: vnet.id
      }
    }
  }
]

@description('Resource ID of the Function-integration subnet.')
output integrationSubnetId string = '${vnet.id}/subnets/${integrationSubnetName}'

@description('Resource ID of the private-endpoint subnet.')
output privateEndpointSubnetId string = '${vnet.id}/subnets/${privateEndpointSubnetName}'

@description('Resource ID of privatelink.blob.core.windows.net zone.')
output blobDnsZoneId string = privateDnsZones[0].id

@description('Resource ID of privatelink.table.core.windows.net zone.')
output tableDnsZoneId string = privateDnsZones[1].id

@description('Resource ID of privatelink.queue.core.windows.net zone.')
output queueDnsZoneId string = privateDnsZones[2].id

@description('Resource ID of privatelink.vaultcore.azure.net zone.')
output keyVaultDnsZoneId string = privateDnsZones[3].id

@description('Resource ID of privatelink.cognitiveservices.azure.com zone.')
output cognitiveServicesDnsZoneId string = privateDnsZones[4].id

@description('Resource ID of privatelink.openai.azure.com zone.')
output openAiDnsZoneId string = privateDnsZones[5].id

@description('Resource ID of privatelink.services.ai.azure.com zone.')
output servicesAiDnsZoneId string = privateDnsZones[6].id

@description('Resource ID of privatelink.file.core.windows.net zone.')
output fileDnsZoneId string = privateDnsZones[7].id
