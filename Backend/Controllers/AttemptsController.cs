using System.Text.Json;
using Backend.Contracts;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

[ApiController]
[Route("api")]
public sealed class AttemptsController(ExamDbContext database) : ControllerBase
{
    [HttpPost("exams/{examId:int}/attempts")]
    public async Task<ActionResult<AttemptStartedDto>> Start(
        int examId,
        [FromBody] StartAttemptRequest request,
        CancellationToken cancellationToken)
    {
        var learnerName = request.LearnerName.Trim();
        if (learnerName.Length is < 1 or > 100)
        {
            return BadRequest(new { error = "Enter a name of 1 to 100 characters." });
        }

        var exam = await LoadExam(examId, cancellationToken);
        if (exam is null || exam.PublishedAt is null)
        {
            return NotFound();
        }

        var orderedQuestions = BuildQuestionOrder(exam);
        var optionOrders = exam.Questions.ToDictionary(
            question => question.Id,
            question => question.ShuffleAnswers
                ? question.Options.Select(option => option.Letter).OrderBy(_ => Random.Shared.Next()).ToArray()
                : question.Options.OrderBy(option => option.Letter).Select(option => option.Letter).ToArray());
        var attempt = new Attempt
        {
            ExamId = exam.Id,
            LearnerName = learnerName,
            StartedAt = DateTimeOffset.UtcNow,
            QuestionOrderJson = JsonSerializer.Serialize(orderedQuestions.Select(question => question.Id)),
            OptionOrdersJson = JsonSerializer.Serialize(optionOrders)
        };

        database.Attempts.Add(attempt);
        await database.SaveChangesAsync(cancellationToken);

        return Ok(new AttemptStartedDto(
            attempt.Id,
            attempt.StartedAt.AddMinutes(exam.DurationMinutes),
            ExamDtoMapper.ToDetails(exam, orderedQuestions, includeAnswers: false, optionOrders)));
    }

