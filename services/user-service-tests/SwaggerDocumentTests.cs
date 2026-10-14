using BuildNexus.UserService.ApiDocs;
using BuildNexus.UserService.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// US-36: the generated Swagger document marks anonymous and protected endpoints
/// differently, names the credential each one needs, and carries the controllers'
/// own descriptions.
/// </summary>
/// <remarks>
/// Builds the document from the real controllers with the service's own Swagger
/// setup, but never starts the service — no database, no stack.
/// </remarks>
public class SwaggerDocumentTests
{
    private static readonly OpenApiDocument Document = Generate();

    private static OpenApiDocument Generate()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddServiceSwagger();

        using var app = builder.Build();
        return app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
    }

    private static OpenApiOperation Operation(string path, HttpMethod method) =>
        Document.Paths[path].Operations![method];

    private static List<string> SchemesOf(OpenApiOperation operation) =>
        (operation.Security ?? []).SelectMany(requirement => requirement.Keys.Select(scheme => scheme.Reference.Id!)).ToList();

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    public void An_anonymous_endpoint_is_not_marked_as_needing_a_token(string path)
    {
        var operation = Operation(path, HttpMethod.Post);

        Assert.Empty(SchemesOf(operation));
        Assert.Contains("anonymous", operation.Description);
    }

    [Fact]
    public void A_protected_endpoint_is_marked_with_the_bearer_scheme_and_its_roles()
    {
        var operation = Operation("/api/users", HttpMethod.Get);

        Assert.Equal(["Bearer"], SchemesOf(operation));
        Assert.Contains("Admin", operation.Description);
        Assert.Contains("401", operation.Responses!.Keys);
        Assert.Contains("403", operation.Responses.Keys);
    }

    [Fact]
    public void The_internal_endpoint_asks_for_the_service_key_not_a_bearer_token()
    {
        var operation = Operation("/api/internal/users/{id}", HttpMethod.Get);

        Assert.Equal(["InternalService"], SchemesOf(operation));
    }

    [Fact]
    public void The_jwt_bearer_scheme_is_defined_in_the_document()
    {
        var scheme = Document.Components!.SecuritySchemes!["Bearer"];

        Assert.Equal(SecuritySchemeType.Http, scheme.Type);
        Assert.Equal("bearer", scheme.Scheme);
        Assert.Equal("JWT", scheme.BearerFormat);
    }

    [Fact]
    public void The_controllers_own_descriptions_reach_the_document()
    {
        var operation = Operation("/api/auth/register", HttpMethod.Post);

        Assert.Contains("Registers a new account", operation.Summary);
        Assert.Equal("The email is already registered.", operation.Responses!["409"].Description);
    }
}
