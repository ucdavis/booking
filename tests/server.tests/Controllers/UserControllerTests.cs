using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Controllers;
using Server.Core.Data;
using Server.Services;

namespace Server.Tests.Controllers;

public class UserControllerTests
{
    [Fact]
    public async Task Me_returns_iam_id_claim()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = CreateController(db, "123456789");

        var result = await controller.Me();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        json.RootElement.GetProperty("IamId").GetString().Should().Be("123456789");
    }

    [Fact]
    public async Task Me_returns_null_iam_id_when_claim_is_missing()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = CreateController(db);

        var result = await controller.Me();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        json.RootElement.GetProperty("IamId").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("IsSiteAdmin").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task Me_returns_site_admin_access_from_the_matching_database_user(
        bool isAdmin, bool isActive, bool expectedSiteAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new Server.Core.Domain.User
        {
            IamId = "123456789",
            Name = "Test User",
            IsAdmin = isAdmin,
            IsActive = isActive,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, "123456789");
        ((ClaimsIdentity)controller.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Admin"));

        var result = await controller.Me();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        json.RootElement.GetProperty("IsSiteAdmin").GetBoolean().Should().Be(expectedSiteAdmin);
    }

    [Fact]
    public async Task Me_does_not_grant_site_admin_for_a_different_database_user()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new Server.Core.Domain.User
        {
            IamId = "another-iam-id",
            Name = "Site Admin",
            IsAdmin = true,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, "123456789");

        var result = await controller.Me();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        json.RootElement.GetProperty("IsSiteAdmin").GetBoolean().Should().BeFalse();
    }

    private static UserController CreateController(AppDbContext db, string? iamId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-1"),
            new("name", "Test User"),
            new("preferred_username", "test@example.com"),
        };

        if (iamId != null)
        {
            claims.Add(new Claim("ucdPersonIAMID", iamId));
        }

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var userService = new UserService(NullLogger<UserService>.Instance, db,
            new ConfigurationBuilder().Build(), new TestEnvironment());
        return new UserController(userService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user,
                },
            },
        };
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
