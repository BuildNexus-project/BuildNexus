using BuildNexus.PaymentService.ApiDocs;
using BuildNexus.PaymentService.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// US-36: the generated Swagger document defines the JWT bearer scheme, marks every
/// endpoint as needing it, and names the roles each one allows.
/// </summary>
/// <remarks>
/// Builds the document from the real controllers with the service's own Swagger
/// setup, but never starts the service — no database, no Kafka.
/// </remarks>
public class SwaggerDocumentTests
{
    private static readonly OpenApiDocument Document = Generate();

    private static OpenApiDocument Generate()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(DashboardController).Assembly);
        builder.Services.AddServiceSwagger();

        using var app = builder.Build();
        return app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
    }

    private static IEnumerable<(string Path, HttpMethod Method, OpenApiOperation Operation)> Operations() =>
        Document.Paths.SelectMany(path => path.Value.Operations!.Select(op => (path.Key, op.Key, op.Value)));

    [Fact]
    public void The_jwt_bearer_scheme_is_defined_in_the_document()
    {
        var scheme = Document.Components!.SecuritySchemes!["Bearer"];

        Assert.Equal(SecuritySchemeType.Http, scheme.Type);
        Assert.Equal("bearer", scheme.Scheme);
        Assert.Equal("JWT", scheme.BearerFormat);
    }

    [Fact]
    public void Every_endpoint_is_marked_as_needing_a_bearer_token()
    {
        // This service has no anonymous controller endpoint: the only open route
        // is /health, which is a minimal endpoint outside the document.
        var unmarked = Operations()
            .Where(op => (op.Operation.Security ?? []).SelectMany(r => r.Keys).All(s => s.Reference.Id != "Bearer"))
            .Select(op => $"{op.Method} {op.Path}")
            .ToList();

        Assert.True(unmarked.Count == 0, "Not marked as needing a bearer token: " + string.Join(", ", unmarked));
    }

    [Fact]
    public void Every_endpoint_names_its_allowed_roles_and_the_401_and_403_responses()
    {
        var incomplete = Operations()
            .Where(op => !op.Operation.Description!.Contains("with the role")
                || !op.Operation.Responses!.ContainsKey("401")
                || !op.Operation.Responses.ContainsKey("403"))
            .Select(op => $"{op.Method} {op.Path}")
            .ToList();

        Assert.True(incomplete.Count == 0, "Missing roles or 401/403: " + string.Join(", ", incomplete));
    }

    [Fact]
    public void A_dashboard_endpoint_names_the_role_that_may_call_it()
    {
        var operation = Document.Paths["/api/payments/dashboard/client"].Operations![HttpMethod.Get];

        Assert.Contains("Client", operation.Description);
    }
}
