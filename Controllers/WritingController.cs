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
    public class WritingController : BaseAuthController
    {
        private readonly AppDbContext _context;
        private readonly GeminiService _geminiService;

        public WritingController(AppDbContext context, GeminiService geminiService)
        {
            _context = context;
            _geminiService = geminiService;
        }

        [HttpGet]
        public async Task<IActionResult> GetTopics()
        {
            var topics = await _context.WritingTopics
                .Where(t => t.UserId == CurrentUserId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
            return Ok(topics);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTopic([FromBody] WritingTopic request)
        {
            if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required");

            request.Id = Guid.NewGuid();
            request.UserId = CurrentUserId;
            request.CreatedAt = DateTime.UtcNow;

            _context.WritingTopics.Add(request);
            await _context.SaveChangesAsync();

            return Ok(request);
        }

        [HttpPost("batch")]
        public async Task<IActionResult> CreateTopicsBatch([FromBody] List<WritingTopic> requests)
        {
            if (requests == null || !requests.Any()) return BadRequest("No data provided");

            foreach(var req in requests)
            {
                if (string.IsNullOrWhiteSpace(req.Title)) continue;
                req.Id = Guid.NewGuid();
                req.UserId = CurrentUserId;
                req.CreatedAt = DateTime.UtcNow;
                _context.WritingTopics.Add(req);
            }
            await _context.SaveChangesAsync();

            return Ok(new { count = requests.Count });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTopic(Guid id, [FromBody] WritingTopic request)
        {
            var topic = await _context.WritingTopics.FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
            if (topic == null) return NotFound();

            topic.Title = request.Title;
            topic.Description = request.Description;
            topic.Level = request.Level;

            await _context.SaveChangesAsync();
            return Ok(topic);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTopic(Guid id)
        {
            var topic = await _context.WritingTopics.FirstOrDefaultAsync(t => t.Id == id && t.UserId == CurrentUserId);
            if (topic == null) return NotFound();

            var relatedHistories = await _context.WritingHistories.Where(h => h.TopicId == id && h.UserId == CurrentUserId).ToListAsync();
            _context.WritingHistories.RemoveRange(relatedHistories);

            _context.WritingTopics.Remove(topic);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("evaluate")]
        public async Task<IActionResult> EvaluateWriting([FromBody] EvaluateWritingRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SubmittedText))
                return BadRequest("Submitted text is required.");

            var topic = await _context.WritingTopics.FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == CurrentUserId);
            var topicTitle = topic?.Title ?? request.TopicTitle ?? "General Topic";
            var targetLevel = topic?.Level ?? "Intermediate";

            try
            {
                var feedbackJson = await _geminiService.EvaluateWritingJsonAsync(topicTitle, request.SubmittedText, targetLevel);

                // Parse the score out of the json
                int score = 0;
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(feedbackJson);
                    if (doc.RootElement.TryGetProperty("score", out var scoreProp))
                    {
                        score = scoreProp.GetInt32();
                    }
                }
                catch { }

                var history = new WritingHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = CurrentUserId,
                    TopicId = request.TopicId,
                    TopicTitle = topicTitle,
                    SubmittedText = request.SubmittedText,
                    Score = score,
                    FeedbackJson = feedbackJson,
                    TargetLevel = targetLevel,
                    SubmittedAt = DateTime.UtcNow
                };

                _context.WritingHistories.Add(history);
                await _context.SaveChangesAsync();

                return Ok(history);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var histories = await _context.WritingHistories
                .Where(h => h.UserId == CurrentUserId)
                .OrderByDescending(h => h.SubmittedAt)
                .ToListAsync();
            return Ok(histories);
        }
    }

    public class EvaluateWritingRequest
    {
        public Guid? TopicId { get; set; }
        public string TopicTitle { get; set; } = string.Empty;
        public string SubmittedText { get; set; } = string.Empty;
    }
}
