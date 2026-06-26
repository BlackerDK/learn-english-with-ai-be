using System;
using System.ComponentModel.DataAnnotations;

namespace backend.Models
{
    public class AppUser
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Role { get; set; } = "user"; // user | admin

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Vocabulary
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; } = Guid.Empty;

        [Required]
        public string Word { get; set; } = string.Empty;

        public string Pronunciation { get; set; } = string.Empty;

        [Required]
        public string Meaning { get; set; } = string.Empty;

        public string Example { get; set; } = string.Empty;

        public string ExampleTranslation { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public string Status { get; set; } = "New";

        public string Tags { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime NextReviewDate { get; set; } = DateTime.UtcNow;

        public int SrsIntervalDays { get; set; } = 0;
    }

    public class FlashcardReview
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid VocabularyId { get; set; }

        public DateTime ReviewDate { get; set; } = DateTime.UtcNow;

        [Required]
        public string Rating { get; set; } = string.Empty;

        public DateTime NextReviewDate { get; set; }
    }

    public class GrammarNote
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; } = Guid.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        [Required]
        public string Level { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ListeningLesson
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; } = Guid.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        public string AudioUrl { get; set; } = string.Empty;

        [Required]
        public string Transcript { get; set; } = string.Empty;

        public string Translation { get; set; } = string.Empty;

        public string Level { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class QuizHistory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; } = Guid.Empty;

        [Required]
        public string Topic { get; set; } = string.Empty;

        public int Score { get; set; }

        public int TotalQuestions { get; set; }

        public DateTime DatePlayed { get; set; } = DateTime.UtcNow;

        public string DetailsJson { get; set; } = "[]";
    }

    public class SystemSetting
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [Required]
        public string Key { get; set; } = string.Empty;

        [Required]
        public string Value { get; set; } = string.Empty;
    }

    public class SpeakingPhrase
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; } = Guid.Empty;

        [Required]
        public string Text { get; set; } = string.Empty;

        public string Translation { get; set; } = string.Empty;

        public string Level { get; set; } = string.Empty;

        public string Language { get; set; } = "en";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SpeakingHistory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; } = Guid.Empty;

        public Guid? PhraseId { get; set; }

        [Required]
        public string PhraseText { get; set; } = string.Empty;

        public string SpokenText { get; set; } = string.Empty;

        public int Score { get; set; }

        public string Accuracy { get; set; } = string.Empty;

        public string Feedback { get; set; } = string.Empty;

        public string WordAnalysisJson { get; set; } = "[]";

        public string Language { get; set; } = "en";

        public DateTime PracticedAt { get; set; } = DateTime.UtcNow;
    }

    public class WritingTopic
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; } = Guid.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        public string Level { get; set; } = "Intermediate";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class WritingHistory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; } = Guid.Empty;

        public Guid? TopicId { get; set; }

        public string TopicTitle { get; set; } = string.Empty;

        [Required]
        public string SubmittedText { get; set; } = string.Empty;

        public int Score { get; set; }

        public string FeedbackJson { get; set; } = "{}";

        public string TargetLevel { get; set; } = string.Empty;

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    }
}
