// Data-plane access for the Function App's system-assigned identity. These
// five assignments are what let SPEC.md 9.2 hold: no connection strings for
// Service Bus or Storage anywhere in the app configuration.
//
// Creating them requires User Access Administrator or Owner on the
// subscription. Contributor alone silently passes every earlier check and
// then fails here — which is what W01 exists to catch.

locals {
  function_principal_id = azurerm_function_app_flex_consumption.main.identity[0].principal_id
}

// --- Service Bus -----------------------------------------------------------

// Sender: SubmitOrder enqueues to orders, ProcessOrder publishes to
// order-events, ReplayDeadLetter resubmits drained messages.
resource "azurerm_role_assignment" "sb_sender" {
  scope                = azurerm_servicebus_namespace.main.id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = local.function_principal_id
}

// Receiver: the queue and subscription triggers, and the DLQ receive in
// ReplayDeadLetter.
resource "azurerm_role_assignment" "sb_receiver" {
  scope                = azurerm_servicebus_namespace.main.id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = local.function_principal_id
}

// --- Storage ---------------------------------------------------------------

// Blob Data Owner covers both the runtime's own host state and the
// deployment package container. Owner rather than Contributor because the
// host manages blob leases for singleton locks.
resource "azurerm_role_assignment" "storage_blob" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = local.function_principal_id
}

// Runtime queues used internally by the Functions host, not by demo code.
resource "azurerm_role_assignment" "storage_queue" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = local.function_principal_id
}

// The Orders and AuditLog tables.
resource "azurerm_role_assignment" "storage_table" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = local.function_principal_id
}

// --- Propagation -----------------------------------------------------------

// Role assignments are eventually consistent. Without a pause the first
// invocation after an apply can fail with 403 for no obvious reason, which
// is a miserable thing to debug during a demo. Sixty seconds is empirical,
// not a guarantee — SPEC.md 17 risk 4.
resource "time_sleep" "rbac_propagation" {
  create_duration = "60s"

  depends_on = [
    azurerm_role_assignment.sb_sender,
    azurerm_role_assignment.sb_receiver,
    azurerm_role_assignment.storage_blob,
    azurerm_role_assignment.storage_queue,
    azurerm_role_assignment.storage_table,
  ]
}
