using System.Text;
using Microsoft.AspNetCore.Mvc;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Services;

namespace PerfPilot.Api.Controllers;

[ApiController]
[Route("api/specs")]
public sealed class SpecsController(SpecImportService specs) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public Task<SpecDetailDto> Get(Guid id, CancellationToken ct) => specs.GetDetailAsync(id, ct);

    [HttpGet("{id:guid}/raw")]
    public async Task<IActionResult> Raw(Guid id, CancellationToken ct)
    {
        var content = await specs.GetRawAsync(id, ct);
        var isJson = content.TrimStart().StartsWith('{');
        return File(Encoding.UTF8.GetBytes(content), isJson ? "application/json" : "application/yaml", isJson ? "openapi.json" : "openapi.yaml");
    }
}
