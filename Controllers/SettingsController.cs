using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Database;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SettingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SettingsController(AppDbContext context)
        {
            _context = context;
        }

        private Guid GetUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdStr, out var userId))
                return userId;
            throw new UnauthorizedAccessException("Invalid user token.");
        }

        // GET: api/settings/gemini-key
        [HttpGet("gemini-key")]
        public async Task<IActionResult> GetGeminiKeyStatus()
        {
            var userId = GetUserId();
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GeminiApiKey" && s.UserId == userId);

            if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            {
                return Ok(new { exists = false, maskedValue = "" });
            }

            var val = setting.Value;
            var masked = val.Length > 8 
                ? new string('•', val.Length - 4) + val.Substring(val.Length - 4) 
                : new string('•', val.Length);

            return Ok(new { exists = true, maskedValue = masked });
        }

        // POST: api/settings/gemini-key
        [HttpPost("gemini-key")]
        public async Task<IActionResult> SaveGeminiKey([FromBody] SaveKeyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Key))
            {
                return BadRequest("API Key cannot be empty.");
            }

            var userId = GetUserId();
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GeminiApiKey" && s.UserId == userId);

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    UserId = userId,
                    Key = "GeminiApiKey",
                    Value = request.Key.Trim()
                };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                setting.Value = request.Key.Trim();
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Gemini API Key saved successfully." });
        }

        // GET: api/settings/groq-key
        [HttpGet("groq-key")]
        public async Task<IActionResult> GetGroqKeyStatus()
        {
            var userId = GetUserId();
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GroqApiKey" && s.UserId == userId);

            if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            {
                return Ok(new { exists = false, maskedValue = "" });
            }

            var val = setting.Value;
            var masked = val.Length > 8 
                ? new string('•', val.Length - 4) + val.Substring(val.Length - 4) 
                : new string('•', val.Length);

            return Ok(new { exists = true, maskedValue = masked });
        }

        // POST: api/settings/groq-key
        [HttpPost("groq-key")]
        public async Task<IActionResult> SaveGroqKey([FromBody] SaveKeyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Key))
            {
                return BadRequest("API Key cannot be empty.");
            }

            var userId = GetUserId();
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GroqApiKey" && s.UserId == userId);

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    UserId = userId,
                    Key = "GroqApiKey",
                    Value = request.Key.Trim()
                };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                setting.Value = request.Key.Trim();
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Groq API Key saved successfully." });
        }
    }

    public class SaveKeyRequest
    {
        public string Key { get; set; } = string.Empty;
    }
}
