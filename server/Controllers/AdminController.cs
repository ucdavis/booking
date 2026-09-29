using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Helpers;

namespace Server.Controllers;

[Authorize(Policy = AuthenticationHelper.SiteAdminPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminController : ApiControllerBase
{
    [HttpGet("access")]
    public IActionResult Access() => NoContent();
}
