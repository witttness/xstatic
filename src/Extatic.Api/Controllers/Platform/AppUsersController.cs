using Extatic.Api.Auth;
using Extatic.Api.Domain.Entities;
using Extatic.Api.Dtos;
using Extatic.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Extatic.Api.Controllers.Platform;

[ApiController]
[Route("api/apps/{app_slug}/appusers")]
[Authorize(Policy = PolicyNames.AppAnyAccess)]
public class AppUsersController(AppUserService appUserService) : ControllerBase
{
    private App CurrentApp => (App)HttpContext.Items["CurrentApp"]!;

    [HttpGet]
    public async Task<ActionResult<List<AppUserDto>>> List()
    {
        var users = await appUserService.GetForAppAsync(CurrentApp.Id);
        return Ok(users.Select(u => u.ToDto()).ToList());
    }
}
