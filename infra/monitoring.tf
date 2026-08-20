resource "azurerm_log_analytics_workspace" "main" {
  name                = local.names.log_analytics
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = local.tags
}

// Workspace-based, so telemetry lands in the workspace above and the
// classic-mode retirement does not apply. Both APIM and the Function App
// point at this instance, which is what makes the end-to-end transaction
// view in SPEC.md 10 possible.
resource "azurerm_application_insights" "main" {
  name                = local.names.app_insights
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"

  tags = local.tags
}
