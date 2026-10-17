using Backend.Contracts;
using Backend.Models;

namespace Backend.Services;

public static class ExamDtoMapper
{
    public static ExamSummaryDto ToSummary(Exam exam) => new(
        exam.Id,
        exam.Title,
        exam.DurationMinutes,
        exam.Questions.Count,
        exam.PublishedAt is not null);

    public static ExamDetailsDto ToDetails(
        Exam exam,
        IReadOnlyList<Question> orderedQuestions,
        bool includeAnswers,
        IReadOnlyDictionary<int, string[]>? optionOrders = null,
        IReadOnlyDictionary<int, string?>? selectedAnswers = null)
    {
        var questions = orderedQuestions.Select(question =>
        {
            IEnumerable<AnswerOption> options = question.Options.OrderBy(option => option.Letter);
            if (optionOrders is not null && optionOrders.TryGetValue(question.Id, out var order))
            {
                var positions = order.Select((letter, index) => (letter, index))
                    .ToDictionary(item => item.letter, item => item.index, StringComparer.Ordinal);
                options = options.OrderBy(option => positions.GetValueOrDefault(option.Letter, int.MaxValue));
            }

            return new QuestionDto(
                question.Id,
                question.Part,
                question.PassageId,
                question.Passage?.Content,
                question.Prompt,
                includeAnswers ? question.Hint : null,
                options.Select(option => new OptionDto(option.Letter, option.Text)).ToArray(),
                includeAnswers ? question.Options.Single(option => option.IsCorrect).Letter : null,
                selectedAnswers is not null && selectedAnswers.TryGetValue(question.Id, out var selected) ? selected : null);
        }).ToArray();

        return new ExamDetailsDto(exam.Id, exam.Title, exam.DurationMinutes, questions.Length, questions);
    }
}
