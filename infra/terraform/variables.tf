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

variable "terraform_operator_ip" {
  description = "Public IPv4 address of the machine running Terraform. It is let through the MySQL firewall so the mysql provider can create — and on destroy, drop — each service's scoped database user. No default: it is one machine's address on one network, so it lives in that operator's git-ignored terraform.tfvars rather than in the repository. `curl -s https://api.ipify.org` prints it."
  type        = string

  validation {
    # A firewall rule takes a single IPv4 start and end address — not a CIDR
    # range, and not IPv6. Checked here so a typo fails at plan time rather than
    # as a mysql provider connection timeout minutes into an apply.
    condition     = can(regex("^[0-9]{1,3}([.][0-9]{1,3}){3}$", var.terraform_operator_ip)) && can(cidrhost("${var.terraform_operator_ip}/32", 0))
    error_message = "terraform_operator_ip must be a single IPv4 address, such as the one `curl -s https://api.ipify.org` prints."
  }
}

# --- Shared Event Hubs namespace ---------------------------------------------

variable "eventhub_namespace_name" {
  description = "Name of the shared Event Hubs namespace that stands in for the Kafka broker. Must be globally unique across Azure, since it is addressed as <name>.servicebus.windows.net — override only if the default is already taken."
  type        = string
  default     = "buildnexus-events-2026"
}

# --- Shared auth convention --------------------------------------------------
#
# Issuer, audience and signing key must be IDENTICAL in every service that
# validates a BuildNexus token. They are variables read by all of them rather
# than literals repeated per service, so the values cannot drift apart — a
# mismatch does not fail quietly, every request comes back 401.

variable "jwt_issuer" {
  description = "Issuer claim on every BuildNexus token. Shared by every service; not a secret."
  type        = string
  default     = "BuildNexusAuth"
}

variable "jwt_audience" {
  description = "Audience claim on every BuildNexus token. Shared by every service; not a secret."
  type        = string
  default     = "BuildNexusServices"
}

variable "jwt_signing_key" {
  description = "HMAC-SHA256 key the User Service signs tokens with and every other service validates against. No default on purpose — anyone holding this can mint a token for any user and any role, so it is supplied through TF_VAR_jwt_signing_key or a git-ignored terraform.tfvars and never committed. Must NOT be the local-development key in infra/.env.example."
  type        = string
  sensitive   = true

  validation {
    # JwtOptions.MinimumSigningKeyBytes is 32 and the host refuses to start
    # below it. Checked here so that surfaces at plan time rather than as an App
    # Service that deploys green and then will not boot. The service counts
    # UTF-8 bytes; for an ASCII key that is the same number as characters.
    condition     = length(var.jwt_signing_key) >= 32
    error_message = "Jwt__SigningKey must be at least 32 bytes or the User Service will not start."
  }
}

variable "internal_service_api_key" {
  description = "Shared key presented in the X-Internal-Api-Key header by a service calling another service's /api/internal endpoints. No default, same reason as jwt_signing_key."
  type        = string
  sensitive   = true

  validation {
    # InternalServiceOptions.MinimumApiKeyBytes is 32, also ValidateOnStart.
    condition     = length(var.internal_service_api_key) >= 32
    error_message = "InternalService__ApiKey must be at least 32 bytes or the User Service will not start."
  }
}

# --- User Service ------------------------------------------------------------

variable "user_service_app_name" {
  description = "Name of the User Service App Service. Must be globally unique across Azure, since it becomes <name>.azurewebsites.net — override only if the default is already taken."
  type        = string
  default     = "buildnexus-user-service-2026"
}

variable "frontend_origin" {
  description = "Origin the React app is served from, used to build the password reset link that is emailed to a user. Still the local dev server: the frontend has no deployed home until its own story lands."
  type        = string
  default     = "http://localhost:5173"
}

# --- Project Service ---------------------------------------------------------

variable "project_service_app_name" {
  description = "Name of the Project Service App Service. Must be globally unique across Azure, since it becomes <name>.azurewebsites.net — override only if the default is already taken."
  type        = string
  default     = "buildnexus-project-service-2026"
}

variable "project_service_db_password" {
  description = "Password for the Project Service's own MySQL user, which is granted privileges on buildnexus_project_db and nothing else on the server. No default on purpose — supply it through TF_VAR_project_service_db_password or a git-ignored terraform.tfvars so it never reaches the repository."
  type        = string
  sensitive   = true

  validation {
    # An empty password would create an account that anything the firewall lets
    # through could sign in to. Eight characters is the same floor the
    # administrator password has.
    condition     = length(var.project_service_db_password) >= 8
    error_message = "project_service_db_password must be at least 8 characters."
  }

  validation {
    # Embedded in ConnectionStrings__ProjectDb, where a semicolon ends the
    # Password= value early: the service then signs in with the truncated
    # remainder and fails at startup with an access-denied that does not point
    # back here. `openssl rand -base64` never produces one.
    condition     = !strcontains(var.project_service_db_password, ";")
    error_message = "project_service_db_password must not contain a semicolon — it is embedded in a MySQL connection string."
  }

  validation {
    # The point of a separate account is that the Project Service never holds
    # the administrator credential. Reusing the administrator's password would
    # quietly undo that for anyone who can read this service's settings.
    condition     = var.project_service_db_password != var.mysql_administrator_password
    error_message = "project_service_db_password must differ from mysql_administrator_password."
  }
}
