# Input variables for the BuildNexus Azure stack.
#
# Nothing secret has a default. A value left undefined makes Terraform stop and
# ask rather than quietly applying a placeholder, and the real values are passed
# in through TF_VAR_* environment variables or a git-ignored terraform.tfvars —
# see terraform.tfvars.example and infra/RUNBOOK.md.

variable "subscription_id" {
  description = "Azure subscription the stack is created in. Left null, the azurerm provider reads ARM_SUBSCRIPTION_ID and then falls back to the subscription the Azure CLI has selected, which is how this is normally run."
  type        = string
  default     = null
}

# --- Shared MySQL Flexible Server -------------------------------------------

variable "mysql_server_name" {
  description = "Name of the shared MySQL Flexible Server. Must be globally unique across Azure, since the server is addressed as <name>.mysql.database.azure.com — override only if the default is already taken."
  type        = string
  default     = "buildnexus-mysql-2026"
}

variable "mysql_administrator_login" {
  description = "Administrator account on the shared MySQL Flexible Server. Not a secret, and not a value Azure lets you change after creation."
  type        = string
  default     = "buildnexusadmin"

  validation {
    # Azure reserves these and rejects the server outright at creation rather
    # than at plan time, which is an expensive way to find out.
    condition     = !contains(["azure_superuser", "azure_pg_admin", "admin", "administrator", "root", "guest", "public"], lower(var.mysql_administrator_login))
    error_message = "Azure reserves this administrator name. Choose another."
  }
}

variable "mysql_administrator_password" {
  description = "Administrator password on the shared MySQL Flexible Server. No default on purpose — supply it through TF_VAR_mysql_administrator_password or a git-ignored terraform.tfvars so it never reaches the repository."
  type        = string
  sensitive   = true

  validation {
    # Azure's own rule is 8-128 characters with three of four character classes.
    # Only the length is checked here; the classes are left to Azure rather than
    # reimplemented in a regex that could disagree with it.
    condition     = length(var.mysql_administrator_password) >= 8 && length(var.mysql_administrator_password) <= 128
    error_message = "Azure requires the MySQL administrator password to be 8-128 characters."
  }
}
