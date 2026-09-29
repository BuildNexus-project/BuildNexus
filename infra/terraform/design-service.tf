# Design Service: its own database on the shared MySQL server and its own MySQL
# user that can reach that database and nothing else.
#
# Independently redeployable is the point. Nothing here is shared with another
# service except the server itself, so redeploying the Design Service neither
# rebuilds nor restarts the other four.

resource "azurerm_mysql_flexible_database" "design_service" {
  # The same database name the local stack uses, so a connection string differs
  # between the two environments only in host and credentials.
  name                = "buildnexus_design_db"
  resource_group_name = azurerm_resource_group.main.name
  server_name         = azurerm_mysql_flexible_server.main.name

  # Matching the mysql:8.0 image's own defaults, for the same reason as the
  # other services' databases: the migrations in services/design-service/
  # Migrations declare no charset, so every table inherits whatever the
  # database was created with.
  charset   = "utf8mb4"
  collation = "utf8mb4_0900_ai_ci"

  # This is also where uploaded design documents live. The service keeps each
  # version's bytes in design_document_versions.file_bytes (a LONGBLOB, capped
  # at 10 MB by DesignFileValidator), not on the App Service's filesystem — so
  # an upload is as durable as this database and covered by the server's
  # backups, and there is no separate Blob Storage account to provision. See
  # infra/RUNBOOK.md.
}

# --- Scoped database user ------------------------------------------------------
#
# The Design Service does not connect as the server administrator. This account
# holds privileges on buildnexus_design_db alone — the same arrangement, and the
# same reasoning, as the Project Service's user in project-service.tf.

resource "mysql_user" "design_service" {
  user = "design_service"

  # Any host, because an App Service on a shared plan has no fixed outbound IP
  # to name. The server's firewall, not this, decides who can reach it at all.
  host = "%"

  plaintext_password = var.design_service_db_password

  # The mysql provider's endpoint is built from a variable, so Terraform cannot
  # see that this needs the server and the operator's firewall rule — see the
  # identical depends_on in project-service.tf, and the propagation wait in
  # main.tf.
  depends_on = [time_sleep.mysql_firewall_propagation]
}

resource "mysql_grant" "design_service" {
  user     = mysql_user.design_service.user
  host     = mysql_user.design_service.host
  database = azurerm_mysql_flexible_database.design_service.name

  # Every table in buildnexus_design_db and nothing on any other database or on
  # the server itself: no *.* privilege, no CREATE USER, no GRANT OPTION.
  privileges = [
    # DML: what the ADO.NET repositories and the outbox dispatcher run.
    "SELECT",
    "INSERT",
    "UPDATE",
    "DELETE",

    # DDL: what DbUp needs at startup to apply Migrations/ and keep its own
    # schemaversions journal — CREATE TABLE, ALTER TABLE (the later scripts
    # widen CHECK constraints by dropping and re-adding them), CREATE INDEX, and
    # REFERENCES for the version and outbox foreign keys. DROP covers a later
    # migration that retires a table.
    "CREATE",
    "ALTER",
    "DROP",
    "INDEX",
    "REFERENCES",
  ]

  # Issued over the same connection as the user, so it waits for the same
  # firewall propagation.
  depends_on = [time_sleep.mysql_firewall_propagation]
}
