namespace BuildNexus.ConstructionService.Contracts;

/// <summary>
/// The body of <c>PUT /api/construction/milestones/{id}/due-date</c>: the day a milestone
/// should be finished by, or nothing to clear it.
/// </summary>
public class SetMilestoneDueDateRequest
{
    /// <summary>
    /// The day, as <c>yyyy-MM-dd</c>, or <c>null</c> to clear the date. A value that is not a
    /// real date is refused as a 400 before the action runs.
    /// </summary>
    public DateOnly? DueDate { get; set; }
}
