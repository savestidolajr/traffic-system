// ============================================================================
// Module:  action-group.bicep
// Purpose: Provision the Azure Monitor action group used by alert rules.
//          Receivers are driven by params so each environment can route to
//          the right inbox / phone / webhook.
// ============================================================================

@description('Name of the action group.')
param actionGroupName string

@description('Short name (max 12 chars) shown in alert notifications.')
@maxLength(12)
param shortName string

@description('Email receivers. Each entry: { name, emailAddress, useCommonAlertSchema }.')
param emailReceivers array = []

@description('SMS receivers. Each entry: { name, countryCode, phoneNumber }.')
param smsReceivers array = []

@description('Webhook receivers. Each entry: { name, serviceUri, useCommonAlertSchema }.')
param webhookReceivers array = []

@description('Tags to apply.')
param tags object = {}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: actionGroupName
  // Action groups live at the Global geography in nearly all regions.
  location: 'global'
  tags: tags
  properties: {
    groupShortName: shortName
    enabled: true
    emailReceivers: [for r in emailReceivers: {
      name: r.name
      emailAddress: r.emailAddress
      useCommonAlertSchema: contains(r, 'useCommonAlertSchema') ? r.useCommonAlertSchema : true
    }]
    smsReceivers: [for r in smsReceivers: {
      name: r.name
      countryCode: r.countryCode
      phoneNumber: r.phoneNumber
    }]
    webhookReceivers: [for r in webhookReceivers: {
      name: r.name
      serviceUri: r.serviceUri
      useCommonAlertSchema: contains(r, 'useCommonAlertSchema') ? r.useCommonAlertSchema : true
    }]
  }
}

output id string = actionGroup.id
output name string = actionGroup.name
