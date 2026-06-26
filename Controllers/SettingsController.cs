using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Database;
using backend.Models;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SettingsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/settings/gemini-key
        [HttpGet("gemini-key")]
        public async Task<IActionResult> GetGeminiKeyStatus()
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GeminiApiKey");

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

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GeminiApiKey");

            if (setting == null)
            {
                setting = new SystemSetting
                {
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
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GroqApiKey");

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

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GroqApiKey");

            if (setting == null)
            {
                setting = new SystemSetting
                {
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
