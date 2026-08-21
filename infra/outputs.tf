// Consumed by the scripts in scripts/. W20 adds the gateway URL, subscription
// key, and Static Web App deployment token once those resources exist.

output "resource_group_name" {
  description = "Resource group holding every demo resource."
  value       = azurerm_resource_group.main.name
}

output "location" {
  description = "Azure region."
  value       = azurerm_resource_group.main.location
}

output "function_app_name" {
  description = "Function App to publish code to."
  value       = azurerm_function_app_flex_consumption.main.name
}

output "function_app_hostname" {
  description = "Default hostname of the Function App."
  value       = azurerm_function_app_flex_consumption.main.default_hostname
}

output "storage_account_name" {
  description = "Storage account backing the runtime and the demo tables."
  value       = azurerm_storage_account.main.name
}

output "servicebus_namespace" {
  description = "Service Bus namespace name."
  value       = azurerm_servicebus_namespace.main.name
}

output "servicebus_fqdn" {
  description = "Fully qualified Service Bus namespace, for direct queue injection in scenario 14.4."
  value       = "${azurerm_servicebus_namespace.main.name}.servicebus.windows.net"
}

output "application_insights_name" {
  description = "Application Insights instance collecting gateway and function telemetry."
  value       = azurerm_application_insights.main.name
}

output "log_analytics_workspace_name" {
  description = "Workspace backing Application Insights. Query demo/queries.kql here — see SPEC.md 10.1."
  value       = azurerm_log_analytics_workspace.main.name
}
