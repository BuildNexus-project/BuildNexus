# Project Service: its own database on the shared MySQL server, its own MySQL user
# that can reach that database and nothing else, its own Event Hub on the shared
# Event Hubs namespace, and its own App Service on the shared plan.
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
  #
  # On the propagation wait in main.tf rather than on the firewall rule itself:
  # Azure reports the rule created before it takes effect, and connecting in
  # that window times out. The wait comes after the rule, so the ordering above
  # still holds.
  depends_on = [time_sleep.mysql_firewall_propagation]
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
  # connection, so it waits for the same firewall propagation.
  depends_on = [time_sleep.mysql_firewall_propagation]
}

# --- Event Hub ---------------------------------------------------------------
#
# project-events: the one topic this service publishes to. On Event Hubs the
# Event Hub IS the Kafka topic, so the name must match
# KafkaProjectEventPublisher.Topic exactly — a publish to any other name has
# nowhere to land. Nothing consumes it yet; a service that later subscribes does
# so through its own consumer group, and changes nothing here.
#
# Declared here rather than left to appear on first publish, the way
# KAFKA_AUTO_CREATE_TOPICS_ENABLE lets it locally, so it exists with the
# partitions and retention chosen below before the service ever starts.

resource "azurerm_eventhub" "project_events" {
  name         = "project-events"
  namespace_id = azurerm_eventhub_namespace.main.id

  # One partition, the same as the local stack: the apache/kafka broker creates
  # topics with its num.partitions default of 1, so a consumer there sees every
  # project's events in a single order, and it will see the same here. Standard
  # cannot change the count after creation — but the stack is destroyed and
  # rebuilt between demos anyway, so raising it later costs one rebuild, not a
  # migration.
  partition_count = 1

  # Seven days, the most Standard allows and included in its price — and the
  # same as the 168-hour log.retention.hours default the local broker runs with,
  # so a consumer that was down can catch up over the same window in both.
  message_retention = 7
}

# --- App Service -------------------------------------------------------------

resource "azurerm_linux_web_app" "project_service" {
  # Globally unique across Azure — this becomes <name>.azurewebsites.net.
  name                = var.project_service_app_name
  resource_group_name = azurerm_resource_group.main.name
  location            = "southeastasia"
  service_plan_id     = azurerm_service_plan.main.id

  # Every endpoint but /health takes a bearer token, so plain HTTP is redirected
  # away before the app sees the request — same as the User Service.
  https_only = true

  site_config {
    # Same reason as the User Service, and one more here: the outbox dispatcher
    # is a background service inside this process. An App Service idled out for
    # want of HTTP traffic stops draining events onto project-events until the
    # next request wakes it.
    always_on = true

    # Already in Program.cs and [AllowAnonymous]. The app answers it only after
    # DbUp has migrated buildnexus_project_db at startup, as the scoped
    # project_service user — so a 200 here also proves that user's grants were
    # enough.
    health_check_path                 = "/health"
    health_check_eviction_time_in_min = 5

    application_stack {
      # Matches <TargetFramework>net10.0</TargetFramework> in ProjectService.csproj.
      dotnet_version = "10.0"
    }
  }

  # The publish-profile deploy in .github/workflows/ci.yml needs this on; see
  # the User Service's App Service for the 401 it causes when off.
  webdeploy_publish_basic_authentication_enabled = true
  ftp_publish_basic_authentication_enabled       = false

  # --- Application Settings ---------------------------------------------------
  #
  # Environment variables, double-underscored onto configuration keys exactly as
  # infra/docker-compose.yml does locally. Nothing is hardcoded: secrets come from
  # variables with no default or from resource attributes, and addresses are
  # built from the resources they point at.
  app_settings = {
    # Not Development: turns Swagger off on a publicly reachable host.
    ASPNETCORE_ENVIRONMENT = "Production"

    # The scoped project_service user — NOT the server administrator the User
    # Service still connects as. This service cannot reach any other service's
    # database. SslMode=Required for the same reason as the User Service's.
    ConnectionStrings__ProjectDb = join("", [
      "Server=${azurerm_mysql_flexible_server.main.fqdn};",
      "Port=3306;",
      "Database=${azurerm_mysql_flexible_database.project_service.name};",
      "User Id=${mysql_user.project_service.user};",
      "Password=${var.project_service_db_password};",
      "SslMode=Required;",
    ])

    # The shared convention, from the same three variables as every other
    # service. No lifetime setting: that is baked into exp by the User Service.
    Jwt__Issuer     = var.jwt_issuer
    Jwt__Audience   = var.jwt_audience
    Jwt__SigningKey = var.jwt_signing_key

    # Azure Event Hubs' Kafka endpoint instead of the local broker, under the
    # same Kafka__BootstrapServers name every publishing service uses.
    Kafka__BootstrapServers = local.eventhub_kafka_bootstrap_servers

    # SASL over TLS with the namespace's connection string, the only way Event
    # Hubs accepts a SAS-authenticated Kafka client. The username is the literal
    # text $ConnectionString, not a reference — HCL only interpolates "${", so
    # the "$" here reaches the app unchanged. The password is the namespace's
    # default RootManageSharedAccessKey connection string, straight from the
    # resource, so it is never typed anywhere.
    Kafka__SecurityProtocol = "SaslSsl"
    Kafka__SaslMechanism    = "Plain"
    Kafka__SaslUsername     = "$ConnectionString"
    Kafka__SaslPassword     = azurerm_eventhub_namespace.main.default_primary_connection_string

    # The values Event Hubs documents for librdkafka clients: a request timeout
    # above its 20-second internal minimum, keepalives against Azure closing a
    # connection idle for 240 seconds, and a metadata refresh below that limit.
    Kafka__RequestTimeoutMs      = "60000"
    Kafka__SocketKeepaliveEnable = "true"
    Kafka__MetadataMaxAgeMs      = "180000"

    # How long one publish may take before it counts as failed — 60 seconds here
    # rather than the service's 5-second default, so an acknowledgement Event
    # Hubs takes its time over (it allows itself 20 seconds) is not recorded as a
    # failure. The cost is only while Event Hubs is unreachable: the dispatcher
    # publishes one event at a time, so each pending event holds it up to a
    # minute before the failure is recorded. Nothing is lost either way — a
    # failed event stays in the outbox and the next pass retries it.
    Kafka__MessageTimeoutMs = "60000"

    # Not named in the story's acceptance criteria, but assigning an Architect or
    # Project Manager asks the User Service what role the account holds. Left to
    # appsettings.json it would point at localhost and every assignment would
    # answer 502. The deployed User Service, over HTTPS; trailing slash required.
    Services__UserService__BaseUrl = "https://${azurerm_linux_web_app.user_service.default_hostname}/"
  }

  # The app runs its migrations as project_service the moment it starts, so the
  # grant must exist first — the connection string only references the user, and
  # a user without its grant fails DbUp's first CREATE TABLE. The Event Hub must
  # exist before the dispatcher's first publish to it.
  depends_on = [
    mysql_grant.project_service,
    azurerm_eventhub.project_events,
  ]
}
