using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

// Phase 0 smoke-test controller — versioning + swagger + routing কাজ করছে প্রমাণ করে।
// Phase 1 এ এটা মুছে ফেলা যাবে (demo/removable)।
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { message = "pong", version = "v1", utc = DateTime.UtcNow });
}
