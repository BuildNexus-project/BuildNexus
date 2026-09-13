# The shared Azure resources every BuildNexus service deployment sits on.
#
# Three things, created once and reused by all five services:
#
#   - one resource group, so the whole stack is a single unit to destroy
#   - one Linux App Service plan, which the five App Services share
#   - one MySQL Flexible Server, which holds one DATABASE per service
#
# The one-database-per-service rule is about not sharing schemas or tables
# between services, never about needing five paid server instances. Each service
# gets its own `azurerm_mysql_flexible_database` on this server, with its own
# connection string, and nothing joins across two of them — the same arrangement
# as the five separate MySQL containers in infra/docker-compose.yml, minus four
# servers' worth of Azure credit.
#
# --- Why every location below is the literal string "centralindia" ------------
#
# This subscription has a region restriction. `eastus` was tried first and Azure
# refused the deployment outright with RequestDisallowedByAzure; centralindia is
# the confirmed-working region. It is written out on each resource rather than
# hidden behind a variable precisely so nobody can override it to a region that
# fails, and so the reason is visible at the point of use.

resource "azurerm_resource_group" "main" {
  name     = "buildnexus-rg"
  location = "centralindia"

  # NOT the state backend's resource group. buildnexus-tfstate-rg holds the
  # storage account this module's own state lives in and is deliberately not
  # managed here — see the backend block in provider.tf.
}

resource "azurerm_service_plan" "main" {
  name                = "buildnexus-asp"
  resource_group_name = azurerm_resource_group.main.name
  location            = "centralindia"
  os_type             = "Linux"

  # B1 (Basic). The smallest tier that still supports Always On, which the
  # services need: without it the platform idles the app out and the next
  # request pays for a cold start plus a full DbUp migration check. Free/Shared
  # tiers cannot hold five apps with Always On.
  sku_name = "B1"
}

resource "azurerm_mysql_flexible_server" "main" {
  # Globally unique across Azure, not merely unique in the resource group — the
  # server is addressed as <name>.mysql.database.azure.com. A variable rather
  # than a literal only so a collision can be worked around without editing
  # this file; the default follows the same naming as the state storage account.
  name                = var.mysql_server_name
  resource_group_name = azurerm_resource_group.main.name
  location            = "centralindia"

  # Neither value is written down here. Both come in through TF_VAR_* or a
  # git-ignored terraform.tfvars — see terraform.tfvars.example.
  administrator_login    = var.mysql_administrator_login
  administrator_password = var.mysql_administrator_password

  # Burstable B1ms, the cheapest Flexible Server tier, and 8.0.21 to match the
  # mysql:8.0 image the local stack runs so the migrations meet the same engine
  # in both places.
  sku_name = "B_Standard_B1ms"
  version  = "8.0.21"

  storage {
    size_gb = 20
    iops    = 360
    # Let Azure grow the disk rather than letting a full one take the service
    # down; 20 GB is already far more than five schemas of demo data need.
    auto_grow_enabled = true
  }

  # The floor Azure allows. Nothing in a demo stack that gets destroyed between
  # sessions is worth paying to retain for longer.
  backup_retention_days = 7

  lifecycle {
    # No `zone` is set above, so Azure picks one at creation and then reports it
    # back. Without this, every later plan shows a phantom change on a field
    # that was never configured and would move the server if applied.
    ignore_changes = [zone]
  }
}

# Lets the App Services reach the server.
#
# 0.0.0.0 to 0.0.0.0 is not an IP range — it is Azure's sentinel for "allow
# any Azure service in any subscription", the same switch the portal labels
# "Allow public access from Azure services". It is needed because an App Service
# on a shared plan has no fixed outbound IP to pin a rule to.
#
# The server still refuses unencrypted connections (require_secure_transport is
# ON by default on Flexible Server), which is why every connection string in
# this module carries SslMode=Required.
resource "azurerm_mysql_flexible_server_firewall_rule" "allow_azure_services" {
  name                = "allow-azure-services"
  resource_group_name = azurerm_resource_group.main.name
  server_name         = azurerm_mysql_flexible_server.main.name
  start_ip_address    = "0.0.0.0"
  end_ip_address      = "0.0.0.0"
}
