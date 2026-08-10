using Asp.Versioning;
using CarDealer.Application.Abstractions;
using CarDealer.Infrastructure.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarDealer.Api.Controllers;

/// <summary>
/// Endpoints that exist to make Phase 0 infrastructure verifiable through Swagger.
/// </summary>
/// <remarks>
/// Under decision D10 there is no UI, so the cache and background-job abstractions would
/// otherwise be untestable by hand. These are development-only and are not registered in
/// Production.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/diagnostics")]
[Authorize]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IBackgroundJobScheduler _jobs;
    private readonly ICacheService _cache;
    private readonly IHostEnvironment _environment;

    public DiagnosticsController(
        IBackgroundJobScheduler jobs, ICacheService cache, IHostEnvironment environment)
    {
        _jobs = jobs;
        _cache = cache;
        _environment = environment;
    }

    /// <summary>Enqueues a durable background job (criteria H4, H5).</summary>
    /// <remarks>
    /// Restart the API after calling this and the job still runs - that is the property
    /// criterion H5 checks, and it is what distinguishes a durable job system from a
    /// fire-and-forget task.
    /// </remarks>
    [HttpPost("enqueue-echo")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult EnqueueEcho([FromQuery] string message = "hello", CancellationToken ct = default)
    {
        if (_environment.IsProduction())
        {
            return NotFound();
        }

        var jobId = _jobs.Enqueue<EchoJob>(message);

        return Accepted(new { jobId, message });
    }

    /// <summary>Round-trips a value through the cache abstraction (criteria H1, H2).</summary>
    [HttpPost("cache-roundtrip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CacheRoundTrip(
        [FromQuery] string key = "diagnostic", [FromQuery] string value = "ok", CancellationToken ct = default)
    {
        if (_environment.IsProduction())
        {
            return NotFound();
        }

        await _cache.SetAsync(key, value, TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
        var read = await _cache.GetAsync<string>(key, ct).ConfigureAwait(false);

        return Ok(new
        {
            key,
            written = value,
            read,
            matched = string.Equals(value, read, StringComparison.Ordinal),
            implementation = _cache.GetType().Name,
        });
    }
}
