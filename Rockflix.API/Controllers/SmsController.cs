using Microsoft.AspNetCore.Mvc;
using Rockflix.API.Services;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/sms")]
public class SmsController : ControllerBase
{
    private readonly MediaRequestService _mediaRequestService;
    private readonly ILogger<SmsController> _logger;

    public SmsController(MediaRequestService mediaRequestService, ILogger<SmsController> logger)
    {
        _mediaRequestService = mediaRequestService;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Receive([FromForm] string Body, [FromForm] string From)
    {
        _logger.LogInformation("SMS from {From}: {Body}", From, Body);

        var result = await _mediaRequestService.ProcessRequestAsync(Body);

        var twiml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response><Message>{System.Web.HttpUtility.HtmlEncode(result.Message)}</Message></Response>
            """;

        return Content(twiml, "application/xml");
    }
}
