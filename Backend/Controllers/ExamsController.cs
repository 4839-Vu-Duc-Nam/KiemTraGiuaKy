using Backend.Contracts;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

[ApiController]
[Route("api/exams")]
public sealed class ExamsController(ExamDbContext database, DocxExamParser parser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExamSummaryDto>>> List(
        [FromQuery] bool includeDrafts = false,
        CancellationToken cancellationToken = default)
    {
        var query = database.Exams.AsNoTracking().Include(exam => exam.Questions).AsQueryable();
        if (!includeDrafts)
        {
            query = query.Where(exam => exam.PublishedAt != null);
        }

        var exams = await query.OrderByDescending(exam => exam.CreatedAt)
            .ToListAsync(cancellationToken);
        return Ok(exams.Select(ExamDtoMapper.ToSummary).ToArray());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ExamDetailsDto>> Get(
        int id,
        [FromQuery] bool preview = false,
        CancellationToken cancellationToken = default)
    {
        var exam = await LoadExam(id, cancellationToken);
        if (exam is null || (!preview && exam.PublishedAt is null))
        {
            return NotFound();
        }

        return Ok(ExamDtoMapper.ToDetails(
            exam,
            exam.Questions.OrderBy(question => question.Position).ToArray(),
            includeAnswers: preview));
    }

    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult> Import(
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { errors = new[] { new ImportErrorDto(1, "Choose a non-empty .docx file.") } });
        }
        if (file.Length > 10 * 1024 * 1024)
        {
            return BadRequest(new { errors = new[] { new ImportErrorDto(1, "The file must be 10 MB or smaller.") } });
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { errors = new[] { new ImportErrorDto(1, "Only .docx files are supported.") } });
        }

        (ExamDraft? Draft, IReadOnlyList<ImportError> Errors) parsed;
        try
        {
            await using var stream = file.OpenReadStream();
            parsed = await parser.ParseAsync(stream);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { errors = new[] { new ImportErrorDto(1, exception.Message) } });
        }
        catch (System.Xml.XmlException exception)
        {
            return BadRequest(new { errors = new[] { new ImportErrorDto(1, $"The Word document is invalid: {exception.Message}") } });
        }

        if (parsed.Errors.Count > 0 || parsed.Draft is null)
        {
            return BadRequest(new
            {
                errors = parsed.Errors.Select(error => new ImportErrorDto(error.Line, error.Message)).ToArray()
            });
        }

        var exam = new Exam
        {
            Title = parsed.Draft.Title,
            DurationMinutes = parsed.Draft.DurationMinutes
        };
        var passageMap = parsed.Draft.Passages.ToDictionary(
            item => item.Key,
            item => new Passage
            {
                Exam = exam,
                ExternalKey = item.Key,
                Part = item.Part,
                Content = item.Content,
                ShuffleQuestions = item.ShuffleQuestions
            },
            StringComparer.OrdinalIgnoreCase);

        foreach (var passage in passageMap.Values)
        {
            exam.Passages.Add(passage);
        }
        for (var index = 0; index < parsed.Draft.Questions.Count; index++)
        {
            var draft = parsed.Draft.Questions[index];
            var question = new Question
            {
                Exam = exam,
                Passage = draft.PassageKey is null ? null : passageMap[draft.PassageKey],
                Part = draft.Part,
                Position = index + 1,
                Prompt = draft.Prompt,
                Hint = draft.Hint,
                ShuffleQuestion = draft.ShuffleQuestion,
                ShuffleAnswers = draft.ShuffleAnswers,
                Options = draft.Options.Select(option => new AnswerOption
                {
                    Letter = option.Letter,
                    Text = option.Text,
                    IsCorrect = option.Letter == draft.CorrectLetter
                }).ToList()
            };
            exam.Questions.Add(question);
        }

        database.Exams.Add(exam);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(Get),
            new { id = exam.Id, preview = true },
            ExamDtoMapper.ToSummary(exam));
    }

    [HttpPost("{id:int}/publish")]
    public async Task<ActionResult<ExamSummaryDto>> Publish(int id, CancellationToken cancellationToken)
    {
        var exam = await LoadExam(id, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }
        if (exam.Questions.Count != 100)
        {
            return BadRequest(new { error = "Only a complete 100-question exam can be published." });
        }

        exam.PublishedAt ??= DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ExamDtoMapper.ToSummary(exam));
    }

    [HttpPut("{id:int}/questions/{questionId:int}")]
    public async Task<IActionResult> UpdateQuestion(
        int id,
        int questionId,
        [FromBody] UpdateQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var question = await database.Questions
            .Include(item => item.Options)
            .SingleOrDefaultAsync(item => item.Id == questionId && item.ExamId == id, cancellationToken);
        if (question is null)
        {
            return NotFound();
        }
        var isPublished = await database.Exams.AnyAsync(exam => exam.Id == id && exam.PublishedAt != null, cancellationToken);
        if (isPublished)
        {
            return Conflict(new { error = "Published exams cannot be edited." });
        }
        if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.Hint))
        {
            return BadRequest(new { error = "Question prompt and hint are required." });
        }
        var options = request.Options
            .Select(option => new UpdateOptionRequest(option.Letter.Trim().ToUpperInvariant(), option.Text.Trim()))
            .ToArray();
        if (options.Length != 4
            || options.Select(option => option.Letter).Distinct(StringComparer.Ordinal).Count() != 4
            || !options.Select(option => option.Letter).Order().SequenceEqual(["A", "B", "C", "D"])
            || options.Any(option => option.Text.Length == 0))
        {
            return BadRequest(new { error = "Provide non-empty options A, B, C, and D exactly once." });
        }
        var correctLetter = request.CorrectLetter.Trim().ToUpperInvariant();
        if (!options.Any(option => option.Letter == correctLetter))
        {
            return BadRequest(new { error = "CorrectLetter must be A, B, C, or D." });
        }

        question.Prompt = request.Prompt.Trim();
        question.Hint = request.Hint.Trim();
        foreach (var option in question.Options)
        {
            var updated = options.Single(item => item.Letter == option.Letter);
            option.Text = updated.Text;
            option.IsCorrect = option.Letter == correctLetter;
        }
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:int}/passages/{passageId:int}")]
    public async Task<IActionResult> UpdatePassage(
        int id,
        int passageId,
        [FromBody] UpdatePassageRequest request,
        CancellationToken cancellationToken)
    {
        var passage = await database.Passages
            .SingleOrDefaultAsync(item => item.Id == passageId && item.ExamId == id, cancellationToken);
        if (passage is null)
        {
            return NotFound();
        }
        var isPublished = await database.Exams.AnyAsync(exam => exam.Id == id && exam.PublishedAt != null, cancellationToken);
        if (isPublished)
        {
            return Conflict(new { error = "Published exams cannot be edited." });
        }
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { error = "Passage content is required." });
        }

        passage.Content = request.Content.Trim();
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:int}/stats")]
    public async Task<ActionResult<ExamStatsDto>> Stats(int id, CancellationToken cancellationToken)
    {
        var exam = await database.Exams.AsNoTracking()
            .Include(item => item.Questions).ThenInclude(question => question.Options)
            .Include(item => item.Attempts).ThenInclude(attempt => attempt.Answers)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        var submitted = exam.Attempts.Where(attempt => attempt.SubmittedAt is not null).ToArray();
        var correctByQuestion = exam.Questions.ToDictionary(
            question => question.Id,
            question => question.Options.Single(option => option.IsCorrect).Letter);
        var scores = submitted.ToDictionary(
            attempt => attempt.Id,
            attempt => attempt.Answers.Count(answer =>
                correctByQuestion.TryGetValue(answer.QuestionId, out var correct)
                && answer.SelectedLetter == correct));
        var average = submitted.Length == 0
            ? 0m
            : decimal.Round(scores.Values.Average(score => (decimal)score / exam.Questions.Count * 100), 2);

        var difficulties = exam.Questions.Select(question =>
        {
            var correct = correctByQuestion[question.Id];
            var incorrect = submitted.Count(attempt =>
                !attempt.Answers.Any(answer =>
                    answer.QuestionId == question.Id && answer.SelectedLetter == correct));
            return new QuestionDifficultyDto(
                question.Id,
                question.Part,
                question.Prompt,
                incorrect,
                submitted.Length == 0 ? 0 : decimal.Round((decimal)incorrect / submitted.Length * 100, 2));
        }).OrderByDescending(item => item.IncorrectPercent)
            .ThenByDescending(item => item.IncorrectCount)
            .Take(10)
            .ToArray();

        var percentages = scores.Values
            .Select(score => exam.Questions.Count == 0 ? 0m : (decimal)score / exam.Questions.Count * 100)
            .ToArray();
        return Ok(new ExamStatsDto(
            submitted.Length,
            average,
            percentages.Count(score => score > average),
            percentages.Count(score => score < average),
            percentages.Count(score => score == average),
            difficulties));
    }

    internal async Task<Exam?> LoadExam(int id, CancellationToken cancellationToken) =>
        await database.Exams
            .Include(exam => exam.Passages)
            .Include(exam => exam.Questions).ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(exam => exam.Id == id, cancellationToken);
}
