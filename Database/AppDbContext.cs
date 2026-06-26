using Microsoft.EntityFrameworkCore;
using backend.Models;

namespace backend.Database
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<AppUser> Users => Set<AppUser>();
        public DbSet<Vocabulary> Vocabularies => Set<Vocabulary>();
        public DbSet<FlashcardReview> FlashcardReviews => Set<FlashcardReview>();
        public DbSet<GrammarNote> GrammarNotes => Set<GrammarNote>();
        public DbSet<ListeningLesson> ListeningLessons => Set<ListeningLesson>();
        public DbSet<QuizHistory> QuizHistories => Set<QuizHistory>();
        public DbSet<SystemSetting> SystemSettings { get; set; }
        public DbSet<SpeakingPhrase> SpeakingPhrases { get; set; }
        public DbSet<SpeakingHistory> SpeakingHistories { get; set; }
        public DbSet<WritingTopic> WritingTopics { get; set; }
        public DbSet<WritingHistory> WritingHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Vocabulary Indexes
            modelBuilder.Entity<Vocabulary>()
                .HasIndex(v => v.Word);

            modelBuilder.Entity<Vocabulary>()
                .HasIndex(v => v.NextReviewDate);

            // Configure SystemSetting default values if needed
        }
    }
}
