variable "subscription_id" {
  description = "Target Azure subscription ID."
  type        = string

  validation {
    condition     = can(regex("^[0-9a-fA-F-]{36}$", var.subscription_id))
    error_message = "subscription_id must be a GUID."
  }
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "westus2"
}

variable "project_name" {
  description = "Name prefix for all resources. Lowercase alphanumeric only, since it feeds the storage account name."
  type        = string
  default     = "aisdemo"

  validation {
    condition     = can(regex("^[a-z0-9]{3,11}$", var.project_name))
    error_message = "project_name must be 3-11 lowercase alphanumeric characters (storage account names cap at 24 and this is prefixed and suffixed)."
  }
}

variable "publisher_name" {
  description = "API Management publisher name. Shown to API consumers."
  type        = string
}

variable "publisher_email" {
  description = "API Management publisher email. Receives APIM notifications."
  type        = string

  validation {
    condition     = can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.publisher_email))
    error_message = "publisher_email must be a valid email address."
  }
}

variable "owner_tag" {
  description = "Value for the owner tag applied to every resource."
  type        = string
}

variable "processing_delay_ms" {
  description = "Simulated work duration in ProcessOrder. Raise before a demo so queue depth and scale-out are visible."
  type        = number
  default     = 250

  validation {
    condition     = var.processing_delay_ms >= 0 && var.processing_delay_ms <= 60000
    error_message = "processing_delay_ms must be between 0 and 60000."
  }
}

variable "rate_limit_calls" {
  description = "Calls permitted per window by the APIM rate-limit-by-key policy on POST /orders."
  type        = number
  default     = 10

  validation {
    condition     = var.rate_limit_calls > 0
    error_message = "rate_limit_calls must be greater than zero."
  }
}

variable "rate_limit_window_seconds" {
  description = "Rate limit window length in seconds."
  type        = number
  default     = 60

  validation {
    condition     = var.rate_limit_window_seconds > 0
    error_message = "rate_limit_window_seconds must be greater than zero."
  }
}

variable "notification_threshold" {
  description = "Order total above which the notifications subscription filter matches. Drives the fan-out demo in SPEC.md 14.2."
  type        = number
  default     = 500
}

variable "apim_sku_name" {
  description = <<-DESC
    API Management SKU. Consumption_0 is near-free and provisions in minutes,
    but permits no rate-limiting policies at all: rate-limit, rate-limit-by-key,
    quota, and quota-by-key are ALL rejected with "Policy is not allowed in
    'Consumption' sku". Demo scenario 14.5 therefore needs a paid tier.
    Set to Basicv2_1 or Developer_1 to enable it.
  DESC
  type        = string
  default     = "Consumption_0"
}
