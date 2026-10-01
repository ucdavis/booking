using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Emulation;
using Server.Services;

namespace Server.Tests.Controllers;

public class EmulationControllerTests
{
    [Fact]
    public void Endpoints_require_authentication_and_limit_start_and_search_to_site_admins()
    {
        typeof(EmulationController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).Should().NotBeEmpty();
        foreach (var actionName in new[] { nameof(EmulationController.Candidates), nameof(EmulationController.Start) })
        {
            typeof(EmulationController).GetMethod(actionName)!.GetCustomAttribute<AuthorizeAttribute>()!
                .Policy.Should().Be(AuthenticationHelper.SiteAdminPolicy);
        }
        foreach (var actionName in new[] { nameof(EmulationController.Stop), nameof(EmulationController.Antiforgery) })
        {
            typeof(EmulationController).GetMethod(actionName)!.GetCustomAttributes<AuthorizeAttribute>()
                .Should().NotContain(attribute => attribute.Policy == AuthenticationHelper.SiteAdminPolicy);
        }
        foreach (var actionName in new[] { nameof(EmulationController.Start), nameof(EmulationController.Stop) })
        {
            var action = typeof(EmulationController).GetMethod(actionName)!;
            action.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
            action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Search_rejects_empty_inputs(string? query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);

        (await controller.Candidates(query)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.Candidates(new string('x', 129))).Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Search_trims_identifiers_without_creating_users()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);
        db.People.Add(new Person { IamId = "1000000001", FullName = "Target", IsActiveInIam = true });
        await db.SaveChangesAsync();

        var result = await controller.Candidates(" 1000000001 ");

        var matches = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<List<EmulationCandidateResponse>>().Subject;
        matches.Should().ContainSingle().Which.HasUserAccount.Should().BeFalse();
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Revoked_actor_admin_cannot_search_or_start_even_if_target_is_admin()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);
        (await db.Users.SingleAsync()).IsAdmin = false;
        db.Users.Add(new User { IamId = "target", Name = "Target", IsAdmin = true });
        await db.SaveChangesAsync();
        controller.HttpContext.User = Principal("target");

        (await controller.Candidates("target")).Result.Should().BeOfType<ForbidResult>();
        (await controller.Start(new StartEmulationRequest { IamId = "target" })).Should().BeOfType<ForbidResult>();
        controller.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task Nested_emulation_is_rejected_before_lookup_or_user_creation()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);
        ((ClaimsIdentity)controller.User.Identity!).AddClaim(new Claim(EmulationService.EmulatingClaimType, "true"));

        (await controller.Candidates("target")).Result.Should().BeOfType<ConflictObjectResult>();
        (await controller.Start(new StartEmulationRequest { IamId = "target" })).Should().BeOfType<ConflictObjectResult>();
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Start_requires_a_bound_actor_session_before_creating_a_person_account()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);
        controller.HttpContext.Items.Remove(EmulationService.SessionItemKey);
        db.People.Add(new Person { IamId = "1000000001", FullName = "Target", IsActiveInIam = true });
        await db.SaveChangesAsync();

        var result = await controller.Start(new StartEmulationRequest { IamId = "1000000001" });

        result.Should().BeOfType<ForbidResult>();
        (await db.Users.CountAsync()).Should().Be(1);
        controller.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_accepts_existing_accounts_with_iam_ids_longer_than_directory_ids()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);
        db.Users.Add(new User { IamId = "sandbox-10001", Name = "Sample User" });
        await db.SaveChangesAsync();
        var actor = controller.User;

        var result = await controller.Start(new StartEmulationRequest { IamId = " sandbox-10001 " });

        result.Should().BeOfType<NoContentResult>();
        controller.Response.Headers.SetCookie.Should().ContainSingle();
        controller.User.Should().BeSameAs(actor);
        (await db.Users.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Start_rejects_invalid_iam_ids(string iamId)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);

        (await controller.Start(new StartEmulationRequest { IamId = iamId })).Should().BeOfType<BadRequestObjectResult>();
        (await controller.Start(new StartEmulationRequest { IamId = new string('x', 51) }))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Missing_or_inactive_targets_do_not_issue_selection_cookies()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);
        db.Users.Add(new User { IamId = "inactive", Name = "Inactive", IsActive = false });
        await db.SaveChangesAsync();

        (await controller.Start(new StartEmulationRequest { IamId = "missing" })).Should().BeOfType<NotFoundObjectResult>();
        (await controller.Start(new StartEmulationRequest { IamId = "inactive" })).Should().BeOfType<ConflictObjectResult>();
        controller.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task Stop_clears_invalid_selection_even_after_actor_admin_is_revoked()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = await Controller(db);
        (await db.Users.SingleAsync()).IsAdmin = false;
        await db.SaveChangesAsync();
        controller.HttpContext.User = Principal("ordinary-target");
        controller.Request.Headers.Cookie = ".Booking.Emulation=invalid";

        var result = controller.Stop();

        result.Should().BeOfType<NoContentResult>();
        controller.Response.Headers.SetCookie.Should().ContainSingle();
    }

    private static async Task<EmulationController> Controller(AppDbContext db)
    {
        db.Users.Add(new User { IamId = "actor", Name = "Administrator", IsAdmin = true });
        await db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:UseLocal"] = "true",
        }).Build();
        var environment = new TestEnvironment();
        var users = new UserService(NullLogger<UserService>.Instance, db, configuration, environment);
        var service = new EmulationService(db, users, configuration, environment,
            new EphemeralDataProtectionProvider(), NullLogger<EmulationService>.Instance);
        var actor = Principal("actor");
        var context = new DefaultHttpContext { User = actor };
        context.Items[EmulationService.ActorItemKey] = actor;
        context.Items[EmulationService.SessionItemKey] = "login-session";
        return new EmulationController(service, users)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private static ClaimsPrincipal Principal(string iamId) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, $"identity:{iamId}"), new Claim("ucdPersonIAMID", iamId)], "Test"));

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
