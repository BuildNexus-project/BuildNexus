using System.Reflection;
using Microsoft.OpenApi;

namespace BuildNexus.PaymentService.ApiDocs;

/// <summary>
/// The Swagger/OpenAPI configuration for this service, in one place so
/// <c>Program.cs</c> and the tests that check the generated document build it the same way.
/// </summary>
public static class SwaggerSetup
{
    public static IServiceCollection AddServiceSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "BuildNexus Payment Service", Version = "v1" });

            options.AddSecurityDefinition(AuthorizationOperationFilter.BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access token returned by the User Service's /api/auth/login."
            });

            // Turns the /// summaries and <response> tags on the controllers into
            // the descriptions Swagger UI shows. Registered before the filter below:
            // this one sets the description, that one appends to it.
            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

            if (File.Exists(xmlFile))
            {
                options.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
            }

            // Which schemes and roles an endpoint needs is added per operation,
            // not globally, so anonymous endpoints are not shown as locked.
            options.OperationFilter<AuthorizationOperationFilter>();
        });

        return services;
    }
}
