using System.Text;
using Microsoft.AspNetCore.Mvc;
using PerfPilot.Core.Contracts;
using PerfPilot.Core.Services;

namespace PerfPilot.Api.Controllers;

[ApiController]
[Route("api/scenarios")]
public sealed class ScenariosController(ScenarioService scenarios) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ScenarioListItemDto>> List([FromQuery] Guid? environmentId, CancellationToken ct) => scenarios.ListAsync(environmentId, ct);

    [HttpGet("{id:guid}")]
    public Task<ScenarioDto> Get(Guid id, CancellationToken ct) => scenarios.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ScenarioDto>> Create(SaveScenarioRequest request, CancellationToken ct)
    {
        var created = await scenarios.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<ScenarioDto> Update(Guid id, SaveScenarioRequest request, CancellationToken ct) => scenarios.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await scenarios.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/clone")]
    public async Task<ActionResult<ScenarioDto>> Clone(Guid id, CancellationToken ct)
    {
        var clone = await scenarios.CloneAsync(id, ct);
        return CreatedAtAction(nameof(Get), new { id = clone.Id }, clone);
    }

    [HttpGet("{id:guid}/script")]
    public Task<ScriptPreviewDto> Script(Guid id, CancellationToken ct) => scenarios.PreviewScriptAsync(id, ct);

    [HttpGet("{id:guid}/script/download")]
    public async Task<IActionResult> DownloadScript(Guid id, CancellationToken ct)
    {
        var preview = await scenarios.PreviewScriptAsync(id, ct);
        return File(Encoding.UTF8.GetBytes(preview.Script), "text/javascript", preview.FileName);
    }

    /// <summary>Generates a script for unsaved builder state.</summary>
    [HttpPost("preview-script")]
    public Task<ScriptPreviewDto> PreviewScript(SaveScenarioRequest request, CancellationToken ct) => scenarios.PreviewScriptAsync(request, ct);

    [HttpGet("/api/load-presets")]
    public IReadOnlyList<LoadPresetDto> Presets() => LoadProfilePresets.All;
}
