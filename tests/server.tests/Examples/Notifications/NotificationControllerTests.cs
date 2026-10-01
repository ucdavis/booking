using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Notification;
using Server.Examples.Notifications;

namespace Server.Tests.Examples.Notifications;

public class NotificationControllerTests
{
    [Theory]
    [InlineData(nameof(NotificationController.SendSample))]
    [InlineData(nameof(NotificationController.SendTableSample))]
    public async Task Inherited_antiforgery_filter_does_not_require_a_token_for_get(string actionName)
    {
        using var provider = CreateAntiforgeryServices();
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Method = HttpMethods.Get;

        var context = await ApplyInheritedAntiforgeryFilter(provider, actionName, httpContext);

        context.Result.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(NotificationController.SendSample), "missing")]
    [InlineData(nameof(NotificationController.SendSample), "invalid")]
    [InlineData(nameof(NotificationController.SendSample), "valid")]
    [InlineData(nameof(NotificationController.SendTableSample), "missing")]
    [InlineData(nameof(NotificationController.SendTableSample), "invalid")]
    [InlineData(nameof(NotificationController.SendTableSample), "valid")]
    public async Task Notification_posts_require_a_valid_antiforgery_token(string actionName, string tokenState)
    {
        using var provider = CreateAntiforgeryServices();
        var notifications = new FakeNotificationService();
        var controller = CreateController(Environments.Development, notifications,
        [
            new Claim(ClaimTypes.NameIdentifier, "notification-test-user"),
            new Claim(ClaimTypes.Email, "person@example.com"),
        ]);
        var httpContext = controller.HttpContext;
        httpContext.RequestServices = provider;
        httpContext.Request.Method = HttpMethods.Post;
        var tokenContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = httpContext.User,
        };
        var tokens = provider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(tokenContext);
        httpContext.Request.Headers.Cookie = tokenContext.Response.Headers.SetCookie.Single()!.Split(';')[0];
        if (tokenState != "missing")
        {
            httpContext.Request.Headers[tokens.HeaderName!] = tokenState == "valid"
                ? tokens.RequestToken
                : "invalid-antiforgery-token";
        }

        var context = await ApplyInheritedAntiforgeryFilter(provider, actionName, httpContext);

        if (tokenState != "valid")
        {
            context.Result.Should().BeOfType<AntiforgeryValidationFailedResult>();
            notifications.Invocations.Should().BeEmpty();
            notifications.TableInvocations.Should().BeEmpty();
            return;
        }

        context.Result.Should().BeNull();
        IActionResult result;
        if (actionName == nameof(NotificationController.SendSample))
        {
            result = await controller.SendSample(new NotificationRequest
            {
                Subject = "Subject",
                Header = "Header",
                Message = "Message",
            }, CancellationToken.None);
            notifications.Invocations.Should().ContainSingle();
        }
        else
        {
            result = await controller.SendTableSample(new TableNotificationRequest
            {
                Subject = "Subject",
                Header = "Header",
                Message = "Message",
                Rows =
                [
                    new TableNotificationRowRequest
                    {
                        Title = "Item",
                        Details = "Details",
                        Amount = 1m,
                    },
                ],
                TotalAmount = 1m,
            }, CancellationToken.None);
            notifications.TableInvocations.Should().ContainSingle();
        }
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Default_endpoint_returns_not_found_outside_development()
    {
        var notificationService = new FakeNotificationService();
        var controller = CreateController(
            environmentName: "Production",
            notificationService: notificationService);

        var result = await controller.SendSample(new NotificationRequest
        {
            Subject = "Subject",
            Header = "Header",
            Message = "Message",
            To = "person@example.com",
        }, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        notificationService.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Default_endpoint_returns_bad_request_when_no_recipient_can_be_resolved()
    {
        var controller = CreateController(
            environmentName: Environments.Development,
            notificationService: new FakeNotificationService());

        var result = await controller.SendSample(new NotificationRequest
        {
            Subject = "Subject",
            Header = "Header",
            Message = "Message",
        }, CancellationToken.None);

        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().Be("No email recipient was provided and the current user does not have an email claim.");
    }

    [Fact]
    public async Task Default_endpoint_uses_current_user_email_when_override_is_blank()
    {
        var notificationService = new FakeNotificationService();
        var controller = CreateController(
            environmentName: Environments.Development,
            notificationService: notificationService,
            claims:
            [
                new Claim("preferred_username", "person@example.com"),
            ]);

        var result = await controller.SendSample(new NotificationRequest
        {
            Subject = "Subject",
            Header = "Header",
            Message = "Message",
            To = "",
        }, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeOfType<SendSampleNotificationResponse>()
            .Which.To.Should().Be("person@example.com");

        notificationService.Invocations.Should().ContainSingle();
        notificationService.Invocations[0].Recipients.To.Should().Equal("person@example.com");
        notificationService.Invocations[0].Subject.Should().Be("Subject");
        notificationService.Invocations[0].Header.Should().Be("Header");
        notificationService.Invocations[0].Message.Should().Be("Message");
    }

    [Fact]
    public async Task Default_endpoint_is_available_in_test_environment()
    {
        var notificationService = new FakeNotificationService();
        var controller = CreateController(
            environmentName: "test",
            notificationService: notificationService,
            claims:
            [
                new Claim("preferred_username", "person@example.com"),
            ]);

        var result = await controller.SendSample(new NotificationRequest
        {
            Subject = "Subject",
            Header = "Header",
            Message = "Message",
            To = "",
        }, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeOfType<SendSampleNotificationResponse>()
            .Which.To.Should().Be("person@example.com");

        notificationService.Invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task Default_endpoint_uses_email_claim_when_preferred_username_is_absent()
    {
        var notificationService = new FakeNotificationService();
        var controller = CreateController(
            environmentName: Environments.Development,
            notificationService: notificationService,
            claims:
            [
                new Claim(ClaimTypes.Email, "email-claim@example.com"),
            ]);

        var result = await controller.SendSample(new NotificationRequest
        {
            Subject = "Subject",
            Header = "Header",
            Message = "Message",
            To = "",
        }, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeOfType<SendSampleNotificationResponse>()
            .Which.To.Should().Be("email-claim@example.com");

        notificationService.Invocations.Should().ContainSingle();
        notificationService.Invocations[0].Recipients.To.Should().Equal("email-claim@example.com");
    }

    [Fact]
    public async Task Default_endpoint_uses_explicit_to_when_provided()
    {
        var notificationService = new FakeNotificationService();
        var controller = CreateController(
            environmentName: Environments.Development,
            notificationService: notificationService,
            claims:
            [
                new Claim("preferred_username", "signed-in@example.com"),
            ]);

        var result = await controller.SendSample(new NotificationRequest
        {
            Subject = "Subject",
            Header = "Header",
            Message = "Message",
            To = "explicit@example.com",
        }, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeOfType<SendSampleNotificationResponse>()
            .Which.To.Should().Be("explicit@example.com");

        notificationService.Invocations.Should().ContainSingle();
        notificationService.Invocations[0].Recipients.To.Should().Equal("explicit@example.com");
        notificationService.Invocations[0].Subject.Should().Be("Subject");
        notificationService.Invocations[0].Header.Should().Be("Header");
        notificationService.Invocations[0].Message.Should().Be("Message");
    }

    [Fact]
    public async Task Default_endpoint_returns_bad_request_when_service_throws_validation_exception()
    {
        var notificationService = new ThrowingNotificationService(
            new System.ComponentModel.DataAnnotations.ValidationException("Notification subject is required."));
        var controller = CreateController(
            environmentName: Environments.Development,
            notificationService: notificationService,
            claims:
            [
                new Claim("preferred_username", "person@example.com"),
            ]);

        var result = await controller.SendSample(new NotificationRequest
        {
            Subject = "",
            Header = "Header",
            Message = "Message",
        }, CancellationToken.None);

        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().Be("Notification subject is required.");
    }

    [Fact]
    public async Task Table_endpoint_uses_current_user_email_and_passes_rows_and_total()
    {
        var notificationService = new FakeNotificationService();
        var controller = CreateController(
            environmentName: Environments.Development,
            notificationService: notificationService,
            claims:
            [
                new Claim("preferred_username", "person@example.com"),
            ]);

        var result = await controller.SendTableSample(new TableNotificationRequest
        {
            Subject = "Subject",
            Header = "Header",
            Message = "Summary message",
            Rows =
            [
                new TableNotificationRowRequest
                {
                    Title = "Design",
                    Details = "Initial exploration",
                    Amount = 75m,
                },
                new TableNotificationRowRequest
                {
                    Title = "Build",
                    Details = "Implementation",
                    Amount = 125m,
                },
            ],
            TotalAmount = 200m,
        }, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeOfType<SendSampleNotificationResponse>()
            .Which.To.Should().Be("person@example.com");

        notificationService.TableInvocations.Should().ContainSingle();
        notificationService.TableInvocations[0].Recipients.To.Should().Equal("person@example.com");
        notificationService.TableInvocations[0].Subject.Should().Be("Subject");
        notificationService.TableInvocations[0].Header.Should().Be("Header");
        notificationService.TableInvocations[0].Message.Should().Be("Summary message");
        notificationService.TableInvocations[0].Rows.Should().HaveCount(2);
        notificationService.TableInvocations[0].Rows[0].Title.Should().Be("Design");
        notificationService.TableInvocations[0].Rows[0].Details.Should().Be("Initial exploration");
        notificationService.TableInvocations[0].Rows[0].Amount.Should().Be(75m);
        notificationService.TableInvocations[0].Rows[1].Title.Should().Be("Build");
        notificationService.TableInvocations[0].Rows[1].Details.Should().Be("Implementation");
        notificationService.TableInvocations[0].Rows[1].Amount.Should().Be(125m);
        notificationService.TableInvocations[0].TotalAmount.Should().Be(200m);
    }

    [Fact]
    public async Task Table_endpoint_is_available_in_test_environment()
    {
        var notificationService = new FakeNotificationService();
        var controller = CreateController(
            environmentName: "test",
            notificationService: notificationService,
            claims:
            [
                new Claim("preferred_username", "person@example.com"),
            ]);

        var result = await controller.SendTableSample(new TableNotificationRequest
        {
            Subject = "Subject",
            Header = "Header",
            Message = "Summary message",
            Rows =
            [
                new TableNotificationRowRequest
                {
                    Title = "Design",
                    Details = "Initial exploration",
                    Amount = 75m,
                },
            ],
            TotalAmount = 75m,
        }, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeOfType<SendSampleNotificationResponse>()
            .Which.To.Should().Be("person@example.com");

        notificationService.TableInvocations.Should().ContainSingle();
    }

    private static ServiceProvider CreateAntiforgeryServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews().AddApplicationPart(typeof(NotificationController).Assembly);
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        return services.BuildServiceProvider();
    }

    private static async Task<AuthorizationFilterContext> ApplyInheritedAntiforgeryFilter(
        IServiceProvider provider, string actionName, HttpContext httpContext)
    {
        var action = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Single(descriptor => descriptor.ControllerTypeInfo.AsType() == typeof(NotificationController)
                && descriptor.MethodInfo.Name == actionName);
        var attribute = action.FilterDescriptors.Select(descriptor => descriptor.Filter)
            .OfType<AutoValidateAntiforgeryTokenAttribute>().Should().ContainSingle().Subject;
        var filter = attribute.CreateInstance(provider);
        var filters = action.FilterDescriptors.OrderBy(descriptor => descriptor.Order)
            .ThenBy(descriptor => descriptor.Scope)
            .Select(descriptor => ReferenceEquals(descriptor.Filter, attribute) ? filter : descriptor.Filter)
            .ToList();
        var context = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), action), filters);

        await ((IAsyncAuthorizationFilter)filter).OnAuthorizationAsync(context);

        return context;
    }

    private static NotificationController CreateController(
        string environmentName,
        ISampleNotificationService notificationService,
        Claim[]? claims = null)
    {
        var controller = new NotificationController(
            new FakeHostEnvironment(environmentName),
            NullLogger<NotificationController>.Instance,
            notificationService);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims ?? [], "TestAuth")),
            },
        };

        return controller;
    }

    private sealed class FakeNotificationService : ISampleNotificationService
    {
        public List<Invocation> Invocations { get; } = [];
        public List<TableInvocation> TableInvocations { get; } = [];

        public Task SendAsync(
            EmailRecipients recipients,
            string subject,
            string header,
            string message,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(new Invocation(recipients, subject, header, message));
            return Task.CompletedTask;
        }

        public Task SendTableAsync(
            EmailRecipients recipients,
            string subject,
            string header,
            string message,
            IReadOnlyList<NotificationTableRow> rows,
            decimal totalAmount,
            CancellationToken cancellationToken = default)
        {
            TableInvocations.Add(new TableInvocation(recipients, subject, header, message, rows, totalAmount));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingNotificationService : ISampleNotificationService
    {
        private readonly Exception _exception;

        public ThrowingNotificationService(Exception exception)
        {
            _exception = exception;
        }

        public Task SendAsync(
            EmailRecipients recipients,
            string subject,
            string header,
            string message,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }

        public Task SendTableAsync(
            EmailRecipients recipients,
            string subject,
            string header,
            string message,
            IReadOnlyList<NotificationTableRow> rows,
            decimal totalAmount,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "server.tests";
        public string ContentRootPath { get; set; } = "/workspace";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record Invocation(
        EmailRecipients Recipients,
        string Subject,
        string Header,
        string Message);

    private sealed record TableInvocation(
        EmailRecipients Recipients,
        string Subject,
        string Header,
        string Message,
        IReadOnlyList<NotificationTableRow> Rows,
        decimal TotalAmount);
}
