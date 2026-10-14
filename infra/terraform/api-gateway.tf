# API Gateway: an App Service on the shared plan, and nothing else.
#
# It has no database and no Event Hub. It is a YARP reverse proxy: the routing
# table is in api-gateway/appsettings.json, and everything that differs between
# environments — the cluster addresses, the allowed CORS origin, the JWT
# convention — arrives as an application setting below.
#
# Independently redeployable, like every service: nothing here is shared with
# another service except the plan itself, so redeploying the gateway neither
# rebuilds nor restarts any of them.

resource "azurerm_linux_web_app" "api_gateway" {
  # Globally unique across Azure — this becomes <name>.azurewebsites.net.
  name                = var.api_gateway_app_name
  resource_group_name = azurerm_resource_group.main.name
  location            = "southeastasia"
  service_plan_id     = azurerm_service_plan.main.id

  # Every call the frontend makes carries a bearer token, and this is the one
  # host the browser talks to. Plain HTTP is redirected away before the app sees
  # the request, so a token never crosses the wire in clear text.
  https_only = true

  site_config {
    # Without it the platform idles the app out, and the first request after a
    # quiet spell pays a cold start in front of every service behind it.
    always_on = true

    # Answered by the gateway itself and [AllowAnonymous]; it deliberately does
    # not call downstream, so one service being down never gets the gateway
    # recycled by App Service's health check.
    health_check_path                 = "/health"
    health_check_eviction_time_in_min = 5

    application_stack {
      # Matches <TargetFramework>net10.0</TargetFramework> in
      # BuildNexus.ApiGateway.csproj.
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
  # the api-gateway block of infra/docker-compose.yml does locally.
  app_settings = {
    # Not Development.
    ASPNETCORE_ENVIRONMENT = "Production"

    # The shared convention, from the same three variables as every service. The
    # gateway validates the signature before it proxies anything, so a mismatch
    # with the User Service does not fail quietly: every request comes back 401.
    # It refuses to start below 32 bytes.
    Jwt__Issuer     = var.jwt_issuer
    Jwt__Audience   = var.jwt_audience
    Jwt__SigningKey = var.jwt_signing_key

    # The one origin allowed to call the gateway from a browser. Configured here
    # once and nowhere else — the services behind it do no CORS of their own. The
    # frontend has no deployed home yet, so this is still the local dev server.
    Cors__AllowedOrigins__0 = var.frontend_origin

    # The deployed frontend origins, which were added to the App Service by hand and
    # are listed here so a Terraform apply does not remove them: the Static Web App,
    # and the custom domain with and without www.
    Cors__AllowedOrigins__1 = "https://delightful-grass-0feec4c00.3.azurestaticapps.net"
    Cors__AllowedOrigins__2 = "https://buildnexus.me"
    Cors__AllowedOrigins__3 = "https://www.buildnexus.me"

    # Where each cluster points: the deployed App Service, over HTTPS, with the
    # trailing slash YARP expects. Left to appsettings.json these would be the
    # local dotnet run ports, and every proxied call would answer 502.
    ReverseProxy__Clusters__user__Destinations__primary__Address    = "https://${azurerm_linux_web_app.user_service.default_hostname}/"
    ReverseProxy__Clusters__project__Destinations__primary__Address = "https://${azurerm_linux_web_app.project_service.default_hostname}/"
    ReverseProxy__Clusters__design__Destinations__primary__Address  = "https://${azurerm_linux_web_app.design_service.default_hostname}/"

    ReverseProxy__Clusters__construction__Destinations__primary__Address = "https://${azurerm_linux_web_app.construction_service.default_hostname}/"
    ReverseProxy__Clusters__payment__Destinations__primary__Address      = "https://${azurerm_linux_web_app.payment_service.default_hostname}/"
  }
}
