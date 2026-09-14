# What `terraform apply` prints, and what infra/RUNBOOK.md tells you to read.
#
# No secret is output. The MySQL administrator password and the JWT signing key
# go into App Service settings and stop there — `terraform output` is a normal
# thing to run in front of other people.

output "user_service_url" {
  description = "Public base address of the User Service. /health answers anonymously and is the quickest proof the deploy worked."
  value       = "https://${azurerm_linux_web_app.user_service.default_hostname}"
}

output "user_service_app_name" {
  description = "App Service name, as the `az webapp` commands in infra/RUNBOOK.md want it."
  value       = azurerm_linux_web_app.user_service.name
}

output "resource_group_name" {
  description = "Resource group holding the whole application stack. NOT buildnexus-tfstate-rg, which holds Terraform's own state and is not managed here."
  value       = azurerm_resource_group.main.name
}

output "mysql_server_fqdn" {
  description = "Host the services connect to. Reachable only from Azure services and whatever the firewall rules allow, and it refuses unencrypted connections."
  value       = azurerm_mysql_flexible_server.main.fqdn
}
