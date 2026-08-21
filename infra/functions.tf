// Flex Consumption is not planless — the Function App's service_plan_id is a
// required argument, so an FC1 plan is mandatory. See SPEC.md 17.1.
resource "azurerm_service_plan" "main" {
  name                = local.names.service_plan
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  os_type             = "Linux"
  sku_name            = "FC1"

  tags = local.tags
}

resource "azurerm_function_app_flex_consumption" "main" {
  name                = local.names.function_app
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  service_plan_id     = azurerm_service_plan.main.id

  // Targeting 10 rather than the platform default of 8, whose Functions
  // support ends 2026-11-10. See SPEC.md 17.1.
  runtime_name    = "dotnet-isolated"
  runtime_version = "10"

  // Deployment packages land in the dedicated container from W06, reached
  // with the app's own identity rather than a storage key — this is what
  // keeps the no-secrets position in SPEC.md 9.2 true for this hop too.
  storage_container_type      = "blobContainer"
  storage_container_endpoint  = "${azurerm_storage_account.main.primary_blob_endpoint}${azurerm_storage_container.deployments.name}"
  storage_authentication_type = "SystemAssignedIdentity"

  maximum_instance_count = 40
  instance_memory_in_mb  = 2048

  https_only = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    application_insights_connection_string = azurerm_application_insights.main.connection_string
  }

  app_settings = {
    // Identity-based connections. Supplying only the namespace and account
    // name — with no key or connection string — makes the host resolve
    // credentials through the system-assigned identity. The role
    // assignments that make these work arrive in W09.
    "ServiceBusConnection__fullyQualifiedNamespace" = "${azurerm_servicebus_namespace.main.name}.servicebus.windows.net"
    "AzureWebJobsStorage__accountName"              = azurerm_storage_account.main.name

    // Read by application code rather than the host. AzureWebJobsStorage is
    // reserved for the runtime, so the table repository gets its own setting
    // instead of borrowing it.
    "STORAGE_ACCOUNT_NAME" = azurerm_storage_account.main.name

    "ORDERS_QUEUE"       = local.entities.queue
    "ORDER_EVENTS_TOPIC" = local.entities.topic
    "TABLE_ORDERS"       = local.entities.table_orders
    "TABLE_AUDIT"        = local.entities.table_audit

    // Raise before a demo so queue depth and scale-out are visible (14.6).
    "PROCESSING_DELAY_MS" = tostring(var.processing_delay_ms)
  }

  tags = local.tags
}
