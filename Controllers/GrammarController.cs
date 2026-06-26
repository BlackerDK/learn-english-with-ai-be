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
    public class GrammarController : BaseAuthController
    {
        private readonly AppDbContext _context;

        public GrammarController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/grammar
        [HttpGet]
        public async Task<ActionResult<IEnumerable<GrammarNote>>> GetGrammarNotes([FromQuery] string? search, [FromQuery] string? level)
        {
            var query = _context.GrammarNotes.Where(n => n.UserId == CurrentUserId).AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(n => n.Title.ToLower().Contains(lowerSearch) || n.Content.ToLower().Contains(lowerSearch));
            }

            if (!string.IsNullOrEmpty(level))
            {
                query = query.Where(n => n.Level == level);
            }

            return await query.OrderByDescending(n => n.CreatedAt).ToListAsync();
        }

        // GET: api/grammar/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<GrammarNote>> GetGrammarNote(Guid id)
        {
            var note = await _context.GrammarNotes.FindAsync(id);
            if (note == null) return NotFound();
            return note;
        }

        // POST: api/grammar
        [HttpPost]
        public async Task<ActionResult<GrammarNote>> CreateGrammarNote(GrammarNote note)
        {
            note.Id = Guid.NewGuid();
            note.UserId = CurrentUserId;
            note.CreatedAt = DateTime.UtcNow;
            note.UpdatedAt = DateTime.UtcNow;

            _context.GrammarNotes.Add(note);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetGrammarNote), new { id = note.Id }, note);
        }

        // PUT: api/grammar/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateGrammarNote(Guid id, GrammarNote note)
        {
            if (id != note.Id) return BadRequest();

            var existing = await _context.GrammarNotes.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Title = note.Title;
            existing.Content = note.Content;
            existing.Level = note.Level;
            existing.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!GrammarNoteExists(id)) return NotFound();
                throw;
            }

            return NoContent();
        }

        // DELETE: api/grammar/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteGrammarNote(Guid id)
        {
            var note = await _context.GrammarNotes.FindAsync(id);
            if (note == null) return NotFound();

            _context.GrammarNotes.Remove(note);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool GrammarNoteExists(Guid id)
        {
            return _context.GrammarNotes.Any(e => e.Id == id);
        }
    }
}
