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

output "gateway_url" {
  description = "Base URL for the Orders API through API Management."
  value       = "${azurerm_api_management.main.gateway_url}/orders-demo"
}

output "subscription_key" {
  description = "Subscription key for the demo product. Send as Ocp-Apim-Subscription-Key."
  value       = azurerm_api_management_subscription.demo.primary_key
  sensitive   = true
}

output "static_web_app_name" {
  description = "Static Web App hosting the demo UI."
  value       = azurerm_static_web_app.main.name
}

output "static_web_app_url" {
  description = "Public URL of the demo UI."
  value       = "https://${azurerm_static_web_app.main.default_host_name}"
}

output "static_web_app_api_key" {
  description = "Deployment token for the Static Web App. Consumed by deploy-web.ps1 via an environment variable, never written to disk."
  value       = azurerm_static_web_app.main.api_key
  sensitive   = true
}
