using BuildNexus.UserService.Contracts;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The validation attribute that keeps a role field to the four BuildNexus
/// roles, spelled exactly. No database, no HTTP — this is the rule on its own.
/// </summary>
public class PlatformRoleAttributeTests
{
    private readonly PlatformRoleAttribute _attribute = new();

    [Theory]
    [InlineData("Client")]
    [InlineData("Architect")]
    [InlineData("ProjectManager")]
    [InlineData("Admin")]
    public void IsValid_accepts_every_platform_role_name(string role)
    {
        Assert.True(_attribute.IsValid(role));
    }

    [Theory]
    [InlineData("client")]
    [InlineData("ARCHITECT")]
    [InlineData("projectmanager")]
    public void IsValid_matches_role_names_case_insensitively(string role)
    {
        Assert.True(_attribute.IsValid(role));
    }

    [Fact]
    public void IsValid_accepts_null_because_presence_is_RequiredAttributes_question()
    {
        // The same attribute guards a required role on registration and an
        // optional one on a directory filter — absence is not this rule's call.
        Assert.True(_attribute.IsValid(null));
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("client ")]
    [InlineData("")]
    [InlineData(" ")]
    // Unlike EnumDataTypeAttribute, the underlying numeric value is refused too.
    [InlineData("3")]
    public void IsValid_refuses_anything_that_is_not_an_exact_role_name(string value)
    {
        Assert.False(_attribute.IsValid(value));
    }

    [Fact]
    public void IsValid_refuses_a_value_that_is_not_a_string()
    {
        Assert.False(_attribute.IsValid(3));
    }

    [Fact]
    public void FormatErrorMessage_lists_every_platform_role()
    {
        Assert.Equal(
            "Role must be one of: Client, Architect, ProjectManager, Admin.",
            _attribute.FormatErrorMessage("Role"));
    }
}
