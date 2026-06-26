using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Database;
using backend.Models;
using backend.Services;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuizController : BaseAuthController
    {
        private readonly AppDbContext _context;
        private readonly GeminiService _geminiService;

        public QuizController(AppDbContext context, GeminiService geminiService)
        {
            _context = context;
            _geminiService = geminiService;
        }

        // GET: api/quiz/history
        [HttpGet("history")]
        public async Task<ActionResult<IEnumerable<QuizHistory>>> GetQuizHistory()
        {
            return await _context.QuizHistories
                .Where(q => q.UserId == CurrentUserId)
                .OrderByDescending(q => q.DatePlayed)
                .ToListAsync();
        }

        // POST: api/quiz/history
        [HttpPost("history")]
        public async Task<ActionResult<QuizHistory>> SaveQuizHistory(QuizHistory history)
        {
            history.Id = Guid.NewGuid();
            history.UserId = CurrentUserId;
            history.DatePlayed = DateTime.UtcNow;

            _context.QuizHistories.Add(history);
            await _context.SaveChangesAsync();

            return Ok(history);
        }

        // POST: api/quiz/generate
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateQuiz([FromBody] GenerateQuizRequest request)
        {
            try
            {
                var vocabularyContext = "";
                if (request.UseVocabulary)
                {
                    // Fetch up to 15 vocabulary words to test
                    var words = await _context.Vocabularies
                        .Where(v => v.UserId == CurrentUserId)
                        .OrderBy(v => EF.Functions.Random()) // SQLite Random
                        .Take(15)
                        .Select(v => $"{v.Word} (Meaning: {v.Meaning}, Example: {v.Example})")
                        .ToListAsync();

                    if (words.Any())
                    {
                        vocabularyContext = string.Join("\n", words);
                    }
                }

                var count = request.Count <= 0 ? 5 : request.Count;
                var topic = string.IsNullOrWhiteSpace(request.Topic) ? "General Language Skills" : request.Topic;

                var quizJson = await _geminiService.GenerateQuizJsonAsync(topic, count, vocabularyContext);
                return Content(quizJson, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class GenerateQuizRequest
    {
        public string Topic { get; set; } = string.Empty;
        public int Count { get; set; } = 5;
        public bool UseVocabulary { get; set; } = false;
    }
}
