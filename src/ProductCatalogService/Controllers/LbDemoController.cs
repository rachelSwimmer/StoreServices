using Microsoft.AspNetCore.Mvc;

namespace ProductCatalogService.Controllers;

/// <summary>
/// Endpoints used solely by the load-balancer example (scripts/lb-demo.sh).
/// Unauthenticated and not exposed through the gateway, matching the
/// convention of the other internal controllers, so the demo needs no JWT.
/// </summary>
[ApiController]
[Route("internal/lb")]
public class LbDemoController : ControllerBase
{
    // Process-local health flag. Toggling it lets the demo mark a still-running
    // instance "unhealthy" so Nginx evicts it via passive health checks.
    private static volatile bool _healthy = true;

    // Process-local artificial latency (ms) added to whoami. Lets the demo make
    // one replica slow so least_conn visibly diverges from round-robin.
    private static volatile int _delayMs;

    private static readonly string InstanceName = Environment.MachineName;

    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI()
    {
        if (_delayMs > 0) await Task.Delay(_delayMs);
        return _healthy
            ? Ok(new { instance = InstanceName })
            // 503 so Nginx's passive health check (max_fails) evicts this
            // instance even though the container is still running.
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { instance = InstanceName, status = "unhealthy" });
    }

    [HttpGet("health")]
    public IActionResult Health()
        => _healthy
            ? Ok(new { instance = InstanceName, status = "healthy" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { instance = InstanceName, status = "unhealthy" });

    [HttpPost("health/toggle")]
    public IActionResult ToggleHealth()
    {
        _healthy = !_healthy;
        return Ok(new { instance = InstanceName, healthy = _healthy });
    }

    // Set artificial latency on this instance (ms). Used by the algorithm
    // demo to make one replica slow so least_conn diverges from round-robin.
    [HttpPost("slow/{ms:int}")]
    public IActionResult SetDelay(int ms)
    {
        _delayMs = ms < 0 ? 0 : ms;
        return Ok(new { instance = InstanceName, delayMs = _delayMs });
    }
}
