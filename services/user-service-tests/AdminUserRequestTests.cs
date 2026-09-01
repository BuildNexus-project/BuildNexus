using System.ComponentModel.DataAnnotations;
using BuildNexus.UserService.Contracts;
using BuildNexus.UserService.Models;

namespace BuildNexus.UserService.Tests;

/// <summary>
/// The rules the Admin account-management payloads are held to before anything
/// reaches the database (US-37). Pure validation, so these run without the
/// development database.
/// </summary>
public class AdminUserRequestTests
{
    // ------------------------------------------------------ the edit payload ---

    [Fact]
    public void An_edit_carrying_a_name_an_email_and_a_role_is_accepted()
    {
        Assert.Empty(Validate(Edit()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public void An_edit_with_a_missing_or_too_short_name_is_refused(string fullName)
    {
        Assert.Contains(Validate(Edit(fullName: fullName)), NamingField(nameof(AdminUpdateUserRequest.FullName)));
    }

    [Fact]
    public void An_edit_with_a_name_longer_than_the_column_holds_is_refused()
    {
        Assert.Contains(
            Validate(Edit(fullName: new string('a', 151))),
            NamingField(nameof(AdminUpdateUserRequest.FullName)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("two@at@example.com")]
    public void An_edit_with_an_unusable_email_is_refused(string email)
    {
        // The email is the login identity: moving an account onto something
        // nobody can sign in with is worse than refusing the edit.
        //
        // As permissive as registration and no more — the same [EmailAddress]
        // guards both, so an address one accepts the other does too. It wants an
        // @ with something either side; a single-label domain gets through, and
        // deciding a domain really exists is not validation's job.
        Assert.Contains(Validate(Edit(email: email)), NamingField(nameof(AdminUpdateUserRequest.Email)));
    }

    [Fact]
    public void An_edit_with_an_email_longer_than_the_column_holds_is_refused()
    {
        var email = new string('a', 250) + "@example.com";

        Assert.Contains(Validate(Edit(email: email)), NamingField(nameof(AdminUpdateUserRequest.Email)));
    }

    [Theory]
    [InlineData(nameof(UserRole.Client))]
    [InlineData(nameof(UserRole.Architect))]
    [InlineData(nameof(UserRole.ProjectManager))]
    [InlineData(nameof(UserRole.Admin))]
    public void An_edit_may_name_any_of_the_four_roles(string role)
    {
        // Admin included, unlike registration: promoting an administrator is
        // exactly what this endpoint is for.
        Assert.Empty(Validate(Edit(role: role)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Superuser")]
    [InlineData("3")]
    public void An_edit_naming_a_role_the_platform_does_not_have_is_refused(string role)
    {
        // "3" is the trap EnumDataType would fall into — the underlying number
        // is not a role name.
        Assert.Contains(Validate(Edit(role: role)), NamingField(nameof(AdminUpdateUserRequest.Role)));
    }

    // ------------------------------------------------ the deactivate payload ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_status_change_carrying_the_flag_is_accepted(bool isActive)
    {
        Assert.Empty(Validate(new SetUserActiveRequest { IsActive = isActive }));
    }

    [Fact]
    public void A_status_change_with_no_flag_at_all_is_refused()
    {
        // Nullable for exactly this: an omitted flag must not arrive as a silent
        // false. Deactivating is not a thing to do by accident.
        Assert.Contains(
            Validate(new SetUserActiveRequest()),
            NamingField(nameof(SetUserActiveRequest.IsActive)));
    }

    // ----------------------------------------------------- the listing query ---

    [Fact]
    public void A_directory_query_that_asks_for_nothing_is_accepted()
    {
        var query = new UserListQuery();

        Assert.Empty(Validate(query));
        Assert.Equal(1, query.Page);
        Assert.Equal(UserListQuery.DefaultPageSize, query.PageSize);
        Assert.Null(query.ParsedRole());
    }

    [Theory]
    [InlineData(nameof(UserRole.Client), UserRole.Client)]
    [InlineData("architect", UserRole.Architect)]
    [InlineData(nameof(UserRole.ProjectManager), UserRole.ProjectManager)]
    public void A_directory_query_may_narrow_to_one_role(string role, UserRole expected)
    {
        var query = new UserListQuery { Role = role };

        Assert.Empty(Validate(query));
        Assert.Equal(expected, query.ParsedRole());
    }

    [Theory]
    [InlineData("Superuser")]
    [InlineData("2")]
    public void A_directory_query_filtering_on_a_role_that_does_not_exist_is_refused(string role)
    {
        Assert.Contains(Validate(new UserListQuery { Role = role }), NamingField(nameof(UserListQuery.Role)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_directory_query_asking_for_a_page_before_the_first_is_refused(int page)
    {
        // Pages are 1-based, so ?page=0 is a caller misunderstanding the API
        // rather than a request for the first page.
        Assert.Contains(Validate(new UserListQuery { Page = page }), NamingField(nameof(UserListQuery.Page)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(UserListQuery.MaxPageSize + 1)]
    public void A_directory_query_asking_for_an_unusable_page_size_is_refused(int pageSize)
    {
        // Refused rather than quietly clamped, so a page that comes back short
        // always means the rows ran out.
        Assert.Contains(
            Validate(new UserListQuery { PageSize = pageSize }),
            NamingField(nameof(UserListQuery.PageSize)));
    }

    [Fact]
    public void A_directory_query_asking_for_the_largest_page_allowed_is_accepted()
    {
        Assert.Empty(Validate(new UserListQuery { PageSize = UserListQuery.MaxPageSize }));
    }

    private static AdminUpdateUserRequest Edit(
        string fullName = "Nimal Fernando",
        string email = "nimal@example.com",
        string role = nameof(UserRole.Architect)) => new()
    {
        FullName = fullName,
        Email = email,
        Role = role
    };

    /// <summary>
    /// A message pinned to one property. <see cref="Predicate{T}"/> rather than
    /// <see cref="Func{T, TResult}"/> so it binds to the xUnit overload the
    /// other request tests use inline.
    /// </summary>
    private static Predicate<ValidationResult> NamingField(string field) =>
        result => result.MemberNames.Contains(field);

    /// <summary>
    /// Runs the payload through the same validator ASP.NET Core applies before
    /// the action method is entered.
    /// </summary>
    private static List<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        return results;
    }
}
