using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Database;
using backend.Models;
using backend.Services;
using System.Text.Json;
using ExcelDataReader;
namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VocabularyController : BaseAuthController
    {
        private readonly AppDbContext _context;
        private readonly GeminiService _geminiService;

        public VocabularyController(AppDbContext context, GeminiService geminiService)
        {
            _context = context;
            _geminiService = geminiService;
        }

        // GET: api/vocabulary
        [HttpGet]
        public async Task<ActionResult<PagedResponse<Vocabulary>>> GetVocabularies(
            [FromQuery] string? search,
            [FromQuery] string? tag,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 12;

            var query = _context.Vocabularies.Where(v => v.UserId == CurrentUserId).AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(v => v.Word.ToLower().Contains(lowerSearch) || v.Meaning.ToLower().Contains(lowerSearch));
            }

            if (!string.IsNullOrEmpty(tag))
            {
                var lowerTag = tag.ToLower();
                query = query.Where(v => v.Tags.ToLower().Contains(lowerTag));
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(v => v.Status == status);
            }

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            var items = await query
                .OrderByDescending(v => v.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResponse<Vocabulary>
            {
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                Items = items
            };
        }

        // GET: api/vocabulary/due
        [HttpGet("due")]
        public async Task<ActionResult<IEnumerable<Vocabulary>>> GetDueVocabularies()
        {
            var today = DateTime.UtcNow;
            return await _context.Vocabularies
                .Where(v => v.UserId == CurrentUserId && v.NextReviewDate <= today)
                .OrderBy(v => v.NextReviewDate)
                .ToListAsync();
        }

        // GET: api/vocabulary/tags
        [HttpGet("tags")]
        public async Task<ActionResult<IEnumerable<string>>> GetTags()
        {
            var tagsList = await _context.Vocabularies
                .Where(v => v.UserId == CurrentUserId)
                .Select(v => v.Tags)
                .ToListAsync();

            var uniqueTags = tagsList
                .SelectMany(t => t.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(t => t.Trim())
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            return uniqueTags;
        }

        // GET: api/vocabulary/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<Vocabulary>> GetVocabulary(Guid id)
        {
            var vocabulary = await _context.Vocabularies.FindAsync(id);
            if (vocabulary == null) return NotFound();
            return vocabulary;
        }

        // POST: api/vocabulary
        [HttpPost]
        public async Task<ActionResult<Vocabulary>> CreateVocabulary(Vocabulary vocabulary)
        {
            vocabulary.Id = Guid.NewGuid();
            vocabulary.UserId = CurrentUserId;
            vocabulary.CreatedAt = DateTime.UtcNow;
            vocabulary.NextReviewDate = DateTime.UtcNow; // Review immediately
            vocabulary.SrsIntervalDays = 0;
            vocabulary.Status = "unknown";

            _context.Vocabularies.Add(vocabulary);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetVocabulary), new { id = vocabulary.Id }, vocabulary);
        }

        // PUT: api/vocabulary/{id}
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateVocabulary(Guid id, Vocabulary vocabulary)
        {
            if (id != vocabulary.Id) return BadRequest();

            var existing = await _context.Vocabularies.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Word = vocabulary.Word;
            existing.Pronunciation = vocabulary.Pronunciation;
            existing.Meaning = vocabulary.Meaning;
            existing.Example = vocabulary.Example;
            existing.ExampleTranslation = vocabulary.ExampleTranslation;
            existing.Notes = vocabulary.Notes;
            existing.Tags = vocabulary.Tags;
            existing.Status = vocabulary.Status;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!VocabularyExists(id)) return NotFound();
                throw;
            }

            return NoContent();
        }

        // DELETE: api/vocabulary/{id}
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteVocabulary(Guid id)
        {
            var vocabulary = await _context.Vocabularies.FindAsync(id);
            if (vocabulary == null) return NotFound();

            // Delete associated reviews as well
            var reviews = _context.FlashcardReviews.Where(r => r.VocabularyId == id);
            _context.FlashcardReviews.RemoveRange(reviews);

            _context.Vocabularies.Remove(vocabulary);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // POST: api/vocabulary/{id}/review
        [HttpPost("{id:guid}/review")]
        public async Task<ActionResult<Vocabulary>> ReviewVocabulary(Guid id, [FromBody] ReviewRequest request)
        {
            var vocabulary = await _context.Vocabularies.FindAsync(id);
            if (vocabulary == null) return NotFound();

            var rating = request.Rating.Trim().ToLower();

            var validStatuses = new[] { "unknown", "hard", "good", "easy" };
            if (validStatuses.Contains(rating))
            {
                vocabulary.Status = rating;
            }

            // Create review log (optional, for tracking)
            var review = new FlashcardReview
            {
                Id = Guid.NewGuid(),
                VocabularyId = id,
                ReviewDate = DateTime.UtcNow,
                Rating = rating,
                NextReviewDate = DateTime.UtcNow // Unused in status-based
            };
            _context.FlashcardReviews.Add(review);

            await _context.SaveChangesAsync();

            return vocabulary;
        }

        // GET: api/vocabulary/flashcard-stats
        [HttpGet("flashcard-stats")]
        public async Task<ActionResult<object>> GetFlashcardStats()
        {
            var stats = await _context.Vocabularies
                .Where(v => v.UserId == CurrentUserId)
                .GroupBy(v => v.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = new
            {
                unknown = stats.FirstOrDefault(s => s.Status == "unknown")?.Count ?? 0,
                hard = stats.FirstOrDefault(s => s.Status == "hard")?.Count ?? 0,
                good = stats.FirstOrDefault(s => s.Status == "good")?.Count ?? 0,
                easy = stats.FirstOrDefault(s => s.Status == "easy")?.Count ?? 0,
                total = stats.Sum(s => s.Count)
            };

            return result;
        }

        // GET: api/vocabulary/debug-stats
        [HttpGet("debug-stats")]
        public async Task<ActionResult<object>> GetDebugStats()
        {
            var reviewsCount = await _context.FlashcardReviews.CountAsync();
            var srsDist = await _context.Vocabularies
                .GroupBy(v => v.SrsIntervalDays)
                .Select(g => new { SrsIntervalDays = g.Key, Count = g.Count() })
                .ToListAsync();
            var statusDist = await _context.Vocabularies
                .GroupBy(v => v.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            return new { ReviewsCount = reviewsCount, SrsDistribution = srsDist, StatusDistribution = statusDist };
        }

        // GET: api/vocabulary/review
        [HttpGet("review")]
        public async Task<ActionResult<IEnumerable<Vocabulary>>> GetReviewVocabularies([FromQuery] string status)
        {
            if (string.IsNullOrEmpty(status)) return BadRequest("Status is required");

            return await _context.Vocabularies
                .Where(v => v.UserId == CurrentUserId && v.Status == status)
                .OrderBy(v => EF.Functions.Random()) // Random order for review
                .Take(50) // Limit to 50 per session
                .ToListAsync();
        }

        private bool VocabularyExists(Guid id)
        {
            return _context.Vocabularies.Any(e => e.Id == id);
        }

        // POST: api/vocabulary/extract-from-document
        [HttpPost("extract-from-document")]
        public async Task<ActionResult<IEnumerable<Vocabulary>>> ExtractFromDocument([FromBody] ExtractDocumentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest("Document text cannot be empty.");
            }

            try
            {
                var jsonStr = await _geminiService.ExtractVocabularyJsonAsync(request.Text);
                var extractedList = JsonSerializer.Deserialize<List<VocabularyExtractionDto>>(jsonStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (extractedList == null || !extractedList.Any())
                {
                    return Ok(new List<Vocabulary>());
                }

                var newVocabs = new List<Vocabulary>();
                foreach (var item in extractedList)
                {
                    // Skip if word is completely empty
                    if (string.IsNullOrWhiteSpace(item.Word)) continue;

                    var vocab = new Vocabulary
                    {
                        Id = Guid.NewGuid(),
                        UserId = CurrentUserId,
                        Word = item.Word,
                        Pronunciation = item.Pronunciation ?? "",
                        Meaning = item.Meaning ?? "",
                        Example = item.Example ?? "",
                        ExampleTranslation = item.ExampleTranslation ?? "",
                        Tags = string.IsNullOrWhiteSpace(item.Tags) ? "Document" : item.Tags,
                        Status = "New",
                        CreatedAt = DateTime.UtcNow,
                        NextReviewDate = DateTime.UtcNow,
                        SrsIntervalDays = 0
                    };
                    newVocabs.Add(vocab);
                }

                if (newVocabs.Any())
                {
                    _context.Vocabularies.AddRange(newVocabs);
                    await _context.SaveChangesAsync();
                }

                return Ok(newVocabs);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        // POST: api/vocabulary/import-excel
        [HttpPost("import-excel")]
        public async Task<ActionResult<IEnumerable<Vocabulary>>> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("Không có tệp nào được tải lên hoặc tệp trống.");
            }

            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls")
            {
                return BadRequest("Chỉ hỗ trợ tệp Excel (.xlsx hoặc .xls).");
            }

            try
            {
                // Register encoding provider for supporting .xls (BIFF) format
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using var stream = file.OpenReadStream();
                using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
                var dataSet = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration
                    {
                        UseHeaderRow = false
                    }
                });

                if (dataSet.Tables.Count == 0)
                {
                    return BadRequest("File Excel không chứa bảng dữ liệu nào.");
                }

                var table = dataSet.Tables[0];
                if (table.Rows.Count == 0)
                {
                    return BadRequest("Bảng dữ liệu trống.");
                }

                // Detect headers from the first row
                int colWord = -1, colPron = -1, colMeaning = -1, colExample = -1, colExTrans = -1;
                int dataStartRow = 0;

                var firstRow = table.Rows[0];
                bool hasHeader = false;
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var header = firstRow[c]?.ToString()?.Trim().ToLowerInvariant() ?? "";
                    if (header.Contains("từ") || header.Contains("word") || header.Contains("vocabulary"))
                    { colWord = c; hasHeader = true; }
                    else if (header.Contains("âm") || header.Contains("pron") || header.Contains("ipa") || header.Contains("phiên"))
                    { colPron = c; hasHeader = true; }
                    else if (header.Contains("nghĩa") || header.Contains("mean") || header.Contains("def") || header.Contains("định"))
                    { colMeaning = c; hasHeader = true; }
                    else if (header.Contains("ví dụ") || header.Contains("example"))
                    { colExample = c; hasHeader = true; }
                    else if (header.Contains("dịch") || header.Contains("translation"))
                    { colExTrans = c; hasHeader = true; }
                }

                if (hasHeader)
                {
                    dataStartRow = 1;
                }
                else
                {
                    // Fallback to positional mapping
                    colWord = 0;
                    colPron = table.Columns.Count > 1 ? 1 : -1;
                    colMeaning = table.Columns.Count > 2 ? 2 : -1;
                    colExample = table.Columns.Count > 3 ? 3 : -1;
                    colExTrans = table.Columns.Count > 4 ? 4 : -1;
                }

                if (colWord < 0)
                {
                    return BadRequest("Không tìm thấy cột Từ vựng (Word). Vui lòng kiểm tra lại tệp Excel.");
                }

                var newVocabs = new List<Vocabulary>();
                for (int r = dataStartRow; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    var word = colWord >= 0 ? row[colWord]?.ToString()?.Trim() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(word)) continue;

                    var vocab = new Vocabulary
                    {
                        Id = Guid.NewGuid(),
                        UserId = CurrentUserId,
                        Word = word,
                        Pronunciation = colPron >= 0 ? row[colPron]?.ToString()?.Trim() ?? "" : "",
                        Meaning = colMeaning >= 0 ? row[colMeaning]?.ToString()?.Trim() ?? "" : "",
                        Example = colExample >= 0 ? row[colExample]?.ToString()?.Trim() ?? "" : "",
                        ExampleTranslation = colExTrans >= 0 ? row[colExTrans]?.ToString()?.Trim() ?? "" : "",
                        Tags = "Excel Import",
                        Status = "unknown",
                        CreatedAt = DateTime.UtcNow,
                        NextReviewDate = DateTime.UtcNow,
                        SrsIntervalDays = 0
                    };
                    newVocabs.Add(vocab);
                }

                if (newVocabs.Any())
                {
                    _context.Vocabularies.AddRange(newVocabs);
                    await _context.SaveChangesAsync();
                }

                return Ok(newVocabs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi khi xử lý file Excel: {ex.Message}");
            }
        }

        // POST: api/vocabulary/{id}/autofill
        [HttpPost("{id:guid}/autofill")]
        public async Task<ActionResult<Vocabulary>> AutofillVocabulary(Guid id)
        {
            var vocabulary = await _context.Vocabularies.FindAsync(id);
            if (vocabulary == null) return NotFound();

            try
            {
                var jsonStr = await _geminiService.LookUpVocabularyJsonAsync(vocabulary.Word);
                var details = JsonSerializer.Deserialize<VocabularyDetailsDto>(jsonStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (details != null)
                {
                    vocabulary.Pronunciation = string.IsNullOrWhiteSpace(details.Pronunciation) ? vocabulary.Pronunciation : details.Pronunciation;
                    vocabulary.Meaning = string.IsNullOrWhiteSpace(details.Meaning) ? vocabulary.Meaning : details.Meaning;
                    vocabulary.Example = string.IsNullOrWhiteSpace(details.Example) ? vocabulary.Example : details.Example;
                    vocabulary.ExampleTranslation = string.IsNullOrWhiteSpace(details.ExampleTranslation) ? vocabulary.ExampleTranslation : details.ExampleTranslation;

                    await _context.SaveChangesAsync();
                }

                return Ok(vocabulary);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        // GET: api/vocabulary/lookup
        [HttpGet("lookup")]
        public async Task<ActionResult<VocabularyDetailsDto>> LookupWord([FromQuery] string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return BadRequest("Word cannot be empty.");

            try
            {
                var jsonStr = await _geminiService.LookUpVocabularyJsonAsync(word);
                var details = JsonSerializer.Deserialize<VocabularyDetailsDto>(jsonStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return Ok(details);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }

    public class ReviewRequest
    {
        public string Rating { get; set; } = string.Empty; // Hard, Good, Easy
    }

    public class ExtractDocumentRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class VocabularyExtractionDto
    {
        public string Word { get; set; } = string.Empty;
        public string Pronunciation { get; set; } = string.Empty;
        public string Meaning { get; set; } = string.Empty;
        public string Example { get; set; } = string.Empty;
        public string ExampleTranslation { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
    }

    public class VocabularyDetailsDto
    {
        public string Pronunciation { get; set; } = string.Empty;
        public string Meaning { get; set; } = string.Empty;
        public string Example { get; set; } = string.Empty;
        public string ExampleTranslation { get; set; } = string.Empty;
    }

    public class PagedResponse<T>
    {
        public int TotalItems { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
    }
}
