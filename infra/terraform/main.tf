# The shared Azure resources every BuildNexus service deployment sits on.
#
# Four things, created once and reused by every service that needs them:
#
#   - one resource group, so the whole stack is a single unit to destroy
#   - one Linux App Service plan, which the five App Services share
#   - one MySQL Flexible Server, which holds one DATABASE per service
#   - one Event Hubs namespace, which holds one EVENT HUB per Kafka topic
#
# The one-database-per-service rule is about not sharing schemas or tables
# between services, never about needing five paid server instances. Each service
# gets its own `azurerm_mysql_flexible_database` on this server, with its own
# connection string, and nothing joins across two of them — the same arrangement
# as the five separate MySQL containers in infra/docker-compose.yml, minus four
# servers' worth of Azure credit.
#
# --- Why every location below is the literal string "southeastasia" -----------
#
# Two separate restrictions narrow this to one workable region, and they fail in
# different ways, so both are worth knowing before anyone tries to change it.
#
# 1. An Azure Policy on the subscription, "Allowed resource deployment regions",
#    permits exactly five: southeastasia, eastasia, centralindia, uaenorth and
#    austriaeast. Anything else is refused at deployment time with
#    RequestDisallowedByAzure, which is how `eastus` was ruled out.
#
# 2. Of those five, centralindia cannot host a MySQL Flexible Server on this
#    subscription. Creating one there fails with ProvisionNotSupportedForRegion,
#    and the region's MySQL capability endpoint returns HTTP 500 for every API
#    version rather than an empty SKU list — there is no smaller Burstable size
#    to fall back to, because the whole capability set is unavailable. The other
#    four regions each return the full set of nine Burstable SKUs.
#
# So southeastasia: allowed by the policy, MySQL works there, and it is the
# closest of the four to the team. Note that centralindia is fine for the
# resource group and App Service plan — it was only MySQL that failed — but the
# stack stays in one region deliberately, rather than leaving the App Service in
# centralindia and reaching across a region boundary for every query.
#
# The region is written out on each resource rather than hidden behind a
# variable precisely so nobody can override it to one that fails, and so the
# reason is visible at the point of use.

resource "azurerm_resource_group" "main" {
  name     = "buildnexus-rg"
  location = "southeastasia"

  # NOT the state backend's resource group. buildnexus-tfstate-rg holds the
  # storage account this module's own state lives in and is deliberately not
  # managed here — see the backend block in provider.tf.
}

resource "azurerm_service_plan" "main" {
  name                = "buildnexus-asp"
  resource_group_name = azurerm_resource_group.main.name
  location            = "southeastasia"
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
  location            = "southeastasia"

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

# Lets the machine running Terraform reach the server.
#
# Not for the App Services — the rule above covers those. Terraform runs locally
# (see infra/RUNBOOK.md), and the mysql provider opens a real MySQL connection
# from that machine: during apply to create each service's scoped user, and
# during destroy to drop it. Without this rule both stop at a connection
# timeout.
#
# Managed here rather than added by hand like the runbook's bootstrap-admin
# rule, because on a rebuild the server does not exist until this same apply
# creates it — there is no earlier moment at which a manual rule could be added.
#
# The address is a variable with no default, not a literal, so no one machine's
# IP is written into the repository. It lives in each operator's git-ignored
# terraform.tfvars and goes stale there instead.
resource "azurerm_mysql_flexible_server_firewall_rule" "terraform_operator" {
  name                = "terraform-operator"
  resource_group_name = azurerm_resource_group.main.name
  server_name         = azurerm_mysql_flexible_server.main.name
  start_ip_address    = var.terraform_operator_ip
  end_ip_address      = var.terraform_operator_ip
}

# The Kafka broker, in Azure.
#
# Event Hubs speaks the Kafka protocol on <namespace>.servicebus.windows.net:9093,
# so the four services that publish events — Project, Design, Construction and
# Payment; the User Service does not use Kafka — keep producing through
# Confluent.Kafka, and a Kafka topic is simply an Event Hub inside this
# namespace. One namespace for the whole stack, for the same reason there is one
# MySQL server: one topic per publishing service is about not mixing their
# events, never about needing four paid namespaces. Each service declares its
# own Event Hub in its own file.
resource "azurerm_eventhub_namespace" "main" {
  # Globally unique across Azure — this becomes <name>.servicebus.windows.net.
  name                = var.eventhub_namespace_name
  resource_group_name = azurerm_resource_group.main.name
  location            = "southeastasia"

  # Standard, not Basic: Basic has no Kafka endpoint at all. Premium and
  # Dedicated add isolation this stack has no use for, at many times the price.
  sku = "Standard"

  # One throughput unit — 1 MB/s or 1,000 events/s in — is far beyond what a
  # demo produces, and units are billed by the hour. auto_inflate stays off so
  # the namespace never adds a unit, and a charge, that nobody chose.
  capacity             = 1
  auto_inflate_enabled = false

  # The services authenticate to the Kafka endpoint with this namespace's SAS
  # connection string, as username $ConnectionString. Local authentication is
  # what SAS is; with it off, every publish fails authentication with an error
  # that does not mention this setting. Set explicitly rather than left to the
  # provider default for that reason.
  local_authentication_enabled = true

  # Kafka clients connect over TLS on 9093. Nothing older than 1.2 is accepted.
  minimum_tls_version = "1.2"
}

locals {
  # Where a Kafka client reaches the namespace: Event Hubs' Kafka endpoint is
  # always the namespace host on 9093, TLS only. Not a secret — the connection
  # string that authenticates against it is, and lives only in App Service
  # settings.
  eventhub_kafka_bootstrap_servers = "${azurerm_eventhub_namespace.main.name}.servicebus.windows.net:9093"
}
