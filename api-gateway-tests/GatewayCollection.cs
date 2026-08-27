namespace BuildNexus.ApiGateway.Tests;

/// <summary>
/// Groups every test class that boots the gateway into one xUnit collection.
/// </summary>
/// <remarks>
/// xUnit runs test classes in parallel by default. These classes share one set
/// of stubs and assert on what those stubs did or did not receive, so running
/// them at the same time would let one class see another's traffic. Sharing a
/// collection makes them run one after another, against a single host.
/// </remarks>
[CollectionDefinition(Name)]
public class GatewayCollection : ICollectionFixture<GatewayFactory>
{
    public const string Name = "API Gateway host";
}
