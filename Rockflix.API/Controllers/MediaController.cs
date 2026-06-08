using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rockflix.API.Services;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MediaController(MediaScannerService scanner) : ControllerBase
{
    [HttpPost("scan")]
    public async Task<IActionResult> ScanMedia()
    {
        await scanner.ScanAsync();
        return Ok(new { message = "Media scan complete." });
    }
}