    [HttpGet("attempts/{attemptId:int}")]
    public async Task<ActionResult<AttemptStateDto>> Get(int attemptId, CancellationToken cancellationToken)
    {
        var attempt = await LoadAttempt(attemptId, cancellationToken);
        if (attempt is null)
        {
            return NotFound();
        }

        if (attempt.SubmittedAt is null && DateTimeOffset.UtcNow >= Deadline(attempt))
        {
            Grade(attempt, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync(cancellationToken);
        }

        var orderedQuestions = GetQuestionOrder(attempt);
        var optionOrders = GetOptionOrders(attempt);
        var selectedAnswers = attempt.Answers.ToDictionary(answer => answer.QuestionId, answer => answer.SelectedLetter);
        return Ok(new AttemptStateDto(
            attempt.Id,
            Deadline(attempt),
            attempt.SubmittedAt is not null,
            ExamDtoMapper.ToDetails(
                attempt.Exam,
                orderedQuestions,
                includeAnswers: false,
                optionOrders,
                selectedAnswers)));
    }

    [HttpPut("attempts/{attemptId:int}/answers")]
    public async Task<IActionResult> SaveAnswers(
        int attemptId,
        [FromBody] SaveAnswersRequest request,
        CancellationToken cancellationToken)
    {
        var attempt = await LoadAttempt(attemptId, cancellationToken);
        if (attempt is null)
        {
            return NotFound();
        }
        if (attempt.SubmittedAt is not null)
        {
            return Conflict(new { error = "This attempt has already been submitted." });
        }
        if (DateTimeOffset.UtcNow >= Deadline(attempt))
        {
            Grade(attempt, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status410Gone, new { error = "Time expired; the attempt was submitted automatically." });
        }
        if (request.Answers.Count > attempt.Exam.Questions.Count
            || request.Answers.Select(answer => answer.QuestionId).Distinct().Count() != request.Answers.Count)
        {
            return BadRequest(new { error = "Answers must have unique question ids from this exam." });
        }

        var questionMap = attempt.Exam.Questions.ToDictionary(question => question.Id);
        foreach (var answer in request.Answers)
        {
            if (!questionMap.TryGetValue(answer.QuestionId, out var question))
            {
                return BadRequest(new { error = $"Question {answer.QuestionId} does not belong to this exam." });
            }
            var selected = string.IsNullOrWhiteSpace(answer.SelectedLetter)
                ? null
                : answer.SelectedLetter.Trim().ToUpperInvariant();
            if (selected is not null && question.Options.All(option => option.Letter != selected))
            {
                return BadRequest(new { error = $"Question {answer.QuestionId} has no option '{selected}'." });
            }

            var saved = attempt.Answers.SingleOrDefault(item => item.QuestionId == answer.QuestionId);
            if (saved is null)
            {
                attempt.Answers.Add(new AttemptAnswer
                {
                    QuestionId = answer.QuestionId,
                    SelectedLetter = selected,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                saved.SelectedLetter = selected;
                saved.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("attempts/{attemptId:int}/submit")]
    public async Task<ActionResult<AttemptResultDto>> Submit(int attemptId, CancellationToken cancellationToken)
    {
        var attempt = await LoadAttempt(attemptId, cancellationToken);
        if (attempt is null)
        {
            return NotFound();
        }
        if (attempt.SubmittedAt is null)
        {
            Grade(attempt, DateTimeOffset.UtcNow);
            await database.SaveChangesAsync(cancellationToken);
        }
        return Ok(ToResult(attempt));
    }

    [HttpGet("attempts/{attemptId:int}/result")]
    public async Task<ActionResult<AttemptResultDto>> Result(int attemptId, CancellationToken cancellationToken)
    {
        var attempt = await LoadAttempt(attemptId, cancellationToken);
        if (attempt is null)
        {
            return NotFound();
        }
        if (attempt.SubmittedAt is null)
        {
            return Conflict(new { error = "The attempt has not been submitted yet." });
        }
        return Ok(ToResult(attempt));
    }

    private async Task<Exam?> LoadExam(int id, CancellationToken cancellationToken) =>
        await database.Exams
            .Include(exam => exam.Passages)
            .Include(exam => exam.Questions).ThenInclude(question => question.Options)
            .AsSplitQuery()
            .SingleOrDefaultAsync(exam => exam.Id == id, cancellationToken);

    private async Task<Attempt?> LoadAttempt(int id, CancellationToken cancellationToken) =>
        await database.Attempts
            .Include(attempt => attempt.Answers)
            .Include(attempt => attempt.Exam).ThenInclude(exam => exam.Passages)
            .Include(attempt => attempt.Exam).ThenInclude(exam => exam.Questions).ThenInclude(question => question.Options)
            .AsSplitQuery()
            .SingleOrDefaultAsync(attempt => attempt.Id == id, cancellationToken);

    private static DateTimeOffset Deadline(Attempt attempt) =>
        attempt.StartedAt.AddMinutes(attempt.Exam.DurationMinutes);

    private static List<Question> BuildQuestionOrder(Exam exam)
    {
        var result = new List<Question>();
        foreach (var part in exam.Questions.GroupBy(question => question.Part).OrderBy(group => group.Key))
        {
            if (part.Key == 5)
            {
                var independent = part.OrderBy(question => question.Position).ToList();
                if (independent.Any(question => question.ShuffleQuestion))
                {
                    Shuffle(independent);
                }
                result.AddRange(independent);
                continue;
            }

            foreach (var passageGroup in part.GroupBy(question => question.PassageId).OrderBy(group => group.Min(question => question.Position)))
            {
                var groupedQuestions = passageGroup.OrderBy(question => question.Position).ToList();
                if (groupedQuestions.First().Passage?.ShuffleQuestions == true)
                {
                    Shuffle(groupedQuestions);
                }
                result.AddRange(groupedQuestions);
            }
        }
        return result;
    }

    private static void Shuffle<T>(IList<T> values)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swap = Random.Shared.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }

    private static List<Question> GetQuestionOrder(Attempt attempt)
    {
        var ids = JsonSerializer.Deserialize<int[]>(attempt.QuestionOrderJson) ?? [];
        var questionMap = attempt.Exam.Questions.ToDictionary(question => question.Id);
        return ids.Where(questionMap.ContainsKey).Select(id => questionMap[id]).ToList();
    }

    private static Dictionary<int, string[]> GetOptionOrders(Attempt attempt) =>
        JsonSerializer.Deserialize<Dictionary<int, string[]>>(attempt.OptionOrdersJson) ?? [];

    private static void Grade(Attempt attempt, DateTimeOffset submittedAt)
    {
        attempt.SubmittedAt = submittedAt;
        var validQuestions = attempt.Exam.Questions.ToDictionary(question => question.Id);
        foreach (var answer in attempt.Answers)
        {
            if (!validQuestions.TryGetValue(answer.QuestionId, out var question)
                || question.Options.All(option => option.Letter != answer.SelectedLetter))
            {
                answer.SelectedLetter = null;
            }
        }
    }

    private static AttemptResultDto ToResult(Attempt attempt)
    {
        var answers = attempt.Answers.ToDictionary(answer => answer.QuestionId, answer => answer.SelectedLetter);
        var results = attempt.Exam.Questions.OrderBy(question => question.Position).Select(question =>
        {
            var correct = question.Options.Single(option => option.IsCorrect).Letter;
            answers.TryGetValue(question.Id, out var selected);
            return new ResultQuestionDto(
                question.Id,
                question.Part,
                question.Prompt,
                question.Passage?.Content,
                question.Options.OrderBy(option => option.Letter).Select(option => new OptionDto(option.Letter, option.Text)).ToArray(),
                selected,
                correct,
                selected == correct);
        }).ToArray();
        var correctCount = results.Count(question => question.IsCorrect);
        var blankCount = results.Count(question => question.SelectedLetter is null);
        var total = results.Length;
        return new AttemptResultDto(
            attempt.Id,
            attempt.LearnerName,
            attempt.Exam.Title,
            total,
            correctCount,
            total - correctCount - blankCount,
            blankCount,
            total == 0 ? 0 : decimal.Round((decimal)correctCount / total * 100, 2),
            attempt.SubmittedAt,
            results);
    }
}
