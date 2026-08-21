// Consumption tier: per-call billing with 1M free calls a month, and it
// provisions in minutes rather than the ~40 the classic tiers take, which
// keeps the create/destroy demo loop viable.
//
// What it costs us (SPEC.md 2): no developer portal, no VNet integration, no
// self-hosted gateway, and no plain rate-limit or quota policies — only the
// -by-key variants.
resource "azurerm_api_management" "main" {
  name                = local.names.api_management
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  publisher_name      = var.publisher_name
  publisher_email     = var.publisher_email
  sku_name            = var.apim_sku_name

  tags = local.tags
}

// Gateway telemetry lands in the same Application Insights instance the
// Function App reports to. Same instance is the whole point — split them and
// the end-to-end transaction view in SPEC.md 10 stops correlating.
resource "azurerm_api_management_logger" "appinsights" {
  name                = "appinsights"
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  resource_id         = azurerm_application_insights.main.id

  application_insights {
    instrumentation_key = azurerm_application_insights.main.instrumentation_key
  }
}

resource "azurerm_api_management_api" "orders" {
  name                = "orders-api"
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  revision            = "1"
  display_name        = "Orders API"
  path                = "orders-demo"
  protocols           = ["https"]
  description         = "Order intake and status for the Azure integration services demo."

  subscription_required = true
}

// --- Operations -------------------------------------------------------------

resource "azurerm_api_management_api_operation" "submit_order" {
  operation_id        = "submit-order"
  api_name            = azurerm_api_management_api.orders.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  display_name        = "Submit order"
  method              = "POST"
  url_template        = "/orders"
  description         = "Accepts an order and returns 202 with a status URL."

  response {
    status_code = 202
    description = "Accepted"
  }

  response {
    status_code = 400
    description = "Invalid order"
  }
}

resource "azurerm_api_management_api_operation" "get_order" {
  operation_id        = "get-order"
  api_name            = azurerm_api_management_api.orders.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  display_name        = "Get order status"
  method              = "GET"
  url_template        = "/orders/{orderId}"
  description         = "Returns the current state of a single order."

  template_parameter {
    name     = "orderId"
    type     = "string"
    required = true
  }

  response {
    status_code = 200
    description = "Order state"
  }

  response {
    status_code = 404
    description = "Unknown order"
  }
}

resource "azurerm_api_management_api_operation" "replay_dlq" {
  operation_id        = "replay-dead-letters"
  api_name            = azurerm_api_management_api.orders.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  display_name        = "Replay dead letters"
  method              = "POST"
  url_template        = "/admin/replay"
  description         = "Drains the dead-letter queue and resubmits. Demo scenario 14.3."

  response {
    status_code = 200
    description = "Replay result"
  }
}

// --- Product and subscription ----------------------------------------------

resource "azurerm_api_management_product" "starter" {
  product_id          = "aisdemo-starter"
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  display_name        = "AIS Demo Starter"
  description         = "Demo product. Requires a subscription key; no approval step."

  subscription_required = true
  approval_required     = false
  published             = true
}

resource "azurerm_api_management_product_api" "starter_orders" {
  product_id          = azurerm_api_management_product.starter.product_id
  api_name            = azurerm_api_management_api.orders.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
}

// A named subscription so the key is a Terraform output rather than something
// clicked out of the portal before every demo.
resource "azurerm_api_management_subscription" "demo" {
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  product_id          = azurerm_api_management_product.starter.id
  display_name        = "Demo subscription"
  state               = "active"
}

// --- Backend wiring and policies -------------------------------------------

// Read after the app exists so the key is never handled manually.
data "azurerm_function_app_host_keys" "main" {
  name                = azurerm_function_app_flex_consumption.main.name
  resource_group_name = azurerm_resource_group.main.name
}

resource "azurerm_api_management_named_value" "function_host_key" {
  name                = "func-host-key"
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  display_name        = "func-host-key"
  value               = data.azurerm_function_app_host_keys.main.default_function_key
  secret              = true
}

resource "azurerm_api_management_api_policy" "orders" {
  api_name            = azurerm_api_management_api.orders.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name

  xml_content = templatefile("${path.module}/policies/orders-api.xml", {
    backend_url = "https://${azurerm_function_app_flex_consumption.main.default_hostname}/api"
    // Placeholder until W30 creates the Static Web App. CORS needs a literal
    // origin, and an empty one would make the policy invalid.
    swa_origin = "https://localhost"
  })

  depends_on = [azurerm_api_management_named_value.function_host_key]
}

resource "azurerm_api_management_api_operation_policy" "submit_order" {
  api_name            = azurerm_api_management_api.orders.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  operation_id        = azurerm_api_management_api_operation.submit_order.operation_id

  xml_content = templatefile("${path.module}/policies/submit-order.xml", {
    // Every rate-limiting policy is rejected on Consumption, so the element is
    // emitted only on a tier that accepts it. See SPEC.md 5.1.1.
    rate_limit_policy = startswith(var.apim_sku_name, "Consumption") ? "" : format(
      "<rate-limit-by-key calls=\"%d\" renewal-period=\"%d\" counter-key=\"@(context.Subscription.Id)\" />",
      var.rate_limit_calls, var.rate_limit_window_seconds
    )
  })
}

// A logger alone emits nothing. The diagnostic is what turns gateway telemetry
// on, and without it APIM never appears in the Application Map at all.
//
// http_correlation_protocol = "W3C" is the load-bearing setting: the default,
// "Legacy", uses Request-Id headers that do not line up with the traceparent
// the Functions worker and the Service Bus SDK emit. Leave it on Legacy and
// the gateway shows up as a separate operation instead of the first span of
// the order's transaction — which is precisely the SPEC.md 10 payoff.
resource "azurerm_api_management_api_diagnostic" "orders" {
  identifier               = "applicationinsights"
  api_name                 = azurerm_api_management_api.orders.name
  api_management_name      = azurerm_api_management.main.name
  resource_group_name      = azurerm_resource_group.main.name
  api_management_logger_id = azurerm_api_management_logger.appinsights.id

  sampling_percentage       = 100
  always_log_errors         = true
  verbosity                 = "information"
  http_correlation_protocol = "W3C"
  log_client_ip             = true

  frontend_request {
    headers_to_log = ["content-type", "x-correlation-id"]
    body_bytes     = 1024
  }

  frontend_response {
    headers_to_log = ["content-type", "x-correlation-id", "location"]
    body_bytes     = 1024
  }

  backend_request {
    headers_to_log = ["content-type", "x-correlation-id"]
    body_bytes     = 1024
  }

  backend_response {
    headers_to_log = ["content-type", "x-correlation-id"]
    body_bytes     = 1024
  }
}

// Service-scope diagnostic in addition to the API-scope one above. The API
// scope alone did not produce gateway telemetry.
resource "azurerm_api_management_diagnostic" "service" {
  identifier               = "applicationinsights"
  api_management_name      = azurerm_api_management.main.name
  resource_group_name      = azurerm_resource_group.main.name
  api_management_logger_id = azurerm_api_management_logger.appinsights.id

  sampling_percentage       = 100
  always_log_errors         = true
  verbosity                 = "information"
  http_correlation_protocol = "W3C"
  log_client_ip             = true
}
