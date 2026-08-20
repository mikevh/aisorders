// One account serves three purposes: the Functions runtime's own state, the
// Flex Consumption deployment package container, and the two demo tables.
resource "azurerm_storage_account" "main" {
  name                = local.names.storage_account
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  account_tier             = "Standard"
  account_replication_type = "LRS"

  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
  allow_nested_items_to_be_public = false

  tags = local.tags
}

// Holds the deployment package. Required by the Function App's
// storage_container_endpoint — Flex Consumption will not deploy without it.
// See SPEC.md 17.1.
resource "azurerm_storage_container" "deployments" {
  name               = local.entities.deployment_container
  storage_account_id = azurerm_storage_account.main.id
}

// Order state, point-read by GetOrder. PartitionKey is a fixed "ORDER"
// literal so a lookup needs only the orderId — a hot-partition antipattern
// in production, kept deliberately as a talking point (SPEC.md 8).
resource "azurerm_storage_table" "orders" {
  name               = local.entities.table_orders
  storage_account_id = azurerm_storage_account.main.id
}

resource "azurerm_storage_table" "audit" {
  name               = local.entities.table_audit
  storage_account_id = azurerm_storage_account.main.id
}
