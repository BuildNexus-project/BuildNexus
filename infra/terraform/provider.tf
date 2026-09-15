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

    # Creates each service's scoped MySQL user, which azurerm cannot: azurerm
    # manages the server and its databases through Azure's management API, but
    # a MySQL user exists only inside the server and has to be created over a
    # MySQL connection. petoju/mysql is the maintained fork of the archived
    # hashicorp/mysql provider.
    mysql = {
      source  = "petoju/mysql"
      version = "~> 3.0"
    }

    # One resource only: time_sleep.mysql_firewall_propagation in main.tf, a
    # fixed wait between the operator's firewall rule being created and the
    # first MySQL connection through it. No provider block is needed.
    time = {
      source  = "hashicorp/time"
      version = "~> 0.14"
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

# Signs in to the shared MySQL Flexible Server as its administrator — the only
# account that can create users and grant privileges — to create each service's
# own scoped user. The connection is opened from the machine running Terraform,
# not from Azure, which is what the terraform-operator firewall rule in main.tf
# is for.
provider "mysql" {
  # Built from the server NAME rather than read off
  # azurerm_mysql_flexible_server.main.fqdn. The stack is destroyed and rebuilt
  # between demos, and on a rebuild that attribute is unknown until the server
  # exists — Terraform cannot reliably configure a provider from a value it does
  # not know at plan time. Flexible Server always addresses a server as
  # <name>.mysql.database.azure.com, so the name alone is enough. The price is
  # that Terraform no longer infers the ordering, which is why the mysql
  # resources in project-service.tf declare depends_on.
  endpoint = "${var.mysql_server_name}.mysql.database.azure.com:3306"

  username = var.mysql_administrator_login
  password = var.mysql_administrator_password

  # require_secure_transport is ON on Flexible Server and refuses an unencrypted
  # connection. "true" encrypts and verifies the server's certificate.
  tls = "true"
}
