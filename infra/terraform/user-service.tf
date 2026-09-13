# User Service: its own database on the shared MySQL server, and its own App
# Service on the shared plan.
#
# Independently redeployable is the point. Nothing here is shared with another
# service except the plan and the server themselves, so redeploying the User
# Service neither rebuilds nor restarts the other four.

resource "azurerm_mysql_flexible_database" "user_service" {
  # The same database name the local stack uses, so a connection string differs
  # between the two environments only in host and credentials.
  name                = "buildnexus_user_db"
  resource_group_name = azurerm_resource_group.main.name
  server_name         = azurerm_mysql_flexible_server.main.name

  # Matching the mysql:8.0 image's own defaults rather than picking something
  # close to them. The migrations in services/user-service/Migrations declare no
  # charset, so every table inherits whatever the database was created with — if
  # these differed from the container's, a string comparison could sort one way
  # locally and another way in Azure.
  charset   = "utf8mb4"
  collation = "utf8mb4_0900_ai_ci"
}

resource "azurerm_linux_web_app" "user_service" {
  # Globally unique across Azure — this becomes <name>.azurewebsites.net.
  name                = var.user_service_app_name
  resource_group_name = azurerm_resource_group.main.name
  location            = "southeastasia"
  service_plan_id     = azurerm_service_plan.main.id

  # The service issues and accepts bearer tokens. Plain HTTP would put them on
  # the wire in clear text, so the platform redirects it away before the app
  # ever sees the request.
  https_only = true

  site_config {
    # B1 allows this, and without it the platform idles the app out: the next
    # request pays a cold start plus a full DbUp migration check before it gets
    # an answer, which in a live demo reads as the service being down.
    always_on = true

    # Already in Program.cs and already [AllowAnonymous], so no application code
    # changes for this. App Service polls it and recycles an instance that stops
    # answering — which here also means an instance that lost the database,
    # since the app will not start without one.
    health_check_path = "/health"

    # Required by the provider whenever health_check_path is set, and capped at
    # 2-10 by Azure. How long an instance may keep failing /health before the
    # load balancer takes it out of rotation.
    health_check_eviction_time_in_min = 5

    application_stack {
      # Matches <TargetFramework>net10.0</TargetFramework> in UserService.csproj.
      dotnet_version = "10.0"
    }
  }

  # The publish-profile deploy in .github/workflows/ci.yml authenticates to the
  # SCM endpoint with the basic credentials the profile carries. Set explicitly
  # rather than left to the provider default, because the deploy job breaks with
  # a 401 that explains nothing if this is ever off.
  webdeploy_publish_basic_authentication_enabled = true

  # Nothing deploys over FTP. Off, so there is one fewer credentialled way in.
  ftp_publish_basic_authentication_enabled = false

  # --- Application Settings -------------------------------------------------
  #
  # Every one of these arrives as an environment variable, which is what the
  # double underscore is for: ASP.NET Core maps Jwt__SigningKey onto the
  # Jwt:SigningKey configuration key, exactly as infra/docker-compose.yml does
  # locally. Not one of them is hardcoded here — the secrets come from variables
  # with no default, and the connection string is built from the server and
  # database resources above.
  app_settings = {
    # Not Development. That switch turns off Swagger and, more importantly, the
    # AdminSeeder — a bootstrap Admin with a known development password must not
    # exist on a publicly reachable host. See infra/RUNBOOK.md for how the first
    # Admin is created instead.
    ASPNETCORE_ENVIRONMENT = "Production"

    # SslMode=Required because Azure MySQL Flexible Server has
    # require_secure_transport ON and refuses an unencrypted connection.
    # Required rather than VerifyFull: it encrypts without also needing Azure's
    # CA bundle shipped inside the app.
    ConnectionStrings__UserDb = join("", [
      "Server=${azurerm_mysql_flexible_server.main.fqdn};",
      "Port=3306;",
      "Database=${azurerm_mysql_flexible_database.user_service.name};",
      "User Id=${var.mysql_administrator_login};",
      "Password=${var.mysql_administrator_password};",
      "SslMode=Required;",
    ])

    # The shared convention. Every service that validates a BuildNexus token
    # reads these same three variables, so the values cannot drift apart the way
    # five independently written literals could.
    Jwt__Issuer     = var.jwt_issuer
    Jwt__Audience   = var.jwt_audience
    Jwt__SigningKey = var.jwt_signing_key

    # User Service only: the lifetime is baked into a token's exp claim when this
    # service signs it, and every other service merely checks whether exp passed.
    Jwt__AccessTokenLifetimeMinutes = "20"

    # Not named in the story's acceptance criteria, but the service will not
    # start without it: Program.cs binds InternalServiceOptions with
    # ValidateOnStart() and a 32-byte minimum, so a missing key fails the host at
    # boot rather than at the first /api/internal call.
    InternalService__ApiKey = var.internal_service_api_key

    # The reset link points at the React app, not at this service. Still the
    # local origin, because the frontend has no deployed home yet — the story
    # that gives it one sets frontend_origin and nothing else here changes.
    PasswordReset__ResetUrlTemplate = "${var.frontend_origin}/reset-password?token={token}"

    # Email__SmtpHost is deliberately unset. Left blank, Program.cs resolves
    # IEmailSender to LoggingEmailSender and reset emails go to the App Service
    # log stream instead of being sent. There is no mail relay in this stack and
    # a deployed environment mailing real addresses is not something to switch on
    # by accident.
  }

}
