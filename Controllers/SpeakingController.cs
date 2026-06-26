using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Database;
using backend.Models;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpeakingController : BaseAuthController
    {
        private readonly AppDbContext _context;

        public SpeakingController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/speaking/phrases?language=en&level=Intermediate
        [HttpGet("phrases")]
        public async Task<ActionResult<IEnumerable<SpeakingPhrase>>> GetPhrases(
            [FromQuery] string? language,
            [FromQuery] string? level,
            [FromQuery] string? search)
        {
            var query = _context.SpeakingPhrases.Where(p => p.UserId == CurrentUserId).AsQueryable();

            if (!string.IsNullOrEmpty(language))
                query = query.Where(p => p.Language == language);

            if (!string.IsNullOrEmpty(level))
                query = query.Where(p => p.Level == level);

            if (!string.IsNullOrEmpty(search))
            {
                var lower = search.ToLower();
                query = query.Where(p => p.Text.ToLower().Contains(lower) || p.Translation.ToLower().Contains(lower));
            }

            return await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        }

        // GET: api/speaking/phrases/daily?language=en&date=2024-01-15
        [HttpGet("phrases/daily")]
        public async Task<ActionResult<IEnumerable<SpeakingPhrase>>> GetDailyPhrases(
            [FromQuery] string language = "en",
            [FromQuery] string? date = null)
        {
            var targetDate = date != null ? DateTime.Parse(date) : DateTime.UtcNow.Date;
            var seed = targetDate.Year * 10000 + targetDate.Month * 100 + targetDate.Day;

            var phrases = await _context.SpeakingPhrases
                .Where(p => p.Language == language && p.UserId == CurrentUserId)
                .ToListAsync();

            if (!phrases.Any())
                return Ok(new List<SpeakingPhrase>());

            // Deterministic shuffle based on date seed — same day same result
            var rng = new Random(seed);
            var shuffled = phrases.OrderBy(_ => rng.Next()).Take(5).ToList();

            return Ok(shuffled);
        }

        // GET: api/speaking/phrases/{id}
        [HttpGet("phrases/{id:guid}")]
        public async Task<ActionResult<SpeakingPhrase>> GetPhrase(Guid id)
        {
            var phrase = await _context.SpeakingPhrases.FindAsync(id);
            if (phrase == null) return NotFound();
            return phrase;
        }

        // POST: api/speaking/phrases
        [HttpPost("phrases")]
        public async Task<ActionResult<SpeakingPhrase>> CreatePhrase(SpeakingPhrase phrase)
        {
            phrase.Id = Guid.NewGuid();
            phrase.UserId = CurrentUserId;
            phrase.CreatedAt = DateTime.UtcNow;

            _context.SpeakingPhrases.Add(phrase);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPhrase), new { id = phrase.Id }, phrase);
        }

        // POST: api/speaking/phrases/bulk — import nhiều câu cùng lúc
        [HttpPost("phrases/bulk")]
        public async Task<ActionResult<IEnumerable<SpeakingPhrase>>> BulkCreatePhrases([FromBody] List<SpeakingPhrase> phrases)
        {
            if (phrases == null || !phrases.Any())
                return BadRequest("Danh sách câu không được rỗng.");

            foreach (var phrase in phrases)
            {
                phrase.Id = Guid.NewGuid();
                phrase.UserId = CurrentUserId;
                phrase.CreatedAt = DateTime.UtcNow;
            }

            _context.SpeakingPhrases.AddRange(phrases);
            await _context.SaveChangesAsync();

            return Ok(phrases);
        }

        // PUT: api/speaking/phrases/{id}
        [HttpPut("phrases/{id:guid}")]
        public async Task<IActionResult> UpdatePhrase(Guid id, SpeakingPhrase phrase)
        {
            if (id != phrase.Id) return BadRequest();

            var existing = await _context.SpeakingPhrases.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Text = phrase.Text;
            existing.Translation = phrase.Translation;
            existing.Level = phrase.Level;
            existing.Language = phrase.Language;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.SpeakingPhrases.Any(p => p.Id == id)) return NotFound();
                throw;
            }

            return NoContent();
        }

        // DELETE: api/speaking/phrases/{id}
        [HttpDelete("phrases/{id:guid}")]
        public async Task<IActionResult> DeletePhrase(Guid id)
        {
            var phrase = await _context.SpeakingPhrases.FindAsync(id);
            if (phrase == null) return NotFound();

            _context.SpeakingPhrases.Remove(phrase);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // GET: api/speaking/history?language=en&page=1&pageSize=20
        [HttpGet("history")]
        public async Task<ActionResult<PagedSpeakingHistory>> GetHistory(
            [FromQuery] string? language,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            var query = _context.SpeakingHistories.Where(h => h.UserId == CurrentUserId).AsQueryable();

            if (!string.IsNullOrEmpty(language))
                query = query.Where(h => h.Language == language);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(h => h.PracticedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new PagedSpeakingHistory
            {
                TotalItems = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)total / pageSize),
                Items = items
            });
        }

        // POST: api/speaking/history
        [HttpPost("history")]
        public async Task<ActionResult<SpeakingHistory>> SaveHistory(SpeakingHistory history)
        {
            history.Id = Guid.NewGuid();
            history.UserId = CurrentUserId;
            history.PracticedAt = DateTime.UtcNow;

            _context.SpeakingHistories.Add(history);
            await _context.SaveChangesAsync();

            return Ok(history);
        }

        // DELETE: api/speaking/history/{id}
        [HttpDelete("history/{id:guid}")]
        public async Task<IActionResult> DeleteHistory(Guid id)
        {
            var history = await _context.SpeakingHistories.FindAsync(id);
            if (history == null) return NotFound();

            _context.SpeakingHistories.Remove(history);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    public class PagedSpeakingHistory
    {
        public int TotalItems { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public IEnumerable<SpeakingHistory> Items { get; set; } = Enumerable.Empty<SpeakingHistory>();
    }
}
