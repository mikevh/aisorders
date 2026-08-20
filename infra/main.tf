// Suffix for globally-unique resource names. Kept short so the storage
// account name (2 + project_name + 5) stays inside the 24-character limit.
resource "random_string" "suffix" {
  length  = 5
  lower   = true
  upper   = false
  numeric = true
  special = false
}

locals {
  // Short region codes for the resource group name. Falls back to the full
  // region name for anything unmapped, which is ugly but never wrong.
  location_short_codes = {
    westus      = "wus"
    westus2     = "wus2"
    westus3     = "wus3"
    eastus      = "eus"
    eastus2     = "eus2"
    centralus   = "cus"
    westeurope  = "weu"
    northeurope = "neu"
  }

  location_short = lookup(local.location_short_codes, var.location, var.location)
  suffix         = random_string.suffix.result

  // Every resource name in one place, so the naming convention is checkable
  // at a glance and later files never build names ad hoc.
  names = {
    resource_group       = "rg-${var.project_name}-${local.location_short}"
    api_management       = "apim-${var.project_name}-${local.suffix}"
    servicebus_namespace = "sb-${var.project_name}-${local.suffix}"
    storage_account      = "st${var.project_name}${local.suffix}"
    service_plan         = "asp-${var.project_name}-${local.suffix}"
    function_app         = "func-${var.project_name}-${local.suffix}"
    log_analytics        = "log-${var.project_name}-${local.suffix}"
    app_insights         = "appi-${var.project_name}-${local.suffix}"
    static_web_app       = "swa-${var.project_name}-${local.suffix}"
  }

  // Fixed entity names. These are duplicated in local/config.json for the
  // Service Bus emulator, which cannot create entities at runtime — see
  // SPEC.md 12.1. Change one, change the other.
  entities = {
    queue                      = "orders"
    topic                      = "order-events"
    subscription_notifications = "notifications"
    subscription_audit         = "audit"
    table_orders               = "Orders"
    table_audit                = "AuditLog"
    deployment_container       = "deployments"
  }

  tags = {
    project     = var.project_name
    environment = "demo"
    managedBy   = "terraform"
    owner       = var.owner_tag
  }
}

resource "azurerm_resource_group" "main" {
  name     = local.names.resource_group
  location = var.location
  tags     = local.tags
}
