using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Database;
using backend.Models;
using ExcelDataReader;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ListeningController : BaseAuthController
    {
        private readonly AppDbContext _context;

        public ListeningController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/listening
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ListeningLesson>>> GetListeningLessons([FromQuery] string? search, [FromQuery] string? level)
        {
            var query = _context.ListeningLessons.Where(l => l.UserId == CurrentUserId).AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(l => l.Title.ToLower().Contains(lowerSearch) || l.Transcript.ToLower().Contains(lowerSearch));
            }

            if (!string.IsNullOrEmpty(level))
            {
                query = query.Where(l => l.Level == level);
            }

            return await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
        }

        // GET: api/listening/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<ListeningLesson>> GetListeningLesson(Guid id)
        {
            var lesson = await _context.ListeningLessons.FindAsync(id);
            if (lesson == null) return NotFound();
            return lesson;
        }

        // POST: api/listening
        [HttpPost]
        public async Task<ActionResult<ListeningLesson>> CreateListeningLesson(ListeningLesson lesson)
        {
            lesson.Id = Guid.NewGuid();
            lesson.UserId = CurrentUserId;
            lesson.CreatedAt = DateTime.UtcNow;

            _context.ListeningLessons.Add(lesson);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetListeningLesson), new { id = lesson.Id }, lesson);
        }

        // PUT: api/listening/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateListeningLesson(Guid id, ListeningLesson lesson)
        {
            if (id != lesson.Id) return BadRequest();

            var existing = await _context.ListeningLessons.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Title = lesson.Title;
            existing.AudioUrl = lesson.AudioUrl;
            existing.Transcript = lesson.Transcript;
            existing.Translation = lesson.Translation;
            existing.Level = lesson.Level;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ListeningLessonExists(id)) return NotFound();
                throw;
            }

            return NoContent();
        }

        // DELETE: api/listening/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteListeningLesson(Guid id)
        {
            var lesson = await _context.ListeningLessons.FindAsync(id);
            if (lesson == null) return NotFound();

            _context.ListeningLessons.Remove(lesson);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ListeningLessonExists(Guid id)
        {
            return _context.ListeningLessons.Any(e => e.Id == id);
        }

        // POST: api/listening/import-excel
        [HttpPost("import-excel")]
        public async Task<ActionResult<IEnumerable<ListeningLesson>>> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Không có tệp nào được tải lên hoặc tệp trống.");

            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls")
                return BadRequest("Chỉ hỗ trợ tệp Excel (.xlsx hoặc .xls).");

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using var stream = file.OpenReadStream();
                using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
                var dataSet = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = false }
                });

                if (dataSet.Tables.Count == 0) return BadRequest("File Excel không chứa bảng dữ liệu nào.");

                var table = dataSet.Tables[0];
                if (table.Rows.Count == 0) return BadRequest("Bảng dữ liệu trống.");

                // Detect header row: Tên bài nghe / Trình độ / Đường dẫn / Nguyên văn / Đoạn dịch
                int colTitle = -1, colLevel = -1, colAudio = -1, colTranscript = -1, colTranslation = -1;
                int dataStartRow = 0;

                var firstRow = table.Rows[0];
                bool hasHeader = false;
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var h = firstRow[c]?.ToString()?.Trim().ToLowerInvariant() ?? "";
                    if (h.Contains("tên") || h.Contains("title") || h.Contains("bài"))
                    { colTitle = c; hasHeader = true; }
                    else if (h.Contains("trình độ") || h.Contains("level") || h.Contains("cấp"))
                    { colLevel = c; hasHeader = true; }
                    else if (h.Contains("đường dẫn") || h.Contains("audio") || h.Contains("url") || h.Contains("link"))
                    { colAudio = c; hasHeader = true; }
                    else if (h.Contains("nguyên văn") || h.Contains("transcript") || h.Contains("nội dung") || h.Contains("văn bản"))
                    { colTranscript = c; hasHeader = true; }
                    else if (h.Contains("đoạn dịch") || h.Contains("dịch") || h.Contains("translation"))
                    { colTranslation = c; hasHeader = true; }
                }

                if (hasHeader)
                {
                    dataStartRow = 1;
                }
                else
                {
                    // Fallback: Tên bài nghe | Trình độ | Đường dẫn | Nguyên văn | Đoạn dịch
                    colTitle       = 0;
                    colLevel       = table.Columns.Count > 1 ? 1 : -1;
                    colAudio       = table.Columns.Count > 2 ? 2 : -1;
                    colTranscript  = table.Columns.Count > 3 ? 3 : -1;
                    colTranslation = table.Columns.Count > 4 ? 4 : -1;
                }

                if (colTitle < 0)
                    return BadRequest("Không tìm thấy cột Tên bài nghe. Vui lòng kiểm tra lại file Excel.");

                var newLessons = new List<ListeningLesson>();
                for (int r = dataStartRow; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    var title = colTitle >= 0 ? row[colTitle]?.ToString()?.Trim() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    newLessons.Add(new ListeningLesson
                    {
                        Id          = Guid.NewGuid(),
                        UserId      = CurrentUserId,
                        Title       = title,
                        Level       = colLevel       >= 0 ? row[colLevel]?.ToString()?.Trim()       ?? "" : "",
                        AudioUrl    = colAudio       >= 0 ? row[colAudio]?.ToString()?.Trim()       ?? "" : "",
                        Transcript  = colTranscript  >= 0 ? row[colTranscript]?.ToString()?.Trim()  ?? "" : "",
                        Translation = colTranslation >= 0 ? row[colTranslation]?.ToString()?.Trim() ?? "" : "",
                        CreatedAt   = DateTime.UtcNow
                    });
                }

                if (newLessons.Any())
                {
                    _context.ListeningLessons.AddRange(newLessons);
                    await _context.SaveChangesAsync();
                }

                return Ok(newLessons);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi khi xử lý file Excel: {ex.Message}");
            }
        }
    }
}
