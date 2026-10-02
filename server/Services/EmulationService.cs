using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Emulation;

namespace Server.Services;

public sealed class EmulationService
{
    public const string ActorItemKey = "Booking.Emulation.Actor";
    public const string SessionItemKey = "Booking.Emulation.Session";
    public const string SessionPropertyKey = "Booking.SessionId";
    public const string EmulatingClaimType = "booking:emulating";

    private const string Scheme = "Booking.Emulation";
    private const string ActorIdClaimType = "booking:actor-id";
    private const string ActorIamClaimType = "booking:actor-iam";
    private const string SessionClaimType = "booking:session";
    private const string TargetClaimType = "booking:target-iam";
    private readonly AppDbContext _dbContext;
    private readonly IUserService _userService;
    private readonly ILogger<EmulationService> _logger;
    private readonly TicketDataFormat _ticketFormat;
    private readonly bool _useLocal;
    private readonly string _cookieName;

    public EmulationService(AppDbContext dbContext, IUserService userService, IConfiguration configuration,
        IHostEnvironment environment, IDataProtectionProvider dataProtectionProvider, ILogger<EmulationService> logger)
    {
        _dbContext = dbContext;
        _userService = userService;
        _logger = logger;
        _useLocal = LocalAuthentication.IsEnabled(configuration, environment);
        _cookieName = ".Booking.Emulation";
        var suffix = configuration["Auth:LocalCookieSuffix"];
        if (_useLocal && !string.IsNullOrEmpty(suffix))
        {
            _cookieName += $".{suffix}";
        }

        // TicketDataFormat serializes the ticket with TicketSerializer before protecting it.
        _ticketFormat = new TicketDataFormat(dataProtectionProvider.CreateProtector("Booking.Emulation.v1", _cookieName));
    }

    public static ClaimsPrincipal GetActor(HttpContext context)
        => context.Items[ActorItemKey] as ClaimsPrincipal ?? context.User;

    public static bool IsEmulating(HttpContext context)
        => context.User.HasClaim(EmulatingClaimType, "true");

    public async Task<List<EmulationCandidateResponse>> SearchAsync(string search, CancellationToken cancellationToken)
    {
        var matches = await (
            from user in _dbContext.Users.AsNoTracking()
            join person in _dbContext.People.AsNoTracking() on user.IamId equals person.IamId into people
            from person in people.DefaultIfEmpty()
            where user.IamId == search || user.Email == search ||
                (person != null && (person.UserId == search || person.Email == search))
            orderby user.Name, user.IamId
            select new EmulationCandidateResponse
            {
                IamId = user.IamId.Trim(),
                Name = user.Name,
                Email = user.Email,
                Kerberos = person == null || person.UserId == null ? null : person.UserId.Trim(),
                HasUserAccount = true,
                IsActive = user.IsActive,
                IsActiveInIam = person == null ? null : (bool?)person.IsActiveInIam,
            })
            .Take(10)
            .ToListAsync(cancellationToken);

        if (matches.Count < 10)
        {
            var people = await _dbContext.People.AsNoTracking()
                .Where(person => (person.IamId == search || person.Email == search || person.UserId == search) &&
                    !_dbContext.Users.Any(user => user.IamId == person.IamId))
                .OrderBy(person => person.FullName).ThenBy(person => person.IamId)
                .Select(person => new EmulationCandidateResponse
                {
                    IamId = person.IamId.Trim(),
                    Name = (person.FullName ?? "").Trim() != ""
                        ? person.FullName!.Trim()
                        : ((person.FirstName ?? "") + " " + (person.LastName ?? "")).Trim(),
                    Email = person.Email == null ? null : person.Email.Trim(),
                    Kerberos = person.UserId == null ? null : person.UserId.Trim(),
                    HasUserAccount = false,
                    IsActive = true,
                    IsActiveInIam = person.IsActiveInIam,
                })
                .Take(10 - matches.Count)
                .ToListAsync(cancellationToken);
            matches.AddRange(people);
        }

        return matches;
    }

    public async Task<(User? User, string? Error)> FindOrCreateTargetAsync(
        string iamId, CancellationToken cancellationToken)
    {
        iamId = iamId.Trim();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
        var person = await _dbContext.People.AsNoTracking()
            .SingleOrDefaultAsync(person => person.IamId == iamId, cancellationToken);
        if (user != null && !user.IsActive)
        {
            return (null, "This user is inactive and cannot be emulated.");
        }
        if (person != null && !person.IsActiveInIam)
        {
            return (null, "This person is inactive in IAM and cannot be emulated.");
        }
        if (user != null)
        {
            return (user, null);
        }
        if (person == null)
        {
            return (null, null);
        }

        var name = string.IsNullOrWhiteSpace(person.FullName)
            ? $"{person.FirstName} {person.LastName}".Trim()
            : person.FullName.Trim();
        var now = DateTimeOffset.UtcNow;
        user = new User
        {
            IamId = person.IamId.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? person.IamId.Trim() : name,
            Email = person.Email?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _dbContext.Users.Add(user);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent sign-in or administrator action can win the unique IAM ID insert.
            _dbContext.Entry(user).State = EntityState.Detached;
            user = await _dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
            if (user == null)
            {
                throw;
            }
            if (!user.IsActive)
            {
                return (null, "This user is inactive and cannot be emulated.");
            }
        }

        return (user, null);
    }

