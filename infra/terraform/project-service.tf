# Project Service: its own database on the shared MySQL server, and its own MySQL
# user that can reach that database and nothing else.
#
# Independently redeployable is the point. Nothing here is shared with another
# service except the plan, the server and the Event Hubs namespace themselves,
# so redeploying the Project Service neither rebuilds nor restarts the other
# four.

resource "azurerm_mysql_flexible_database" "project_service" {
  # The same database name the local stack uses, so a connection string differs
  # between the two environments only in host and credentials.
  name                = "buildnexus_project_db"
  resource_group_name = azurerm_resource_group.main.name
  server_name         = azurerm_mysql_flexible_server.main.name

  # Matching the mysql:8.0 image's own defaults, for the same reason as the User
  # Service's database: the migrations in services/project-service/Migrations
  # declare no charset, so every table inherits whatever the database was
  # created with.
  charset   = "utf8mb4"
  collation = "utf8mb4_0900_ai_ci"
}

# --- Scoped database user ------------------------------------------------------
#
# The Project Service does not connect as the server administrator. This account
# holds privileges on buildnexus_project_db alone, so a leaked connection string
# exposes one service's schema rather than every service's — the
# one-schema-per-service rule enforced by the server, not just by convention.
# It is the same arrangement the local stack already has: DatabaseMigrator.cs is
# written to run under an account scoped to its own database.

resource "mysql_user" "project_service" {
  user = "project_service"

  # Any host, because an App Service on a shared plan has no fixed outbound IP to
  # name — the same reason the allow-azure-services rule in main.tf is 0.0.0.0.
  # The server's firewall, not this, decides who can reach it at all.
  host = "%"

  plaintext_password = var.project_service_db_password

  # The mysql provider's endpoint is built from a variable rather than read off
  # the server resource (see provider.tf), so Terraform cannot see that this
  # needs the server to exist and this machine to be let through its firewall.
  # Said explicitly: a rebuild would otherwise try to create the user before
  # there is a server to create it on, and a destroy would remove the firewall
  # rule before dropping the user.
  depends_on = [azurerm_mysql_flexible_server_firewall_rule.terraform_operator]
}

resource "mysql_grant" "project_service" {
  user     = mysql_user.project_service.user
  host     = mysql_user.project_service.host
  database = azurerm_mysql_flexible_database.project_service.name

  # Every table in buildnexus_project_db — `buildnexus_project_db`.* — and
  # nothing on any other database or on the server itself: no *.* privilege, no
  # CREATE USER, no GRANT OPTION.
  privileges = [
    # DML: what the ADO.NET repositories and the outbox dispatcher run.
    "SELECT",
    "INSERT",
    "UPDATE",
    "DELETE",

    # DDL: what DbUp needs at startup to apply Migrations/ and keep its own
    # schemaversions journal — CREATE TABLE, ALTER TABLE, CREATE INDEX, and
    # REFERENCES for the foreign keys into projects. DROP covers a later
    # migration that retires a table.
    "CREATE",
    "ALTER",
    "DROP",
    "INDEX",
    "REFERENCES",
  ]

  # Same reason as on mysql_user above — the grant is issued over the same
  # connection.
  depends_on = [azurerm_mysql_flexible_server_firewall_rule.terraform_operator]
}
