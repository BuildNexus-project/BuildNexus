using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BuildNexus.UserService.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BuildNexus.UserService.Authorization;

/// <summary>
/// Authenticates a call from another BuildNexus service, not a signed-in user.
/// </summary>
/// <remarks>
/// Endpoints under <c>/api/internal</c> are not reachable with a user's own
/// bearer token — forwarding a Client's token the way <c>design-service</c>
/// forwards it to the Project Service would work for endpoints the Client is
/// themselves allowed to call, but a user directory lookup is not one of them
/// for any role. A second scheme, alongside the JWT bearer scheme the rest of
/// this service uses, keeps that boundary explicit: a caller here is trusted
/// because it holds the shared key, not because of who a token says signed in.
/// </remarks>
public class InternalServiceAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The scheme name passed to <c>[Authorize(AuthenticationSchemes = ...)]</c>.</summary>
    public const string SchemeName = "InternalService";

    private const string ApiKeyHeader = "X-Internal-Api-Key";

    private readonly InternalServiceOptions _options;

    public InternalServiceAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<InternalServiceOptions> internalServiceOptions)
        : base(options, logger, encoder)
    {
        _options = internalServiceOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var provided) || provided.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.Fail($"The {ApiKeyHeader} header is required."));
        }

        // Fixed-time comparison: a caller learning the key one byte at a time
        // from response-time differences is exactly what a timing-safe compare
        // closes off, the same reasoning the password hasher's own compare uses.
        var suppliedBytes = Encoding.UTF8.GetBytes(provided[0]!);
        var expectedBytes = Encoding.UTF8.GetBytes(_options.ApiKey);

        if (suppliedBytes.Length != expectedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail("The internal API key is not valid."));
        }

        // No user identity — this principal speaks for a trusted caller service,
        // not for any BuildNexus account.
        var identity = new ClaimsIdentity(SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
