namespace BuildNexus.DesignService.Tests;

/// <summary>
/// Groups every test class that talks to the development database into one
/// xUnit collection.
/// </summary>
/// <remarks>
/// xUnit runs test classes in parallel by default. These share one database and
/// the fixture's cleanup removes every <c>test-</c> document when it is
/// disposed, so running them at the same time would let one class delete rows
/// another is still reading. Sharing a collection makes them run one after
/// another, against a single fixture.
/// </remarks>
[CollectionDefinition(Name)]
public class DesignDatabaseCollection : ICollectionFixture<DesignDatabaseFixture>
{
    public const string Name = "Design Service database";
}
