using System.Text;
using Microsoft.AspNetCore.Mvc;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;
using PerfPilot.Core.Services;

namespace PerfPilot.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController(RunService runs) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<RunListItemDto>> List(
        [FromQuery] Guid? scenarioId,
        [FromQuery] Guid? environmentId,
        [FromQuery] RunStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        runs.ListAsync(new RunQuery(scenarioId, environmentId, status, page, pageSize), ct);

    [HttpGet("{id:guid}")]
    public Task<RunDetailDto> Get(Guid id, CancellationToken ct) => runs.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<RunDetailDto>> Start(StartRunRequest request, CancellationToken ct)
    {
        var run = await runs.StartAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = run.Id }, run);
    }

    [HttpPost("{id:guid}/cancel")]
    public Task<RunDetailDto> Cancel(Guid id, CancellationToken ct) => runs.CancelAsync(id, ct);

    [HttpGet("{id:guid}/report")]
    public async Task<IActionResult> Report(Guid id, CancellationToken ct)
    {
        var (html, fileName) = await runs.GetReportAsync(id, ct);
        return File(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", fileName);
    }

    [HttpGet("{id:guid}/script")]
    public async Task<IActionResult> Script(Guid id, CancellationToken ct)
    {
        var (script, fileName) = await runs.GetScriptAsync(id, ct);
        return File(Encoding.UTF8.GetBytes(script), "text/javascript", fileName);
    }

    [HttpGet("{id:guid}/summary")]
    public async Task<IActionResult> Summary(Guid id, CancellationToken ct)
    {
        var json = await runs.GetSummaryJsonAsync(id, ct);
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"k6-summary-{id:N}.json");
    }
}
