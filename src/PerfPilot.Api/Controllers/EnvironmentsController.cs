using Microsoft.AspNetCore.Mvc;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Services;

namespace PerfPilot.Api.Controllers;

[ApiController]
[Route("api/environments")]
public sealed class EnvironmentsController(EnvironmentService environments, SpecImportService specs) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<EnvironmentDto>> List(CancellationToken ct) => environments.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<EnvironmentDto> Get(Guid id, CancellationToken ct) => environments.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<EnvironmentDto>> Create(SaveEnvironmentRequest request, CancellationToken ct)
    {
        var created = await environments.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<EnvironmentDto> Update(Guid id, SaveEnvironmentRequest request, CancellationToken ct) => environments.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await environments.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Tests the posted settings from the server (never the browser). Pass environmentId to reuse stored secrets.</summary>
    [HttpPost("test-connection")]
    public Task<ConnectionTestResult> TestConnection(TestConnectionRequest request, CancellationToken ct) =>
        environments.TestConnectionAsync(request, ct);

    [HttpGet("{id:guid}/specs")]
    public Task<IReadOnlyList<SpecSummaryDto>> ListSpecs(Guid id, CancellationToken ct) => specs.ListVersionsAsync(id, ct);

    [HttpGet("{id:guid}/specs/latest")]
    public async Task<ActionResult<SpecDetailDto>> LatestSpec(Guid id, CancellationToken ct) =>
        await specs.GetLatestDetailAsync(id, ct) is { } detail ? detail : NoContent();

    [HttpPost("{id:guid}/specs/import-url")]
    public Task<SpecDetailDto> ImportFromUrl(Guid id, ImportSpecFromUrlRequest request, CancellationToken ct) =>
        specs.ImportFromUrlAsync(id, request, ct);

    [HttpPost("{id:guid}/specs/upload")]
    [RequestSizeLimit(100L * 1024 * 1024)]
    public async Task<ActionResult<SpecDetailDto>> Upload(Guid id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new Core.Common.ValidationException(new Dictionary<string, string[]> { ["file"] = ["Choose a .json or .yaml file to upload."] });
        }
        await using var stream = file.OpenReadStream();
        return await specs.ImportFromFileAsync(id, file.FileName, stream, file.Length, ct);
    }
}
