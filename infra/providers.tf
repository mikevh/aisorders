terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source = "hashicorp/azurerm"
      # Pinned in W02. v5 renamed several storage arguments; see SPEC.md 17.1.
      version = "~> 5.2"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Local state by design; see SPEC.md 13.3.
}

provider "azurerm" {
  # Set explicitly rather than relying on ambient CLI or environment state,
  # so a stray `az account set` cannot retarget an apply.
  subscription_id = var.subscription_id

  features {}
}
