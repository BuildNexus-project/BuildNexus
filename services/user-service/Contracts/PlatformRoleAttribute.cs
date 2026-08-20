using System.ComponentModel.DataAnnotations;
using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Contracts;

/// <summary>
/// Accepts only the names of the four BuildNexus roles: <c>Client</c>,
/// <c>Architect</c>, <c>ProjectManager</c>, <c>Admin</c>.
/// </summary>
/// <remarks>
/// Deliberately stricter than <see cref="EnumDataTypeAttribute"/>, which would
/// also accept numeric values such as "3" and any undefined number.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class PlatformRoleAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string role || string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        // Name comparison rather than Enum.TryParse, which would also accept the
        // underlying numbers ("3") as valid roles.
        return Enum.GetNames<UserRole>()
            .Any(name => string.Equals(name, role, StringComparison.OrdinalIgnoreCase));
    }

    public override string FormatErrorMessage(string name) =>
        $"{name} must be one of: {string.Join(", ", Enum.GetNames<UserRole>())}.";
}
