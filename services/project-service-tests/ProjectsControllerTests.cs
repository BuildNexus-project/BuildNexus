using System.Security.Claims;
using BuildNexus.ProjectService.Configuration;
using BuildNexus.ProjectService.Contracts;
using BuildNexus.ProjectService.Controllers;
using BuildNexus.ProjectService.Data;
using BuildNexus.ProjectService.Messaging;
using BuildNexus.ProjectService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildNexus.ProjectService.Tests;

/// <summary>
/// The US-05 acceptance criteria walked over stand-in collaborators: the
/// project is created with status <c>Pending</c>, and <c>ProjectCreated</c> is
/// published once it is stored.
/// </summary>
/// <remarks>
/// The repository and the publisher are stand-ins, so these need neither MySQL
/// nor a Kafka broker. What is under test is the action's own decisions — what
/// it stores, where the client id comes from, what it publishes and when — not
/// the SQL or the produce call, neither of which can be exercised without the
/// real thing running.
/// </remarks>
public class ProjectsControllerTests
{
    private static readonly Guid ClientId = Guid.Parse("7a91c3d2-6f14-4b8e-9c05-1d2e3f4a5b6c");

    [Fact]
    public async Task Creates_the_project_with_status_pending()
    {
        // The AC names the status outright, and the caller has no say in it.
        var (controller, repository, _) = ControllerFor();

        await controller.CreateProject(Request());

        Assert.Equal(ProjectStatus.Pending, repository.Inserted!.Status);
    }

    [Fact]
    public async Task Stores_every_requirement_the_form_captured()
    {
        var (controller, repository, _) = ControllerFor();

        await controller.CreateProject(Request());

        var stored = repository.Inserted!;
        Assert.Equal("Beachfront villa", stored.Name);
        Assert.Equal("Galle", stored.Location);
        Assert.Equal(25.5m, stored.LandSizePerches);
        Assert.Equal(18_500_000m, stored.Budget);
        Assert.Equal(2, stored.Floors);
        Assert.Equal(4, stored.Bedrooms);
        Assert.Equal(3, stored.Bathrooms);
        Assert.Equal(2, stored.GarageSpaces);
        Assert.Equal("Solar hot water", stored.OtherRequirements);
    }

    [Fact]
    public async Task Records_the_caller_as_the_client()
    {
        // The owner comes from the token, not the payload — there is no field
        // on the request to send somebody else's id in.
        var (controller, repository, _) = ControllerFor();

        await controller.CreateProject(Request());

        Assert.Equal(ClientId, repository.Inserted!.ClientId);
    }

    [Fact]
    public async Task Gives_the_project_an_id_of_its_own()
    {
        var (controller, repository, _) = ControllerFor();

        await controller.CreateProject(Request());

        Assert.NotEqual(Guid.Empty, repository.Inserted!.Id);
    }

    [Fact]
    public async Task Trims_the_name_and_location()
    {
        var (controller, repository, _) = ControllerFor();

        await controller.CreateProject(Request(name: "  Beachfront villa  ", location: "  Galle  "));

        Assert.Equal("Beachfront villa", repository.Inserted!.Name);
        Assert.Equal("Galle", repository.Inserted.Location);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Stores_empty_other_requirements_as_null(string? blank)
    {
        // A requirements box the Client left alone must store nothing, which is
        // also what a project submitted without one has.
        var (controller, repository, _) = ControllerFor();

        await controller.CreateProject(Request(otherRequirements: blank));

        Assert.Null(repository.Inserted!.OtherRequirements);
    }

    [Fact]
    public async Task Answers_201_with_the_project_as_stored()
    {
        var (controller, repository, _) = ControllerFor();

        var result = Assert.IsType<ObjectResult>(await controller.CreateProject(Request()));

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);

        var response = Assert.IsType<ProjectResponse>(result.Value);
        Assert.Equal(repository.Inserted!.Id, response.Id);
        Assert.Equal(ClientId, response.ClientId);
        Assert.Equal("Pending", response.Status);
    }

