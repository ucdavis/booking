using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers;

// Shared authorization and antiforgery protection for API controllers.
[Authorize]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("api/[controller]")]
public class ApiControllerBase : ControllerBase
{

}
