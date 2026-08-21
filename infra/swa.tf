// Free tier: a real HTTPS URL you can share, at no cost. It hosts static
// assets only — every call it makes goes through API Management, which is what
// makes the CORS policy in policies/orders-api.xml load-bearing.
resource "azurerm_static_web_app" "main" {
  name                = local.names.static_web_app
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku_tier            = "Free"
  sku_size            = "Free"

  tags = local.tags
}
