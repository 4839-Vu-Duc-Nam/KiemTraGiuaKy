namespace Backend.Models;

public sealed class Exam
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int DurationMinutes { get; set; } = 75;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public List<Passage> Passages { get; set; } = [];
    public List<Question> Questions { get; set; } = [];
    public List<Attempt> Attempts { get; set; } = [];
}

public sealed class Passage
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public string ExternalKey { get; set; } = "";
    public int Part { get; set; }
    public string Content { get; set; } = "";
    public bool ShuffleQuestions { get; set; }
    public List<Question> Questions { get; set; } = [];
}

public sealed class Question
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public int? PassageId { get; set; }
    public Passage? Passage { get; set; }
    public int Part { get; set; }
    public int Position { get; set; }
    public string Prompt { get; set; } = "";
    public string Hint { get; set; } = "";
    public bool ShuffleQuestion { get; set; }
    public bool ShuffleAnswers { get; set; }
    public List<AnswerOption> Options { get; set; } = [];
}

public sealed class AnswerOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public string Letter { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public sealed class Attempt
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public string LearnerName { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }
    public string QuestionOrderJson { get; set; } = "[]";
    public string OptionOrdersJson { get; set; } = "{}";
    public List<AttemptAnswer> Answers { get; set; } = [];
}

public sealed class AttemptAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public int QuestionId { get; set; }
    public string? SelectedLetter { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
