# Design Service: its own database on the shared MySQL server, its own MySQL user
# that can reach that database and nothing else, its own Event Hub on the shared
# Event Hubs namespace, and its own App Service on the shared plan.
#
# Independently redeployable is the point. Nothing here is shared with another
# service except the plan, the server and the namespace themselves, so
# redeploying the Design Service neither rebuilds nor restarts the other four.

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

# --- Event Hub ---------------------------------------------------------------
#
# design-events: the one topic this service publishes to. On Event Hubs the
# Event Hub IS the Kafka topic, so the name must match
# KafkaDesignEventPublisher.Topic exactly — a publish to any other name has
# nowhere to land.
#
# The Construction Service reads it, under its own construction-service consumer
# group, to set up milestones for each approved design. That service is not
# deployed yet; when its story lands it subscribes here and changes nothing in
# this block.
#
# Declared here rather than left to appear on first publish, the way
# KAFKA_AUTO_CREATE_TOPICS_ENABLE lets it locally, so it exists with the
# partitions and retention chosen below before the service ever starts.

resource "azurerm_eventhub" "design_events" {
  name         = "design-events"
  namespace_id = azurerm_eventhub_namespace.main.id

  # One partition, the same as the local stack and as project-events: events
  # are keyed by document id, and a consumer sees them in a single order. The
  # count cannot change after creation on Standard, but the stack is rebuilt
  # between demos anyway.
  partition_count = 1

  # Seven days, the most Standard allows and included in its price — and the
  # local broker's own default. Also the window a Construction Service deployed
  # later has to catch up on approvals published before it existed.
  message_retention = 7
}

# --- App Service -------------------------------------------------------------

resource "azurerm_linux_web_app" "design_service" {
  # Globally unique across Azure — this becomes <name>.azurewebsites.net.
  name                = var.design_service_app_name
  resource_group_name = azurerm_resource_group.main.name
  location            = "southeastasia"
  service_plan_id     = azurerm_service_plan.main.id

  # Every endpoint but /health takes a bearer token, so plain HTTP is redirected
  # away before the app sees the request — same as the other services.
  https_only = true

  site_config {
    # Same reason as the Project Service: the outbox dispatcher is a background
    # service inside this process, and an App Service idled out for want of
    # HTTP traffic stops draining events onto design-events until the next
    # request wakes it.
    always_on = true

    # Already in Program.cs and [AllowAnonymous]. The app answers it only after
    # DbUp has migrated buildnexus_design_db at startup, as the scoped
    # design_service user, and every ValidateOnStart check has passed.
    health_check_path                 = "/health"
    health_check_eviction_time_in_min = 5

    application_stack {
      # Matches <TargetFramework>net10.0</TargetFramework> in DesignService.csproj.
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

    # The scoped design_service user, not the server administrator. This
    # service cannot reach any other service's database. SslMode=Required for
    # the same reason as the other services'.
    ConnectionStrings__DesignDb = join("", [
      "Server=${azurerm_mysql_flexible_server.main.fqdn};",
      "Port=3306;",
      "Database=${azurerm_mysql_flexible_database.design_service.name};",
      "User Id=${mysql_user.design_service.user};",
      "Password=${var.design_service_db_password};",
      "SslMode=Required;",
    ])

    # The shared convention, from the same three variables as every other
    # service. No lifetime setting: that is baked into exp by the User Service.
    Jwt__Issuer     = var.jwt_issuer
    Jwt__Audience   = var.jwt_audience
    Jwt__SigningKey = var.jwt_signing_key

    # Azure Event Hubs' Kafka endpoint, authenticated and tuned exactly as the
    # Project Service's is — see project-service.tf for what each value is and
    # why. The username is the literal text $ConnectionString; the password is
    # the namespace's connection string, straight from the resource.
    Kafka__BootstrapServers      = local.eventhub_kafka_bootstrap_servers
    Kafka__SecurityProtocol      = "SaslSsl"
    Kafka__SaslMechanism         = "Plain"
    Kafka__SaslUsername          = "$ConnectionString"
    Kafka__SaslPassword          = azurerm_eventhub_namespace.main.default_primary_connection_string
    Kafka__RequestTimeoutMs      = "60000"
    Kafka__SocketKeepaliveEnable = "true"
    Kafka__MetadataMaxAgeMs      = "180000"
    Kafka__MessageTimeoutMs      = "60000"

    # Every upload and listing asks the Project Service whether the caller is
    # party to the project. Left to appsettings.json it would point at
    # localhost, and every call would answer 502. HTTPS; trailing slash
    # required.
    Services__ProjectService__BaseUrl = "https://${azurerm_linux_web_app.project_service.default_hostname}/"

    # Not in appsettings.json at all, and required at startup: a revision
    # request asks the User Service for the Architect's name and email to
    # notify, presenting the shared internal key.
    Services__UserService__BaseUrl = "https://${azurerm_linux_web_app.user_service.default_hostname}/"
    InternalService__ApiKey        = var.internal_service_api_key

    # Also required at startup (ValidateOnStart) and absent from
    # appsettings.json — the same sender the local stack uses. Email__SmtpHost
    # is deliberately unset, as on the User Service: Program.cs then resolves
    # IEmailSender to LoggingEmailSender, and revision-request emails go to the
    # App Service log stream rather than to real addresses.
    Email__FromAddress = "no-reply@buildnexus.local"
  }

  # The app runs its migrations as design_service the moment it starts, so the
  # grant must exist first; the Event Hub must exist before the dispatcher's
  # first publish to it.
  depends_on = [
    mysql_grant.design_service,
    azurerm_eventhub.design_events,
  ]
}
