using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BuildNexus.ConstructionService.ApiDocs;

/// <summary>
/// Marks each operation in the Swagger document with what it actually needs:
/// nothing for an <c>[AllowAnonymous]</c> endpoint, a bearer token plus the
/// allowed roles for everything else.
/// </summary>
/// <remarks>
/// Replaces one global security requirement. That marked every endpoint — login
/// and register included — as locked, so Swagger UI showed a padlock on the very
/// calls that hand out the token, and "authenticated endpoints clearly marked"
/// could not be told from "all of them". Reads the same metadata the
/// authorization middleware reads, so the page cannot disagree with the service.
/// </remarks>
public class AuthorizationOperationFilter : IOperationFilter
{
    /// <summary>The security scheme every ordinary endpoint is authenticated with.</summary>
    public const string BearerScheme = "Bearer";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            AppendDescription(operation, "**Access:** anonymous — no token needed.");
            return;
        }

        var authorize = metadata.OfType<IAuthorizeData>().ToList();

        // An endpoint with no [Authorize] at all is still protected: the
        // deny-by-default fallback policy in Program.cs requires a signed-in user.
        var schemes = authorize
            .SelectMany(data => (data.AuthenticationSchemes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct()
            .ToList();

        if (schemes.Count == 0)
        {
            schemes.Add(BearerScheme);
        }

        operation.Security ??= [];

        foreach (var scheme in schemes)
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(scheme, context.Document)] = []
            });
        }

        var roles = authorize
            .Select(data => data.Roles)
            .Where(roles => !string.IsNullOrWhiteSpace(roles))
            .Select(roles => string.Join(" or ", roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .ToList();

        var credential = schemes.Contains(BearerScheme) ? "a JWT bearer token" : "the internal service key";

        AppendDescription(
            operation,
            roles.Count == 0
                ? $"**Access:** requires {credential}."
                : $"**Access:** requires {credential} with the role {string.Join(" and ", roles)}.");

        operation.Responses ??= new OpenApiResponses();

        if (!operation.Responses.ContainsKey("401"))
        {
            operation.Responses.Add("401", new OpenApiResponse { Description = "No valid credential was sent." });
        }

        if (roles.Count > 0 && !operation.Responses.ContainsKey("403"))
        {
            operation.Responses.Add("403", new OpenApiResponse { Description = "The signed-in user's role is not allowed to call this endpoint." });
        }
    }

    private static void AppendDescription(OpenApiOperation operation, string line)
    {
        operation.Description = string.IsNullOrWhiteSpace(operation.Description)
            ? line
            : $"{operation.Description}\n\n{line}";
    }
}
