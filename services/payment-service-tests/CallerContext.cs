using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.PaymentService.Tests;

/// <summary>
/// A signed-in caller for the controller tests that use Moq. The same few lines
/// the older controller tests carry privately, kept in one place for the new ones.
/// </summary>
internal static class CallerContext
{
    /// <summary>
    /// A caller in the given role. <paramref name="callerId"/> of <c>null</c>
    /// stands for a token that carried no usable <c>sub</c>.
    /// </summary>
    public static ControllerContext For(Guid? callerId, string role)
    {
        var claims = new List<Claim> { new("role", role) };

        if (callerId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, callerId.Value.ToString()));
        }

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }
}