    public async Task<User?> GetActiveTargetAsync(string iamId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.IamId == iamId && user.IsActive, cancellationToken);
        if (user == null || await _dbContext.People.AnyAsync(
                person => person.IamId == iamId && !person.IsActiveInIam, cancellationToken))
        {
            return null;
        }

        return user;
    }

    public async Task<ClaimsPrincipal> CreatePrincipalAsync(User target)
    {
        var iamId = target.IamId.Trim();
        var identifier = _useLocal ? $"local-person:{iamId}" : $"emulated-user:{target.Id}";
        if (_useLocal && iamId == "sandbox-10001")
        {
            identifier = "sandbox-sample";
        }
        else if (_useLocal && iamId == "sandbox-10002")
        {
            identifier = "sandbox-basic";
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identifier),
            new(ClaimTypes.Name, target.Name),
            new("name", target.Name),
            new("ucdPersonIAMID", iamId),
            new(EmulatingClaimType, "true"),
        };
        if (!string.IsNullOrWhiteSpace(target.Email))
        {
            claims.Add(new Claim("preferred_username", target.Email));
        }
        if (_useLocal)
        {
            claims.Add(new Claim(ClaimTypes.Role, "User"));
            if (iamId == "sandbox-10001")
            {
                claims.Add(new Claim(ClaimTypes.Role, "SampleRole"));
            }
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return _useLocal ? principal : await _userService.UpdateUserPrincipalIfNeeded(principal) ?? principal;
    }

    public bool HasSelection(HttpContext context) => context.Request.Cookies.ContainsKey(_cookieName);

    public void Start(HttpContext context, ClaimsPrincipal actor, string sessionId, User target)
    {
        var actorId = actor.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var actorIamId = actor.FindFirst("ucdPersonIAMID")?.Value;
        if (actor.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(actorId) ||
            string.IsNullOrWhiteSpace(actorIamId) || string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("An authenticated administrator session is required for emulation.");
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ActorIdClaimType, actorId),
            new Claim(ActorIamClaimType, actorIamId),
            new Claim(SessionClaimType, sessionId),
            new Claim(TargetClaimType, target.IamId.Trim()),
        ], Scheme));
        var now = DateTimeOffset.UtcNow;
        var ticket = new AuthenticationTicket(principal,
            new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddHours(8) }, Scheme);
        context.Response.Cookies.Append(_cookieName, _ticketFormat.Protect(ticket), CookieOptions(context));
        _logger.LogInformation("Emulation started by IAM {ActorIamId} for user {TargetUserId} with IAM {TargetIamId}",
            actorIamId, target.Id, target.IamId);
    }

    public string? ReadTarget(HttpContext context, ClaimsPrincipal actor, string sessionId)
    {
        if (!context.Request.Cookies.TryGetValue(_cookieName, out var selection) || string.IsNullOrEmpty(selection))
        {
            return null;
        }

        var ticket = _ticketFormat.Unprotect(selection);
        var actorId = actor.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var actorIamId = actor.FindFirst("ucdPersonIAMID")?.Value;
        if (ticket == null || ticket.AuthenticationScheme != Scheme || actor.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(actorIamId) ||
            string.IsNullOrWhiteSpace(sessionId) || ticket.Properties.ExpiresUtc == null ||
            ticket.Properties.ExpiresUtc <= DateTimeOffset.UtcNow ||
            ticket.Principal.FindFirst(ActorIdClaimType)?.Value != actorId ||
            ticket.Principal.FindFirst(ActorIamClaimType)?.Value != actorIamId ||
            ticket.Principal.FindFirst(SessionClaimType)?.Value != sessionId)
        {
            // Preserve invalid selections so middleware can block the request until an explicit stop.
            return null;
        }

        var target = ticket.Principal.FindFirst(TargetClaimType)?.Value;
        return string.IsNullOrWhiteSpace(target) || target.Length > 50 ? null : target;
    }

    public void Clear(HttpContext context, bool force = false)
    {
        var hasSelection = HasSelection(context);
        if (!force && !hasSelection)
        {
            return;
        }

        context.Response.Cookies.Delete(_cookieName, CookieOptions(context));
        if (hasSelection)
        {
            _logger.LogInformation("Emulation cleared by IAM {ActorIamId}",
                GetActor(context).FindFirst("ucdPersonIAMID")?.Value);
        }
    }

    private static CookieOptions CookieOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = "/",
    };
}
