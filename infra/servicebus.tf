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
