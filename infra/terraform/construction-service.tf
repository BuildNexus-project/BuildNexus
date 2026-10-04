# Construction Service: its own database on the shared MySQL server, its own MySQL
# user that can reach that database and nothing else, its own Event Hub to publish
# to, consumer groups on the three Event Hubs it reads, and its own App Service on
# the shared plan. Nothing new is added to the shared server, namespace or plan.

resource "azurerm_mysql_flexible_database" "construction_service" {
  name                = "buildnexus_construction_db"
  resource_group_name = azurerm_resource_group.main.name
  server_name         = azurerm_mysql_flexible_server.main.name

  charset   = "utf8mb4"
  collation = "utf8mb4_0900_ai_ci"
}

# --- Scoped database user ------------------------------------------------------

resource "mysql_user" "construction_service" {
  user = "construction_service"

  host = "%"

  plaintext_password = var.construction_service_db_password

  depends_on = [time_sleep.mysql_firewall_propagation]
}

resource "mysql_grant" "construction_service" {
  user     = mysql_user.construction_service.user
  host     = mysql_user.construction_service.host
  database = azurerm_mysql_flexible_database.construction_service.name

  # The same DML and DDL set as the Design Service: the repositories read and
  # write their own tables, and DbUp needs DDL to apply Migrations/ at startup.
  privileges = [
    "SELECT",
    "INSERT",
    "UPDATE",
    "DELETE",
    "CREATE",
    "ALTER",
    "DROP",
    "INDEX",
    "REFERENCES",
  ]

  depends_on = [time_sleep.mysql_firewall_propagation]
}

# --- Event Hubs --------------------------------------------------------------------

# construction-events: the one topic this service publishes to. Must match
# KafkaConstructionEventPublisher.Topic exactly.
resource "azurerm_eventhub" "construction_events" {
  name         = "construction-events"
  namespace_id = azurerm_eventhub_namespace.main.id

  partition_count   = 1
  message_retention = 7
}

# payment-events: the Payment Service is not deployed yet, but this service's
# PaymentEventsConsumer subscribes to it on startup. Declared here so that
# subscription has a topic to attach to. When the Payment Service is deployed it
# publishes to this same hub and nothing here needs to change.
resource "azurerm_eventhub" "payment_events" {
  name         = "payment-events"
  namespace_id = azurerm_eventhub_namespace.main.id

  partition_count   = 1
  message_retention = 7
}

# Consumer groups. Event Hubs needs each Kafka consumer group to exist as its own
# resource before a consumer can join it. One group per reader, named exactly as
# the service's consumers compute them: the design consumer uses the bare
# ConsumerGroupId, and the project and payment consumers append their topic.
resource "azurerm_eventhub_consumer_group" "design_events" {
  name                = "construction-service"
  eventhub_name       = azurerm_eventhub.design_events.name
  namespace_name      = azurerm_eventhub_namespace.main.name
  resource_group_name = azurerm_resource_group.main.name
}

resource "azurerm_eventhub_consumer_group" "project_events" {
  name                = "construction-service-project-events"
  eventhub_name       = azurerm_eventhub.project_events.name
  namespace_name      = azurerm_eventhub_namespace.main.name
  resource_group_name = azurerm_resource_group.main.name
}

resource "azurerm_eventhub_consumer_group" "payment_events" {
  name                = "construction-service-payment-events"
  eventhub_name       = azurerm_eventhub.payment_events.name
  namespace_name      = azurerm_eventhub_namespace.main.name
  resource_group_name = azurerm_resource_group.main.name
}

# --- App Service -------------------------------------------------------------

resource "azurerm_linux_web_app" "construction_service" {
  name                = var.construction_service_app_name
  resource_group_name = azurerm_resource_group.main.name
  location            = "southeastasia"
  service_plan_id     = azurerm_service_plan.main.id

  https_only = true

  site_config {
    # The outbox dispatcher and three consumers run inside this process. An idled
    # App Service stops draining construction-events and stops reading the topics.
    always_on = true

    health_check_path                 = "/health"
    health_check_eviction_time_in_min = 5

    application_stack {
      dotnet_version = "10.0"
    }
  }

  webdeploy_publish_basic_authentication_enabled = true
  ftp_publish_basic_authentication_enabled       = false

  app_settings = {
    ASPNETCORE_ENVIRONMENT = "Production"

    ConnectionStrings__ConstructionDb = join("", [
      "Server=${azurerm_mysql_flexible_server.main.fqdn};",
      "Port=3306;",
      "Database=${azurerm_mysql_flexible_database.construction_service.name};",
      "User Id=${mysql_user.construction_service.user};",
      "Password=${var.construction_service_db_password};",
      "SslMode=Required;",
    ])

    Jwt__Issuer     = var.jwt_issuer
    Jwt__Audience   = var.jwt_audience
    Jwt__SigningKey = var.jwt_signing_key

    Kafka__BootstrapServers      = local.eventhub_kafka_bootstrap_servers
    Kafka__SecurityProtocol      = "SaslSsl"
    Kafka__SaslMechanism         = "Plain"
    Kafka__SaslUsername          = "$ConnectionString"
    Kafka__SaslPassword          = azurerm_eventhub_namespace.main.default_primary_connection_string
    Kafka__RequestTimeoutMs      = "60000"
    Kafka__SocketKeepaliveEnable = "true"
    Kafka__MetadataMaxAgeMs      = "180000"
    Kafka__MessageTimeoutMs      = "60000"

    # The Project Manager dashboard asks the Project Service which projects the
    # caller is assigned to. Without this those requests answer 502.
    Services__ProjectService__BaseUrl = "https://${azurerm_linux_web_app.project_service.default_hostname}/"
  }

  depends_on = [
    mysql_grant.construction_service,
    azurerm_eventhub.construction_events,
    azurerm_eventhub_consumer_group.design_events,
    azurerm_eventhub_consumer_group.project_events,
    azurerm_eventhub_consumer_group.payment_events,
  ]
}
