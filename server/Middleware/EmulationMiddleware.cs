using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Server.Services;

namespace Server.Middleware;

public sealed class EmulationMiddleware(RequestDelegate next, ILogger<EmulationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, EmulationService emulation, IUserService users)
    {
        var actor = context.User;
        context.Items[EmulationService.ActorItemKey] = actor;

        if (actor.Identity?.IsAuthenticated != true)
        {
            if (emulation.HasSelection(context))
            {
                emulation.Clear(context);
            }
            await next(context);
            return;
        }

        var authentication = await context.AuthenticateAsync();
        var properties = authentication.Properties;
        string? sessionId = null;
        properties?.Items.TryGetValue(EmulationService.SessionPropertyKey, out sessionId);
        sessionId ??= properties?.IssuedUtc?.ToString("O", CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(sessionId))
        {
            context.Items[EmulationService.SessionItemKey] = sessionId;
        }

        // The local sign-out form uses the real identity's antiforgery token from the login page.
        // Successful sign-in or sign-out clears emulation.
        if (!emulation.HasSelection(context) || context.Request.Path.StartsWithSegments("/login")
            || context.Request.Path.Equals("/logout/local", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var targetIamId = string.IsNullOrEmpty(sessionId)
            ? null
            : emulation.ReadTarget(context, actor, sessionId);
        var target = targetIamId != null && await users.IsSiteAdmin(actor, context.RequestAborted)
            ? await emulation.GetActiveTargetAsync(targetIamId, context.RequestAborted)
            : null;

        if (target == null)
        {
            // Never let a stale emulated request fall back to the administrator's permissions.
            // Keep the account menu available so the administrator can explicitly stop emulating.
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "emulation-unavailable"),
                new Claim("name", "Emulation unavailable"),
                new Claim(EmulationService.EmulatingClaimType, "true"),
            ], "Emulation"));
            var isPublicPage = HttpMethods.IsGet(context.Request.Method)
                && !context.Request.Path.StartsWithSegments("/api")
                && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null;
            if (isPublicPage || IsRecoveryRequest(context.Request.Path))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(
                "Emulation is no longer available. Stop emulating to return to your account.",
                context.RequestAborted);
            return;
        }

        context.User = await emulation.CreatePrincipalAsync(target);
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["EmulationActorIamId"] = actor.FindFirst("ucdPersonIAMID")?.Value,
            ["EmulationTargetUserId"] = target.Id,
        }))
        {
            await next(context);
        }
    }

    private static bool IsRecoveryRequest(PathString path)
        => path.Equals("/api/user/me", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/antiforgery", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/emulation/stop", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/logout/antiforgery", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/logout", StringComparison.OrdinalIgnoreCase);
}