    [Fact]
    public async Task Publishes_ProjectCreated_once_the_project_is_stored()
    {
        // The third AC bullet. The event carries the project that was actually
        // written, not the request that asked for it.
        var (controller, repository, publisher) = ControllerFor();

        await controller.CreateProject(Request());

        Assert.Equal(1, publisher.PublishCount);
        Assert.Same(repository.Inserted, publisher.Published);
    }

    [Fact]
    public async Task Does_not_publish_when_the_project_could_not_be_stored()
    {
        // Announcing a project that was never written would leave four other
        // services acting on something that does not exist.
        var (controller, _, publisher) = ControllerFor(insertFails: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.CreateProject(Request()));

        Assert.Equal(0, publisher.PublishCount);
    }

    [Fact]
    public async Task Still_answers_201_when_the_event_cannot_be_published()
    {
        // The row is already committed by then. A 500 would tell the Client
        // their submission was lost when it was not, and their retry would
        // create a second project.
        var (controller, repository, _) = ControllerFor(publishFails: true);

        var result = Assert.IsType<ObjectResult>(await controller.CreateProject(Request()));

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        Assert.NotNull(repository.Inserted);
    }

    [Fact]
    public async Task Refuses_a_token_that_carries_no_usable_subject()
    {
        // Signature-valid but unusable: there is no client to attribute the
        // project to, so there is nothing to create.
        var (controller, repository, publisher) = ControllerFor(subject: "not-a-guid");

        Assert.IsType<UnauthorizedResult>(await controller.CreateProject(Request()));

        Assert.Null(repository.Inserted);
        Assert.Equal(0, publisher.PublishCount);
    }

    [Fact]
    public async Task Stamps_created_and_updated_with_the_same_moment()
    {
        var (controller, repository, _) = ControllerFor();

        await controller.CreateProject(Request());

        var stored = repository.Inserted!;
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
    }

    private static (ProjectsController Controller, StubProjectRepository Repository, StubEventPublisher Publisher)
        ControllerFor(
            bool insertFails = false,
            bool publishFails = false,
            string? subject = null)
    {
        var repository = new StubProjectRepository { Fails = insertFails };
        var publisher = new StubEventPublisher { Fails = publishFails };

        var controller = new ProjectsController(
            repository,
            publisher,
            NullLogger<ProjectsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    // The claims a token validated by this service leaves behind:
                    // "sub" and "role" in their short form, not rewritten into
                    // the WS-Federation URIs.
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, subject ?? ClientId.ToString()),
                        new Claim(JwtOptions.RoleClaimType, "Client")
                    ], "TestAuth"))
                }
            }
        };

        return (controller, repository, publisher);
    }

    private static CreateProjectRequest Request(
        string name = "Beachfront villa",
        string location = "Galle",
        string? otherRequirements = "Solar hot water") => new()
        {
            Name = name,
            Location = location,
            LandSizePerches = 25.5m,
            Budget = 18_500_000m,
            Floors = 2,
            Bedrooms = 4,
            Bathrooms = 3,
            GarageSpaces = 2,
            OtherRequirements = otherRequirements
        };

    /// <summary>Records what the action asked to store, instead of storing it.</summary>
    private sealed class StubProjectRepository : IProjectRepository
    {
        public Project? Inserted { get; private set; }

        public bool Fails { get; init; }

        public Task InsertAsync(Project project)
        {
            if (Fails)
            {
                throw new InvalidOperationException("The insert failed.");
            }

            Inserted = project;
            return Task.CompletedTask;
        }
    }

    /// <summary>Records what the action asked to publish, instead of publishing it.</summary>
    private sealed class StubEventPublisher : IProjectEventPublisher
    {
        public Project? Published { get; private set; }

        public int PublishCount { get; private set; }

        public bool Fails { get; init; }

        public Task PublishProjectCreatedAsync(Project project, CancellationToken cancellationToken = default)
        {
            if (Fails)
            {
                // What an unreachable broker looks like from the action's side.
                throw new InvalidOperationException("The broker could not be reached.");
            }

            Published = project;
            PublishCount++;
            return Task.CompletedTask;
        }
    }
}
