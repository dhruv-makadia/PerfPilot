using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PerfPilot.Api.Identity;
using PerfPilot.Core.Abstractions;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Options;

namespace PerfPilot.Api.Controllers;

[ApiController]
[Route("api/config")]
public sealed class ConfigController(
    IOptions<SafetyOptions> safety,
    IOptions<IdentityOptions> identity,
    ICurrentUser currentUser,
    IK6Info k6) : ControllerBase
{
    /// <summary>Settings the UI needs: safety caps, identity mode, current user and k6 availability.</summary>
    [HttpGet]
    public async Task<AppConfigDto> Get(CancellationToken ct) => new(
        safety.Value.MaxVus,
        safety.Value.MaxDurationMinutes,
        identity.Value.IsWindows ? IdentityModes.Windows : IdentityModes.Header,
        currentUser.Name,
        await k6.GetStatusAsync(ct));
}
