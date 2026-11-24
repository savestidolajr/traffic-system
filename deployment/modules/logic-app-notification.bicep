// ============================================================================
// Module:  logic-app-notification.bicep
// Purpose: Provision a Consumption Logic App that receives HTTP webhook posts
//          from the Function App and renders Adaptive Cards in a Teams channel.
//
//          The Teams API connection deploys in Unauthorized state. A human must
//          authorise it once per environment via the Azure Portal:
//            Portal → API Connections → teams-{env} → Edit API connection → Authorize
//
//          After authorisation, the connection persists across redeployments.
// ============================================================================

@description('Logic App name.')
param logicAppName string

@description('Azure region.')
param location string

@description('Tags to apply.')
param tags object = {}

@description('Teams group (team) ID to post notifications to.')
param teamsGroupId string

@description('Teams channel ID to post notifications to.')
param teamsChannelId string

@description('Base URL of the operator console (Static Web App) for deep links in the card.')
param opsConsoleBaseUrl string = ''

@description('Environment label shown on the Azure Monitor alert card (e.g. dev/test/prod), so one Teams channel can carry more than one environment without ambiguity.')
param environmentLabel string = ''

// ---- API Connection for Teams ----

var teamsConnectionName = '${logicAppName}-teams'

resource teamsConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: teamsConnectionName
  location: location
  tags: tags
  properties: {
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'teams')
    }
    displayName: 'Teams Notification Connection'
    // No parameterValues — Teams requires interactive OAuth consent.
    // The connection deploys in Unauthorized state. Authorise once via Portal.
  }
}

// NOTE: no connection access policy — Microsoft.Web/connections/accessPolicies
// applies only to V2 connections and FAILS on this V1 Consumption connection
// (InvalidApiConnectionAccessPolicy, verified live 2026-07-05). A Consumption
// Logic App in the same RG uses the connection directly via its $connections
// parameter; authorisation is the one-time interactive OAuth consent above.

// ---- Logic App ----

resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: logicAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    state: 'Enabled'
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      parameters: {
        '$connections': {
          defaultValue: {}
          type: 'Object'
        }
      }
      triggers: {
        'When_a_notification_is_received': {
          type: 'Request'
          kind: 'Http'
          inputs: {
            schema: {
              type: 'object'
              properties: {
                title: { type: 'string' }
                jobId: { type: 'string' }
                market: { type: 'string' }
                station: { type: 'string' }
                status: { type: 'string' }
                scheduledAtDisplay: { type: 'string' }
                previewText: { type: 'string' }
                createdBy: { type: 'string' }
                createdAt: { type: 'string' }
                schemaVersion: { type: 'string' }
                correlationId: { type: 'string' }
                url: { type: 'string' }
                allowedActions: {
                  type: 'array'
                  items: { type: 'string' }
                }
              }
            }
          }
        }
      }
      actions: {
        // Two payload families arrive on this one trigger:
        //   1. Azure Monitor action-group webhooks (useCommonAlertSchema: true),
        //      identified by the top-level schemaId. Rendered as an alert card.
        //   2. Everything else — the Function App's own notifications.
        // The alert test runs first so an alert can never be mistaken for a job
        // card (an alert payload has no `card`, so it would otherwise fall into
        // the legacy composed-template branch, where Compose_card calls
        // formatDateTime on a null createdAt and fails the whole run).
        //
        // Discriminate on schemaId, NOT on data.essentials.alertRule: Azure's own
        // action-group test notification (createNotifications) sends the full
        // common alert schema with alertRule set to NULL, so an alertRule test
        // routes every test payload down the job-card branch and fails. Verified
        // live on dev 2026-07-31. schemaId is always present and constant.
        Is_azure_monitor_alert: {
          type: 'If'
          runAfter: {}
          expression: {
            equals: ['@coalesce(triggerBody()?[\'schemaId\'], \'\')', 'azureMonitorCommonAlertSchema']
          }
          actions: {
            Compose_alert_card: {
              type: 'Compose'
              runAfter: {}
              inputs: alertCardTemplate
            }
            'Post_alert_card': {
              type: 'ApiConnection'
              runAfter: {
                Compose_alert_card: ['Succeeded']
              }
              inputs: {
                host: {
                  connection: {
                    name: '@parameters(\'$connections\')[\'teams\'][\'connectionId\']'
                  }
                }
                method: 'post'
                path: '/v1.0/teams/conversation/adaptivecard/poster/Flow bot/location/@{encodeURIComponent(\'Channel\')}'
                body: {
                  messageBody: '@string(outputs(\'Compose_alert_card\'))'
                  recipient: {
                    groupId: teamsGroupId
                    channelId: teamsChannelId
                  }
                }
              }
            }
          }
          'else': {
            actions: {
              // Schema 3.0 (2026-07-05): the Function App builds the full Adaptive Card
              // in C# (TeamsCardBuilder, unit-tested) and sends it as `card`. The Logic
              // App is dumb transport for those payloads. Legacy senders (ADO CI webhook,
              // schema 2.0) omit `card` and fall through to the composed template below.
              Has_card: {
                type: 'If'
                runAfter: {}
                expression: {
                  equals: ['@empty(triggerBody()?[\'card\'])', false]
                }
                actions: {
                  'Post_card_passthrough': {
                    type: 'ApiConnection'
                    runAfter: {}
                    inputs: {
                      host: {
                        connection: {
                          name: '@parameters(\'$connections\')[\'teams\'][\'connectionId\']'
                        }
                      }
                      method: 'post'
                      path: '/v1.0/teams/conversation/adaptivecard/poster/Flow bot/location/@{encodeURIComponent(\'Channel\')}'
                      body: {
                        messageBody: '@string(triggerBody()[\'card\'])'
                        recipient: {
                          groupId: teamsGroupId
                          channelId: teamsChannelId
                        }
                      }
                    }
                  }
                }
                'else': {
                  actions: {
                    // Build the card as an OBJECT first. Values substituted here are
                    // JSON-escaped when string() serialises the composed object — a
                    // previewText containing quotes/newlines (e.g. an embedded provider
                    // error) must NOT break the card. The previous design interpolated
                    // into a pre-serialised JSON *string* and failed with
                    // InvalidBotRequestMessageBody on the first real failure payload
                    // (verified live 2026-07-05).
                    Compose_card: {
                      type: 'Compose'
                      runAfter: {}
                      inputs: adaptiveCardTemplate
                    }
                    'Post_card_in_a_chat_or_channel': {
                      type: 'ApiConnection'
                      runAfter: {
                        Compose_card: ['Succeeded']
                      }
                      inputs: {
                        host: {
                          connection: {
                            name: '@parameters(\'$connections\')[\'teams\'][\'connectionId\']'
                          }
                        }
                        method: 'post'
                        path: '/v1.0/teams/conversation/adaptivecard/poster/Flow bot/location/@{encodeURIComponent(\'Channel\')}'
                        body: {
                          messageBody: '@string(outputs(\'Compose_card\'))'
                          recipient: {
                            groupId: teamsGroupId
                            channelId: teamsChannelId
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
      }
      outputs: {}
    }
    parameters: {
      '$connections': {
        value: {
          teams: {
            connectionId: teamsConnection.id
            connectionName: teamsConnectionName
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'teams')
          }
        }
      }
    }
  }
}

// ---- Azure Monitor alert card template ----
// Rendered for action-group webhook posts using the common alert schema
// (useCommonAlertSchema: true, set in action-group.bicep). Same OBJECT-not-string
// discipline as the job card below: an alert description can carry quotes and
// newlines, and string() must do the escaping.
//
// Shape consumed (azureMonitorCommonAlertSchema):
//   data.essentials.alertRule / severity / monitorCondition / signalType
//   data.essentials.firedDateTime / description / alertTargetIDs
// monitorCondition is 'Fired' or 'Resolved' — the card colours and titles on it,
// so a resolution reads as good news rather than a second alarm.

var alertCardTemplate = {
  type: 'AdaptiveCard'
  '$schema': 'http://adaptivecards.io/schemas/adaptive-card.json'
  version: '1.4'
  body: [
    {
      type: 'Container'
      style: '@{if(equals(triggerBody()?[\'data\']?[\'essentials\']?[\'monitorCondition\'], \'Resolved\'), \'good\', \'attention\')}'
      bleed: true
      spacing: 'None'
      padding: 'Default'
      items: [
        {
          type: 'TextBlock'
          text: '@{if(equals(triggerBody()?[\'data\']?[\'essentials\']?[\'monitorCondition\'], \'Resolved\'), \'RESOLVED\', \'ALERT FIRED\')} \u{00B7} @{coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'severity\'], \'Sev?\')}'
          size: 'Small'
          weight: 'Bolder'
          isSubtle: true
          spacing: 'None'
        }
        {
          type: 'TextBlock'
          text: '@{coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'alertRule\'], \'(unnamed alert rule)\')}'
          size: 'Large'
          weight: 'Bolder'
          wrap: true
          spacing: 'None'
        }
      ]
    }
    {
      type: 'FactSet'
      spacing: 'Medium'
      facts: [
        {
          title: 'Environment'
          value: environmentLabel
        }
        {
          title: 'Condition'
          value: '@{coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'monitorCondition\'], \'Unknown\')}'
        }
        {
          title: 'Signal'
          value: '@{coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'signalType\'], \'Unknown\')}'
        }
        {
          title: 'Fired (UTC)'
          value: '@{coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'firedDateTime\'], \'\')}'
        }
      ]
    }
    {
      type: 'Container'
      separator: true
      spacing: 'Medium'
      items: [
        {
          type: 'TextBlock'
          // Most Arn rules carry no description; hide the block rather than show a blank line.
          text: '@{coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'description\'], \'\')}'
          wrap: true
          maxLines: 8
          isVisible: '@{not(empty(coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'description\'], \'\')))}'
        }
        {
          type: 'TextBlock'
          // json('[]'), not createArray(): Logic Apps rejects createArray with no
          // arguments outright (verified live on dev 2026-07-31).
          text: '@{join(coalesce(triggerBody()?[\'data\']?[\'essentials\']?[\'alertTargetIDs\'], json(\'[]\')), \', \')}'
          wrap: true
          size: 'Small'
          isSubtle: true
        }
      ]
    }
  ]
  // Action.OpenUrl rejects an empty url and takes the whole card down with it,
  // so an environment with no console URL simply gets a card with no button.
  actions: empty(opsConsoleBaseUrl) ? [] : [
    {
      type: 'Action.OpenUrl'
      title: 'Open operator console'
      url: opsConsoleBaseUrl
    }
  ]
}

// ---- Adaptive Card template ----
// The card rendered in Teams when a notification fires — defined as a Bicep
// OBJECT (fed to a Compose action) so every @{...} substitution lands as a
// JSON *value* and is escaped by string() at serialisation time. NEVER
// convert this back to a pre-serialised string template: raw interpolation
// broke on the first payload containing quotes/newlines
// (InvalidBotRequestMessageBody, verified live 2026-07-05).

var adaptiveCardTemplate = {
  type: 'AdaptiveCard'
  '$schema': 'http://adaptivecards.io/schemas/adaptive-card.json'
  version: '1.4'
  body: [
    {
      type: 'Container'
      style: 'emphasis'
      bleed: true
      items: [
        {
          type: 'TextBlock'
          text: '@{triggerBody()?[\'title\']}'
          size: 'Large'
          weight: 'Bolder'
          wrap: true
          color: 'Default'
        }
      ]
      spacing: 'None'
      padding: 'Default'
    }
    {
      type: 'ColumnSet'
      spacing: 'Medium'
      columns: [
        {
          type: 'Column'
          width: 'stretch'
          items: [
            {
              type: 'TextBlock'
              text: 'MARKET'
              size: 'Small'
              weight: 'Bolder'
              isSubtle: true
              spacing: 'None'
            }
            {
              type: 'TextBlock'
              text: '@{triggerBody()?[\'market\']}'
              size: 'Medium'
              weight: 'Bolder'
              spacing: 'None'
            }
          ]
        }
        {
          type: 'Column'
          width: 'stretch'
          items: [
            {
              type: 'TextBlock'
              text: 'STATION'
              size: 'Small'
              weight: 'Bolder'
              isSubtle: true
              spacing: 'None'
            }
            {
              type: 'TextBlock'
              text: '@{triggerBody()?[\'station\']}'
              size: 'Medium'
              weight: 'Bolder'
              spacing: 'None'
            }
          ]
        }
        {
          type: 'Column'
          width: 'stretch'
          items: [
            {
              type: 'TextBlock'
              text: 'STATUS'
              size: 'Small'
              weight: 'Bolder'
              isSubtle: true
              spacing: 'None'
            }
            {
              type: 'TextBlock'
              text: '@{triggerBody()?[\'status\']}'
              size: 'Medium'
              weight: 'Bolder'
              color: 'Accent'
              spacing: 'None'
            }
          ]
        }
      ]
    }
    {
      type: 'ColumnSet'
      spacing: 'Small'
      columns: [
        {
          type: 'Column'
          width: 'auto'
          items: [
            {
              type: 'TextBlock'
              text: '\u{1F550}'
              spacing: 'None'
            }
          ]
          verticalContentAlignment: 'Center'
        }
        {
          type: 'Column'
          width: 'stretch'
          items: [
            {
              type: 'TextBlock'
              text: '@{triggerBody()?[\'scheduledAtDisplay\']}'
              isSubtle: true
              spacing: 'None'
            }
          ]
          verticalContentAlignment: 'Center'
        }
        {
          type: 'Column'
          width: 'auto'
          items: [
            {
              type: 'TextBlock'
              text: '\u{1F517} @{triggerBody()?[\'correlationId\']}'
              isSubtle: true
              size: 'Small'
              spacing: 'None'
            }
          ]
          verticalContentAlignment: 'Center'
        }
      ]
    }
    {
      type: 'Container'
      style: 'default'
      separator: true
      spacing: 'Medium'
      items: [
        {
          type: 'TextBlock'
          text: '@{triggerBody()?[\'previewText\']}'
          wrap: true
          maxLines: 10
          size: 'Default'
        }
      ]
    }
    {
      type: 'TextBlock'
      text: '@{triggerBody()?[\'createdBy\']} \u{00B7} @{formatDateTime(triggerBody()?[\'createdAt\'], \'dd MMM yyyy HH:mm\')} UTC'
      size: 'Small'
      isSubtle: true
      separator: true
      spacing: 'Medium'
      horizontalAlignment: 'Right'
    }
  ]
  actions: [
    {
      type: 'Action.OpenUrl'
      title: 'Open in Console'
      url: '@{triggerBody()?[\'url\']}'
      style: 'positive'
    }
  ]
}

// ---- Outputs ----
@description('Logic App trigger invoke URL (the POST endpoint the Function App webhook calls). Stored in Key Vault as webhookEndpointUrl. NOTE: must be the TRIGGER-scoped callback — the workflow-level listCallbackUrl() returns a base URL that 404s on POST (found live 2026-07-05).')
output triggerUrl string = listCallbackUrl('${logicApp.id}/triggers/When_a_notification_is_received', '2019-05-01').value

@description('Logic App resource ID.')
output logicAppId string = logicApp.id

@description('Logic App name.')
output logicAppName string = logicApp.name

@description('Teams API connection name. Authorise once per environment (Portal, or listConsentLinks + confirmConsentCode — see docs/operations/notifications.md).')
output teamsConnectionName string = teamsConnection.name
