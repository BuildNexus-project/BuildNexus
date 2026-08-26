namespace BuildNexus.UserService.Tests;

/// <summary>
/// Groups every test class that talks to the development database into one
/// xUnit collection.
/// </summary>
/// <remarks>
/// xUnit runs each test class in parallel by default. These classes share one
/// database and the factory's cleanup removes every <c>test-</c> account when it
/// is disposed, so running them at the same time would let one class delete the
/// accounts another is still signed in as. Sharing a collection makes them run
/// one after another, against a single host.
/// </remarks>
[CollectionDefinition(Name)]
public class UserServiceCollection : ICollectionFixture<UserServiceFactory>
{
    public const string Name = "User Service host";
}
