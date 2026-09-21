using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildNexus.ConstructionService.Contracts;
using BuildNexus.ConstructionService.Models;

namespace BuildNexus.ConstructionService.Tests;

/// <summary>
/// Pins the wire contract of <see cref="UpdateMilestoneStatusRequest"/>: an
/// empty body is refused (rather than silently binding to
/// <see cref="MilestoneStatus.NotStarted"/> and demoting a Completed
/// milestone), and the integer form of the enum is refused (rather than
/// binding to whichever member happens to sit at that ordinal).
/// </summary>
/// <remarks>
/// These are not naked-controller unit tests: the bugs they cover live at
/// the JSON-binder + validation seam, one layer below where the controller
/// runs. Testing them against <see cref="Validator"/> and
/// <see cref="JsonSerializer"/> directly reaches the same code the ASP.NET
/// pipeline runs, without spinning up a WebApplicationFactory.
/// </remarks>
public class UpdateMilestoneStatusRequestTests
{
    /// <summary>
    /// The same JSON options <c>Program.cs</c> registers, kept in one place
    /// so a change there is a change here — tests would then fail
    /// meaningfully rather than silently drift from the app's real behaviour.
    /// </summary>
    private static readonly JsonSerializerOptions AppJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    // ---------- [Required] on the nullable enum ----------

    [Fact]
    public void Status_is_required_so_an_empty_body_fails_validation()
    {
        // The bug this pins: a plain (non-nullable) MilestoneStatus property
        // silently binds to NotStarted on an empty body, and [Required] on
        // a value type never fires — so a PATCH {} would return 200 and
        // demote a Completed milestone. Nullable + [Required] rejects the
        // empty body as a 400 before the controller runs.
        var request = new UpdateMilestoneStatusRequest { Status = null };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("status is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Any_of_the_three_enum_members_passes_validation()
    {
        foreach (var status in Enum.GetValues<MilestoneStatus>())
        {
            var request = new UpdateMilestoneStatusRequest { Status = status };
            var results = new List<ValidationResult>();

            var valid = Validator.TryValidateObject(
                request,
                new ValidationContext(request),
                results,
                validateAllProperties: true);

            Assert.True(valid, $"Validation failed for {status}: {string.Join(", ", results.Select(r => r.ErrorMessage))}");
        }
    }

    // ---------- JsonStringEnumConverter(allowIntegerValues: false) ----------

    [Fact]
    public void Deserialising_a_string_enum_name_binds_to_the_matching_member()
    {
        var request = JsonSerializer.Deserialize<UpdateMilestoneStatusRequest>(
            """{"status":"InProgress"}""",
            AppJsonOptions);

        Assert.NotNull(request);
        Assert.Equal(MilestoneStatus.InProgress, request!.Status);
    }

    [Fact]
    public void Deserialising_a_missing_status_field_leaves_the_property_null()
    {
        // The JSON binder's own behaviour, before validation runs: an absent
        // field on a nullable property binds as null. Combined with
        // [Required] above, that null then fails validation — which is the
        // whole point of the nullable-plus-Required shape.
        var request = JsonSerializer.Deserialize<UpdateMilestoneStatusRequest>(
            "{}",
            AppJsonOptions);

        Assert.NotNull(request);
        Assert.Null(request!.Status);
    }

    [Fact]
    public void Deserialising_an_integer_status_is_refused()
    {
        // The bug this pins: the default JsonStringEnumConverter accepts
        // integers as a compat feature, so {"status": 1} would silently
        // bind to whichever member sits at ordinal 1 (currently InProgress),
        // and a later reordering of the enum would silently change what
        // "1" means on every existing wire message. allowIntegerValues:
        // false makes the string name the only accepted shape.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateMilestoneStatusRequest>(
            """{"status":1}""",
            AppJsonOptions));
    }

    [Fact]
    public void Deserialising_a_string_that_is_not_one_of_the_three_names_is_refused()
    {
        // The whole-word enum boundary — a typo or a hypothetical fourth
        // state name never binds to something surprising.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateMilestoneStatusRequest>(
            """{"status":"Started"}""",
            AppJsonOptions));
    }
}