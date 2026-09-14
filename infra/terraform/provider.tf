# Terraform settings for the BuildNexus Azure stack.
#
# One root module, one state file. This module owns the shared resources every
# service deployment reuses — the resource group, the App Service plan and the
# MySQL Flexible Server — plus each service's own App Service and database on
# top of them. A later deployment story adds its service's resources to THIS
# module rather than starting a second stack, so `terraform destroy` between
# demos tears down exactly what `terraform apply` brought up and nothing is left
# billing quietly in a state file nobody remembers.

terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # State lives in Azure Storage, never on a laptop. The stack is destroyed and
  # rebuilt between demos to control Azure credit usage, and local state would
  # make that a one-machine operation — and lose the stack outright if that
  # machine did.
  #
  # This resource group, storage account and container are NOT managed by this
  # module. They were created once, out of band, and exist before the first
  # `terraform init`. A module must not hold its own state's storage: destroying
  # the stack would take the record of the stack with it.
  #
  # No credentials here. The backend authenticates with the storage account
  # access key, read from the ARM_ACCESS_KEY environment variable — see
  # infra/RUNBOOK.md.
  backend "azurerm" {
    resource_group_name  = "buildnexus-tfstate-rg"
    storage_account_name = "buildnexustfstate2026"
    container_name       = "tfstate"
    key                  = "user-service/terraform.tfstate"
  }
}

provider "azurerm" {
  features {}

  # Deliberately no client_id / client_secret / tenant_id. The provider uses the
  # signed-in Azure CLI session (`az login`) instead: the university's Azure AD
  # tenant blocks Service Principal creation for student accounts, so there is no
  # credential pair to put here — which is also why Terraform is only ever run
  # locally and never from a GitHub Actions workflow.
  #
  # subscription_id is an identifier rather than a secret, but it stays out of
  # the repository all the same. Left null, the provider falls back to
  # ARM_SUBSCRIPTION_ID and then to the subscription the CLI has selected.
  subscription_id = var.subscription_id
}
