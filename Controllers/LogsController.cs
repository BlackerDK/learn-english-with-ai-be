using Microsoft.AspNetCore.Mvc;
using backend.Services;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogsController : ControllerBase
    {
        [HttpPost("frontend")]
        public IActionResult LogFrontendError([FromBody] FrontendErrorLogRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest("Invalid error log data.");
            }

            var details = $"URL: {request.Url}\nMessage: {request.Message}";
            FileLogger.LogError(details, "Frontend React", request.Stack);

            return Ok(new { message = "Frontend error logged successfully." });
        }

        [HttpGet("test-error")]
        public IActionResult TestError()
        {
            throw new System.Exception("Test unhandled exception from backend controller.");
        }
    }

    public class FrontendErrorLogRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? Stack { get; set; }
        public string? Url { get; set; }
    }
}
