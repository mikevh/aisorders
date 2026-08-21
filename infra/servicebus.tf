// Standard is the minimum tier that supports topics, which the fan-out in
// SPEC.md 6.2 needs. It carries a fixed monthly base charge and is the only
// meaningful line item in this demo's cost.
resource "azurerm_servicebus_namespace" "main" {
  name                = local.names.servicebus_namespace
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Standard"

  minimum_tls_version = "1.2"

  tags = local.tags
}

// max_delivery_count drives demo scenario 14.3: five attempts, then the
// message dead-letters. Lower it if the retry sequence is too slow to show
// on screen.
resource "azurerm_servicebus_queue" "orders" {
  name         = local.entities.queue
  namespace_id = azurerm_servicebus_namespace.main.id

  max_delivery_count                   = 5
  lock_duration                        = "PT1M"
  dead_lettering_on_message_expiration = true
}

// Routes Service Bus platform metrics into the same workspace App Insights
// writes to, so dead-letter queue depth is queryable in KQL alongside the
// application telemetry (SPEC.md 10).
//
// Without this the DeadletteredMessages metric exists only in the portal's
// Metrics explorer and cannot be joined to anything — which would force a
// blade switch mid-demo.
//
// Metrics only. The available log categories (OperationalLogs and friends)
// cover management-plane operations, not per-message activity, so they add
// ingestion cost without adding anything the demo shows.
resource "azurerm_monitor_diagnostic_setting" "servicebus" {
  name                       = "metrics-to-log-analytics"
  target_resource_id         = azurerm_servicebus_namespace.main.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  enabled_metric {
    category = "AllMetrics"
  }
}

// --- Fan-out ----------------------------------------------------------------

resource "azurerm_servicebus_topic" "order_events" {
  name         = local.entities.topic
  namespace_id = azurerm_servicebus_namespace.main.id
}

// Filtered subscriber. The threshold is what makes filtering visible on stage:
// a small order reaches only the audit subscriber, a large one reaches both.
resource "azurerm_servicebus_subscription" "notifications" {
  name               = local.entities.subscription_notifications
  topic_id           = azurerm_servicebus_topic.order_events.id
  max_delivery_count = 5
}

// Service Bus creates every subscription with a catch-all rule named $Default.
// It cannot be replaced in place: creating a rule of that name fails with
// "already exists - to be managed via Terraform this resource needs to be
// imported". Importing would fix this machine and break a clean rebuild, since
// a fresh apply hits the identical conflict.
//
// Leaving it alone is not an option either. Rules on a subscription are OR-ed,
// so a TrueFilter sitting beside the SQL filter would match every message and
// the fan-out demo in SPEC.md 14.2 would show both subscribers receiving
// everything.
//
// So the default rule is deleted first, then the real filter is created under
// its own name. az is already a documented prerequisite.
resource "terraform_data" "remove_default_notifications_rule" {
  triggers_replace = [azurerm_servicebus_subscription.notifications.id]

  provisioner "local-exec" {
    interpreter = ["pwsh", "-NoProfile", "-Command"]
    command     = <<-CMD
      az servicebus topic subscription rule delete `
        --resource-group '${azurerm_resource_group.main.name}' `
        --namespace-name '${azurerm_servicebus_namespace.main.name}' `
        --topic-name '${azurerm_servicebus_topic.order_events.name}' `
        --subscription-name '${azurerm_servicebus_subscription.notifications.name}' `
        --name '$Default' --output none
      if ($LASTEXITCODE -ne 0) { Write-Host 'default rule already absent' }
      exit 0
    CMD
  }
}

resource "azurerm_servicebus_subscription_rule" "notifications_filter" {
  name            = "high-value-completed"
  subscription_id = azurerm_servicebus_subscription.notifications.id
  filter_type     = "SqlFilter"

  // Reads message application properties, never the body - which is why
  // OrderMessaging sets eventType and orderTotal as properties too.
  sql_filter = "eventType = 'OrderCompleted' AND orderTotal > ${var.notification_threshold}"

  depends_on = [terraform_data.remove_default_notifications_rule]
}

// Catch-all subscriber. Keeps the default rule as created.
resource "azurerm_servicebus_subscription" "audit" {
  name               = local.entities.subscription_audit
  topic_id           = azurerm_servicebus_topic.order_events.id
  max_delivery_count = 5
}
