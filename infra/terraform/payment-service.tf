# Payment Service: its own database on the shared MySQL server, its own MySQL user
# that can reach that database and nothing else, its own Event Hub to publish to,
# consumer groups on the two Event Hubs it reads, and its own App Service on the
# shared plan. Nothing new is added to the shared server, namespace or plan.

resource "azurerm_mysql_flexible_database" "payment_service" {
  name                = "buildnexus_payment_db"
  resource_group_name = azurerm_resource_group.main.name
  server_name         = azurerm_mysql_flexible_server.main.name

  charset   = "utf8mb4"
  collation = "utf8mb4_0900_ai_ci"
}

# --- Scoped database user ------------------------------------------------------

resource "mysql_user" "payment_service" {
  user = "payment_service"

  host = "%"

  plaintext_password = var.payment_service_db_password

  depends_on = [time_sleep.mysql_firewall_propagation]
}

resource "mysql_grant" "payment_service" {
  user     = mysql_user.payment_service.user
  host     = mysql_user.payment_service.host
  database = azurerm_mysql_flexible_database.payment_service.name

  # The same DML and DDL set as the other services: the repositories read and
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

# payment-events: the one topic this service publishes to. Must match
# KafkaPaymentEventPublisher.Topic exactly. The Construction Service's
# PaymentEventsConsumer reads it, through its own consumer group in
# construction-service.tf; it was declared there first, before this service
# existed, and moved here so the publisher owns its topic like every other service.
resource "azurerm_eventhub" "payment_events" {
  name         = "payment-events"
  namespace_id = azurerm_eventhub_namespace.main.id

  partition_count   = 1
  message_retention = 7
}

# Consumer groups. Event Hubs needs each Kafka consumer group to exist as its own
# resource before a consumer can join it. Named exactly as the consumers compute
# them: Kafka:ConsumerGroupId ("payment-service") with the topic appended.
resource "azurerm_eventhub_consumer_group" "payment_service_project_events" {
  name                = "payment-service-project-events"
  eventhub_name       = azurerm_eventhub.project_events.name
  namespace_name      = azurerm_eventhub_namespace.main.name
  resource_group_name = azurerm_resource_group.main.name
}

resource "azurerm_eventhub_consumer_group" "payment_service_construction_events" {
  name                = "payment-service-construction-events"
  eventhub_name       = azurerm_eventhub.construction_events.name
  namespace_name      = azurerm_eventhub_namespace.main.name
  resource_group_name = azurerm_resource_group.main.name
}

# --- App Service -------------------------------------------------------------

resource "azurerm_linux_web_app" "payment_service" {
  name                = var.payment_service_app_name
  resource_group_name = azurerm_resource_group.main.name
  location            = "southeastasia"
  service_plan_id     = azurerm_service_plan.main.id

  https_only = true

  site_config {
    # The outbox dispatcher and two consumers run inside this process. An idled
    # App Service stops draining payment-events and stops reading the topics.
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

    ConnectionStrings__PaymentDb = join("", [
      "Server=${azurerm_mysql_flexible_server.main.fqdn};",
      "Port=3306;",
      "Database=${azurerm_mysql_flexible_database.payment_service.name};",
      "User Id=${mysql_user.payment_service.user};",
      "Password=${var.payment_service_db_password};",
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

    # No Services__* settings: the Payment Service calls no other service over
    # HTTP. Everything it learns about projects arrives as events.
  }

  depends_on = [
    mysql_grant.payment_service,
    azurerm_eventhub.payment_events,
    azurerm_eventhub_consumer_group.payment_service_project_events,
    azurerm_eventhub_consumer_group.payment_service_construction_events,
  ]
}
