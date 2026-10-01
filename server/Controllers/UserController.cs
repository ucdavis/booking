using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Services;

namespace Server.Controllers;

public class UserController(IUserService userService, AppDbContext dbContext) : ApiControllerBase
{
    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = User.FindFirst("name")?.Value;
        var userEmail = User.FindFirst("preferred_username")?.Value;
        var iamId = User.FindFirst("ucdPersonIAMID")?.Value;

        var userRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (userId == null)
        {
            return Unauthorized();
        }

        var kerberos = string.IsNullOrWhiteSpace(iamId)
            ? null
            : await dbContext.People.AsNoTracking()
                .Where(person => person.IamId == iamId)
                .Select(person => person.UserId)
                .SingleOrDefaultAsync(cancellationToken);

        var userInfo = new
        {
            Id = userId,
            Name = userName,
            Email = userEmail,
            IamId = iamId,
            Kerberos = string.IsNullOrWhiteSpace(kerberos) ? null : kerberos.Trim(),
            IsSiteAdmin = await userService.IsSiteAdmin(User, cancellationToken),
            IsEmulating = EmulationService.IsEmulating(HttpContext),
            Roles = userRoles,
        };

        return Ok(userInfo);
    }
}
