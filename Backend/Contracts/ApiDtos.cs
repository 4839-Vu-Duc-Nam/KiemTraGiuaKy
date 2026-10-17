namespace Backend.Contracts;

public sealed record ExamSummaryDto(int Id, string Title, int DurationMinutes, int QuestionCount, bool IsPublished);

public sealed record OptionDto(string Letter, string Text);

public sealed record QuestionDto(
    int Id,
    int Part,
    int? PassageId,
    string? PassageContent,
    string Prompt,
    string? Hint,
    IReadOnlyList<OptionDto> Options,
    string? CorrectLetter,
    string? SelectedLetter);

public sealed record ExamDetailsDto(
    int Id,
    string Title,
    int DurationMinutes,
    int QuestionCount,
    IReadOnlyList<QuestionDto> Questions);

public sealed record ImportErrorDto(int Line, string Message);

public sealed record AttemptStartedDto(int AttemptId, DateTimeOffset Deadline, ExamDetailsDto Exam);

public sealed record AttemptStateDto(
    int AttemptId,
    DateTimeOffset Deadline,
    bool IsSubmitted,
    ExamDetailsDto Exam);

public sealed record AttemptResultDto(
    int AttemptId,
    string LearnerName,
    string ExamTitle,
    int TotalQuestions,
    int Correct,
    int Incorrect,
    int Blank,
    decimal ScorePercent,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<ResultQuestionDto> Questions);

public sealed record ResultQuestionDto(
    int QuestionId,
    int Part,
    string Prompt,
    string? PassageContent,
    IReadOnlyList<OptionDto> Options,
    string? SelectedLetter,
    string CorrectLetter,
    bool IsCorrect);

public sealed record QuestionDifficultyDto(int QuestionId, int Part, string Prompt, int IncorrectCount, decimal IncorrectPercent);

public sealed record ExamStatsDto(
    int Participants,
    decimal AverageScorePercent,
    int AboveAverage,
    int BelowAverage,
    int EqualToAverage,
    IReadOnlyList<QuestionDifficultyDto> HardestQuestions);

public sealed record StartAttemptRequest(string LearnerName);

public sealed record SaveAnswerRequest(int QuestionId, string? SelectedLetter);
public sealed record SaveAnswersRequest(IReadOnlyList<SaveAnswerRequest> Answers);

public sealed record UpdateQuestionRequest(
    string Prompt,
    string Hint,
    IReadOnlyList<UpdateOptionRequest> Options,
    string CorrectLetter);

public sealed record UpdateOptionRequest(string Letter, string Text);
public sealed record UpdatePassageRequest(string Content);
