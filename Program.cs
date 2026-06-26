using Microsoft.EntityFrameworkCore;
using backend.Database;
using backend.Services;
using backend.Middlewares;
using backend.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Register DbContext with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register HttpClient and GeminiService
builder.Services.AddHttpClient<GeminiService>();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "antigravity-lang-secret-key-2024-secure";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = "antigravity-lang",
            ValidAudience            = "antigravity-lang",
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Enable CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
              .SetIsOriginAllowed(_ => true)  // allow ngrok and any host
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowReactApp");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Ensure Database is Created & Seeded
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        
        // This will create the db file and tables if they do not exist
        context.Database.EnsureCreated();

        // Ensure new tables exist (EnsureCreated won't add tables to existing DB)
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Users (
                Id TEXT NOT NULL PRIMARY KEY,
                Username TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                DisplayName TEXT NOT NULL DEFAULT '',
                Role TEXT NOT NULL DEFAULT 'user',
                CreatedAt TEXT NOT NULL
            );
        ");
        context.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users(Username);");
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS SpeakingPhrases (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                Text TEXT NOT NULL,
                Translation TEXT NOT NULL DEFAULT '',
                Level TEXT NOT NULL DEFAULT '',
                Language TEXT NOT NULL DEFAULT 'en',
                CreatedAt TEXT NOT NULL
            );
        ");
        // Add UserId column to SpeakingPhrases if missing (old schema)
        try { context.Database.ExecuteSqlRaw("ALTER TABLE SpeakingPhrases ADD COLUMN UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';"); } catch { }
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS SpeakingHistories (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                PhraseId TEXT NULL,
                PhraseText TEXT NOT NULL,
                SpokenText TEXT NOT NULL DEFAULT '',
                Score INTEGER NOT NULL DEFAULT 0,
                Accuracy TEXT NOT NULL DEFAULT '',
                Feedback TEXT NOT NULL DEFAULT '',
                WordAnalysisJson TEXT NOT NULL DEFAULT '[]',
                Language TEXT NOT NULL DEFAULT 'en',
                PracticedAt TEXT NOT NULL
            );
        ");
        // Add UserId column to SpeakingHistories if missing (old schema)
        try { context.Database.ExecuteSqlRaw("ALTER TABLE SpeakingHistories ADD COLUMN UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';"); } catch { }

        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS WritingTopics (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                Title TEXT NOT NULL,
                Description TEXT NOT NULL,
                Level TEXT NOT NULL DEFAULT 'Intermediate',
                CreatedAt TEXT NOT NULL
            );
        ");

        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS WritingHistories (
                Id TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                TopicId TEXT NULL,
                TopicTitle TEXT NOT NULL DEFAULT '',
                SubmittedText TEXT NOT NULL,
                Score INTEGER NOT NULL DEFAULT 0,
                FeedbackJson TEXT NOT NULL DEFAULT '{{}}',
                TargetLevel TEXT NOT NULL DEFAULT '',
                SubmittedAt TEXT NOT NULL
            );
        ");

        // Add UserId column to existing tables if not exists
        try { context.Database.ExecuteSqlRaw("ALTER TABLE Vocabularies ADD COLUMN UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE GrammarNotes ADD COLUMN UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE ListeningLessons ADD COLUMN UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';"); } catch { }
        try { context.Database.ExecuteSqlRaw("ALTER TABLE QuizHistories ADD COLUMN UserId TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';"); } catch { }

        // Seed admin account
        var adminExists = context.Database.ExecuteSqlRaw("SELECT 1 FROM Users WHERE Username='admin' LIMIT 1");
        var adminUser = context.Users.FirstOrDefault(u => u.Username == "admin");
        if (adminUser == null)
        {
            adminUser = new AppUser
            {
                Id          = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Username    = "admin",
                DisplayName = "Admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456a@"),
                Role        = "admin",
                CreatedAt   = DateTime.UtcNow
            };
            context.Users.Add(adminUser);
            context.SaveChanges();
        }

        // Assign all existing data (UserId = Guid.Empty) to admin account
        var adminId = adminUser.Id.ToString();
        context.Database.ExecuteSqlRaw($"UPDATE Vocabularies SET UserId='{adminId}' WHERE UserId='00000000-0000-0000-0000-000000000000';");
        context.Database.ExecuteSqlRaw($"UPDATE GrammarNotes SET UserId='{adminId}' WHERE UserId='00000000-0000-0000-0000-000000000000';");
        context.Database.ExecuteSqlRaw($"UPDATE ListeningLessons SET UserId='{adminId}' WHERE UserId='00000000-0000-0000-0000-000000000000';");
        context.Database.ExecuteSqlRaw($"UPDATE QuizHistories SET UserId='{adminId}' WHERE UserId='00000000-0000-0000-0000-000000000000';");
        context.Database.ExecuteSqlRaw($"UPDATE SpeakingPhrases SET UserId='{adminId}' WHERE UserId='00000000-0000-0000-0000-000000000000';");
        context.Database.ExecuteSqlRaw($"UPDATE SpeakingHistories SET UserId='{adminId}' WHERE UserId='00000000-0000-0000-0000-000000000000';");

        // Migrate old Vocabulary statuses to the new buckets based on previous SrsIntervalDays learning progress
        context.Database.ExecuteSqlRaw(@"
            UPDATE Vocabularies 
            SET Status = CASE 
                WHEN SrsIntervalDays >= 7 THEN 'easy'
                WHEN SrsIntervalDays >= 3 THEN 'good'
                WHEN SrsIntervalDays >= 1 THEN 'hard'
                ELSE 'unknown'
            END;
        ");

        var srsDist = context.Vocabularies.GroupBy(v => v.SrsIntervalDays).Select(g => new { Key = g.Key, Count = g.Count() }).ToList();
        var statusDist = context.Vocabularies.GroupBy(v => v.Status).Select(g => new { Key = g.Key, Count = g.Count() }).ToList();
        Console.WriteLine("DEBUG - SRS Distribution:");
        foreach(var d in srsDist) Console.WriteLine($"SRS {d.Key}: {d.Count}");
        Console.WriteLine("DEBUG - Status Distribution:");
        foreach(var d in statusDist) Console.WriteLine($"Status {d.Key}: {d.Count}");

        // Seed Vocabulary if empty
        if (!context.Vocabularies.Any())
        {
            context.Vocabularies.AddRange(
                new Vocabulary
                {
                    Word = "Serendipity",
                    Pronunciation = "/ˌserənˈdipədē/",
                    Meaning = "Sự tình cờ may mắn (sự tìm ra những giá trị tốt đẹp một cách tình cờ)",
                    Example = "We found the charming little restaurant by pure serendipity.",
                    ExampleTranslation = "Chính phủ đang thực hiện các sáng kiến mới để bảo vệ môi trường.",
                    Notes = "Có thể dịch là 'sáng kiến', 'bước đầu', hoặc 'sự khởi xướng'.",
                    Status = "unknown",
                    Tags = "IELTS,Work,C2",
                    CreatedAt = DateTime.UtcNow,
                    NextReviewDate = DateTime.UtcNow,
                    SrsIntervalDays = 0
                },
                new Vocabulary
                {
                    Word = "Eloquent",
                    Pronunciation = "/ˈeləkwənt/",
                    Meaning = "Hùng biện, có tài ăn nói lưu loát và đầy sức thuyết phục",
                    Example = "She made an eloquent speech in defense of her client.",
                    ExampleTranslation = "Cô ấy đã có một bài phát biểu hùng biện để bảo vệ thân chủ của mình.",
                    Notes = "Tính từ mô tả người ăn nói có duyên và thuyết phục hoặc bài viết sắc bén.",
                    Status = "Learning",
                    Tags = "Business,IELTS,B2",
                    CreatedAt = DateTime.UtcNow,
                    NextReviewDate = DateTime.UtcNow,
                    SrsIntervalDays = 0
                },
                new Vocabulary
                {
                    Word = "Meticulous",
                    Pronunciation = "/məˈtikyələs/",
                    Meaning = "Tỉ mỉ, kỹ càng, quá chăm chút từng chi tiết nhỏ",
                    Example = "Many hours of meticulous preparation have gone into writing the book.",
                    ExampleTranslation = "Chương trình mới hứa hẹn sẽ tăng cường năng suất của nhân viên.",
                    Notes = "Động từ 'enhance' mang nghĩa cải thiện, làm cho tốt hơn.",
                    Status = "unknown",
                    Tags = "Academic,Work,C1",
                    CreatedAt = DateTime.UtcNow,
                    NextReviewDate = DateTime.UtcNow,
                    SrsIntervalDays = 0
                }
            );
        }

        // Seed Grammar Notes if empty
        if (!context.GrammarNotes.Any())
        {
            context.GrammarNotes.AddRange(
                new GrammarNote
                {
                    Title = "Phân biệt Present Perfect (Hiện tại hoàn thành) và Past Simple (Quá khứ đơn)",
                    Level = "Intermediate",
                    Content = @"### 1. Khái niệm cơ bản
* **Quá khứ đơn (Past Simple):** Dùng để diễn tả một hành động đã xảy ra và **kết thúc hoàn toàn** trong quá khứ, có thời gian xác định rõ ràng.
* **Hiện tại hoàn thành (Present Perfect):** Dùng để diễn tả hành động đã xảy ra trong quá khứ nhưng **còn liên quan đến hiện tại** (không quan trọng thời gian, hoặc vẫn đang tiếp diễn, hoặc để lại kết quả ở hiện tại).

### 2. Công thức so sánh
| Thì | Khẳng định | Phủ định | Nghi vấn | Từ nhận biết |
| :--- | :--- | :--- | :--- | :--- |
| **Past Simple** | S + V2/ed | S + didn't + V-infinitive | Did + S + V-inf? | *yesterday, ago, last week, in 2020* |
| **Present Perfect** | S + have/has + V3/ed | S + haven't/hasn't + V3/ed | Have/Has + S + V3/ed? | *already, yet, since, for, so far, ever* |

### 3. Ví dụ so sánh trực quan
* **Past Simple:**
  > I *lost* my keys yesterday. (Hôm qua tôi mất chìa khóa, hiện tại có thể tôi đã tìm thấy rồi).
* **Present Perfect:**
  > I *have lost* my keys. (Tôi đã mất chìa khóa rồi - nhấn mạnh kết quả hiện tại là tôi *vẫn chưa có chìa khóa* để vào nhà).

### 4. Bài tập luyện tập
Hãy tự đặt 3 câu so sánh trải nghiệm của bản thân bằng cả 2 thì này và hỏi **AI Tutor** để được chấm điểm nhé!",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            );
        }

        // Seed Listening Lesson if empty
        if (!context.ListeningLessons.Any())
        {
            context.ListeningLessons.AddRange(
                new ListeningLesson
                {
                    Title = "The Importance of Lifelong Learning",
                    Level = "Intermediate",
                    AudioUrl = "https://www.soundhelix.com/examples/mp3/SoundHelix-Song-1.mp3", // Free placeholder audio
                    Transcript = "Learning is not a process that ends when you graduate from school or university. In fact, that is just the beginning. Lifelong learning is the ongoing, voluntary, and self-motivated pursuit of knowledge for either personal or professional reasons. It not only enhances social inclusion, active citizenship, and personal development, but also self-sustainability, as well as competitiveness and employability.",
                    Translation = "Học tập không phải là một quá trình kết thúc khi bạn tốt nghiệp trường học hoặc đại học. Trên thực tế, đó chỉ là sự khởi đầu. Học tập suốt đời là sự theo đuổi kiến thức liên tục, tự nguyện và tự định hướng vì lý do cá nhân hoặc nghề nghiệp. Nó không chỉ tăng cường sự hòa nhập xã hội, tinh thần công dân tích cực và phát triển cá nhân, mà còn cả khả năng tự duy trì, cũng như khả năng cạnh tranh và cơ hội việc làm.",
                    CreatedAt = DateTime.UtcNow
                }
            );
        }

        context.SaveChanges();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
