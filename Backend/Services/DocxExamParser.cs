using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Backend.Models;

namespace Backend.Services;

public sealed record ImportError(int Line, string Message);

public sealed class ExamDraft
{
    public string Title { get; init; } = "";
    public int DurationMinutes { get; init; }
    public List<PassageDraft> Passages { get; init; } = [];
    public List<QuestionDraft> Questions { get; init; } = [];
}

public sealed class PassageDraft
{
    public string Key { get; init; } = "";
    public int Part { get; init; }
    public string Content { get; init; } = "";
    public bool ShuffleQuestions { get; init; }
}

public sealed class QuestionDraft
{
    public int Part { get; init; }
    public string? PassageKey { get; init; }
    public string Prompt { get; init; } = "";
    public string Hint { get; init; } = "";
    public string CorrectLetter { get; init; } = "";
    public bool ShuffleQuestion { get; init; }
    public bool ShuffleAnswers { get; init; }
    public List<(string Letter, string Text)> Options { get; init; } = [];
}

public sealed class DocxExamParser
{
    private static readonly Regex OptionLine = new(@"^([A-D])\s*[.)]\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex Attribute = new(
        """([A-Za-z]+)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s\]]+))""",
        RegexOptions.Compiled);

    public async Task<IReadOnlyList<string>> ReadParagraphsAsync(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var document = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("DOCX is missing word/document.xml.");

        if (document.Length > 10 * 1024 * 1024)
        {
            throw new InvalidDataException("The Word document content is too large.");
        }

        await using var documentStream = document.Open();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(documentStream, settings);
        var xml = XDocument.Load(reader);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return xml.Descendants(word + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(word + "t").Select(text => text.Value)))
            .ToArray();
    }

    public async Task<(ExamDraft? Draft, IReadOnlyList<ImportError> Errors)> ParseAsync(Stream stream)
    {
        var lines = (await ReadParagraphsAsync(stream))
            .SelectMany(paragraph => paragraph.Replace("\r", "").Split('\n'))
            .Select(line => line.Trim())
            .ToArray();
        var errors = new List<ImportError>();
        var questions = new List<QuestionDraft>();
        var passages = new List<PassageDraft>();
        var title = "";
        var duration = 75;
        var durationSpecified = false;
        var inExam = false;
        var examClosed = false;
        PassageBuilder? passage = null;
        QuestionBuilder? question = null;

        void Error(int line, string message) => errors.Add(new ImportError(line, message));

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            if (!inExam)
            {
                if (line == "[EXAM]")
                {
                    inExam = true;
                }
                else
                {
                    Error(lineNumber, "Expected [EXAM] before exam content.");
                }
                continue;
            }

            if (question is not null)
            {
                if (line == "[/QUESTION]")
                {
                    FinishQuestion(question, lineNumber, Error, questions);
                    question = null;
                    continue;
                }

                if (line.StartsWith("Prompt:", StringComparison.OrdinalIgnoreCase))
                {
                    question.Prompt.AppendLine(line["Prompt:".Length..].Trim());
                    continue;
                }

                if (line.StartsWith("Hint:", StringComparison.OrdinalIgnoreCase))
                {
                    if (question.Hint.Length > 0)
                    {
                        Error(lineNumber, "Hint may only be specified once per question.");
                    }
                    question.Hint = line["Hint:".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("Correct:", StringComparison.OrdinalIgnoreCase))
                {
                    if (question.CorrectLetter.Length > 0)
                    {
                        Error(lineNumber, "Correct answer may only be specified once per question.");
                    }
                    question.CorrectLetter = line["Correct:".Length..].Trim().ToUpperInvariant();
                    continue;
                }

                var optionMatch = OptionLine.Match(line);
                if (optionMatch.Success)
                {
                    var letter = optionMatch.Groups[1].Value;
                    if (question.Options.Any(option => option.Letter == letter))
                    {
                        Error(lineNumber, $"Option {letter} is duplicated.");
                    }
                    else
                    {
                        question.Options.Add((letter, optionMatch.Groups[2].Value.Trim()));
                    }
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    Error(lineNumber, "Expected [/QUESTION] before the next tag.");
                    continue;
                }

                question.Prompt.AppendLine(line);
                continue;
            }

            if (line == "[/EXAM]")
            {
                if (passage is not null)
                {
                    Error(lineNumber, "Close the current passage with [/PASSAGE] before [/EXAM].");
                }
                examClosed = true;
                inExam = false;
                continue;
            }

            if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            {
                if (title.Length > 0)
                {
                    Error(lineNumber, "Title may only be specified once.");
                }
                title = line["Title:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("DurationMinutes:", StringComparison.OrdinalIgnoreCase))
            {
                if (durationSpecified)
                {
                    Error(lineNumber, "DurationMinutes may only be specified once.");
                    continue;
                }
                durationSpecified = true;
                if (!int.TryParse(line["DurationMinutes:".Length..].Trim(), out duration))
                {
                    Error(lineNumber, "DurationMinutes must be a whole number.");
                }
                continue;
            }

            if (line.StartsWith("[PASSAGE", StringComparison.Ordinal))
            {
                if (passage is not null)
                {
                    Error(lineNumber, "Passages cannot be nested; close the current passage first.");
                    continue;
                }
                passage = StartPassage(line, lineNumber, Error);
                continue;
            }

            if (line == "[/PASSAGE]")
            {
                if (passage is null)
                {
                    Error(lineNumber, "[/PASSAGE] has no matching [PASSAGE].");
                }
                else
                {
                    if (passage.Content.Length == 0)
                    {
                        Error(passage.Line, $"Passage '{passage.Key}' must contain reading text.");
                    }
                    if (passage.Questions.Count == 0)
                    {
                        Error(passage.Line, $"Passage '{passage.Key}' must contain at least one question.");
                    }
                    passages.Add(new PassageDraft
                    {
                        Key = passage.Key,
                        Part = passage.Part,
                        Content = passage.Content.ToString().Trim(),
                        ShuffleQuestions = passage.ShuffleQuestions
                    });
                    questions.AddRange(passage.Questions);
                    passage = null;
                }
                continue;
            }

            if (line.StartsWith("[QUESTION", StringComparison.Ordinal))
            {
                question = StartQuestion(line, lineNumber, passage, Error);
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                Error(lineNumber, $"Unknown tag '{line}'.");
                continue;
            }

            if (passage is not null)
            {
                passage.Content.AppendLine(line);
            }
            else
            {
                Error(lineNumber, "Unexpected text outside a passage or question.");
            }
        }

        if (question is not null)
        {
            Error(question.Line, "Question is missing [/QUESTION].");
        }
        if (passage is not null)
        {
            Error(passage.Line, $"Passage '{passage.Key}' is missing [/PASSAGE].");
        }
        if (!examClosed)
        {
            Error(Math.Max(1, lines.Length), "Exam is missing [/EXAM].");
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            Error(1, "Title is required.");
        }
        if (duration != 75)
        {
            Error(1, "DurationMinutes must be 75 for a TOEIC Reading exam.");
        }
        if (questions.Count != 100)
        {
            Error(1, $"A TOEIC Reading exam must contain exactly 100 questions; found {questions.Count}.");
        }
        foreach (var (part, expectedCount) in new[] { (5, 30), (6, 16), (7, 54) })
        {
            var actualCount = questions.Count(item => item.Part == part);
            if (actualCount != expectedCount)
            {
                Error(1, $"Part {part} must contain exactly {expectedCount} questions; found {actualCount}.");
            }
        }

        var passageKeys = passages.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in questions)
        {
            if (item.PassageKey is not null && !passageKeys.Contains(item.PassageKey))
            {
                Error(1, $"Question refers to missing passage '{item.PassageKey}'.");
            }
        }
        foreach (var duplicate in passages.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            Error(1, $"Passage id '{duplicate.Key}' is duplicated.");
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new ExamDraft
        {
            Title = title,
            DurationMinutes = duration,
            Passages = passages,
            Questions = questions
        }, errors);
    }

    private static PassageBuilder? StartPassage(string line, int lineNumber, Action<int, string> error)
    {
        if (!line.EndsWith(']'))
        {
            error(lineNumber, "Passage tag is missing its closing ']' character.");
            return null;
        }
        var values = ParseAttributes(line, lineNumber, error, "id", "part", "shuffleQuestions");
        if (!values.TryGetValue("id", out var key) || string.IsNullOrWhiteSpace(key))
        {
            error(lineNumber, "[PASSAGE] requires a unique id.");
        }
        if (!values.TryGetValue("part", out var partText) || !int.TryParse(partText, out var part) || part is not (6 or 7))
        {
            error(lineNumber, "Passage part must be 6 or 7.");
        }
        return new PassageBuilder
        {
            Key = key ?? "",
            Part = int.TryParse(partText, out var validPart) ? validPart : 0,
            ShuffleQuestions = ReadBoolean(values, "shuffleQuestions", false, lineNumber, error),
            Line = lineNumber
        };
    }

    private static QuestionBuilder StartQuestion(
        string line,
        int lineNumber,
        PassageBuilder? passage,
        Action<int, string> error)
    {
        if (!line.EndsWith(']'))
        {
            error(lineNumber, "Question tag is missing its closing ']' character.");
        }
        var values = ParseAttributes(line, lineNumber, error, "part", "shuffleQuestions", "shuffleAnswers");
        if (!values.TryGetValue("part", out var partText) || !int.TryParse(partText, out var part) || part is < 5 or > 7)
        {
            error(lineNumber, "Question part must be 5, 6, or 7.");
        }
        var actualPart = int.TryParse(partText, out var validPart) ? validPart : 0;
        if (actualPart == 5 && passage is not null)
        {
            error(lineNumber, "Part 5 questions must not be inside a passage.");
        }
        if (actualPart is 6 or 7 && (passage is null || passage.Part != actualPart))
        {
            error(lineNumber, $"Part {actualPart} questions must be inside a matching [PASSAGE] block.");
        }

        return new QuestionBuilder
        {
            Part = actualPart,
            PassageKey = actualPart == 5 ? null : passage?.Key,
            ShuffleQuestion = ReadBoolean(values, "shuffleQuestions", false, lineNumber, error),
            ShuffleAnswers = ReadBoolean(values, "shuffleAnswers", false, lineNumber, error),
            Line = lineNumber,
            Passage = passage
        };
    }

    private static void FinishQuestion(
        QuestionBuilder builder,
        int lineNumber,
        Action<int, string> error,
        List<QuestionDraft> independentQuestions)
    {
        var prompt = builder.Prompt.ToString().Trim();
        if (prompt.Length == 0)
        {
            error(builder.Line, "Question prompt is required.");
        }
        if (builder.Options.Count != 4 || !builder.Options.Select(option => option.Letter).Order().SequenceEqual(["A", "B", "C", "D"]))
        {
            error(builder.Line, "Each question must have exactly one non-empty option A, B, C, and D.");
        }
        if (!new[] { "A", "B", "C", "D" }.Contains(builder.CorrectLetter))
        {
            error(builder.Line, "Correct must name one of the options A, B, C, or D.");
        }
        if (string.IsNullOrWhiteSpace(builder.Hint))
        {
            error(builder.Line, "Hint is required.");
        }

        var draft = new QuestionDraft
        {
            Part = builder.Part,
            PassageKey = builder.PassageKey,
            Prompt = prompt,
            Hint = builder.Hint,
            CorrectLetter = builder.CorrectLetter,
            ShuffleQuestion = builder.ShuffleQuestion,
            ShuffleAnswers = builder.ShuffleAnswers,
            Options = builder.Options
        };
        if (builder.Passage is null)
        {
            independentQuestions.Add(draft);
        }
        else
        {
            builder.Passage.Questions.Add(draft);
        }
    }

    private static Dictionary<string, string> ParseAttributes(
        string tag,
        int line,
        Action<int, string> error,
        params string[] allowedNames)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Attribute.Matches(tag))
        {
            var name = match.Groups[1].Value;
            var value = match.Groups[2].Success ? match.Groups[2].Value
                : match.Groups[3].Success ? match.Groups[3].Value
                : match.Groups[4].Value;
            if (!values.TryAdd(name, value))
            {
                error(line, $"Tag attribute '{name}' is repeated.");
            }
            else if (!allowedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                error(line, $"Unknown tag attribute '{name}'.");
            }
        }
        return values;
    }

    private static bool ReadBoolean(
        IReadOnlyDictionary<string, string> values,
        string name,
        bool defaultValue,
        int line,
        Action<int, string> error)
    {
        if (!values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }
        if (bool.TryParse(value, out var result))
        {
            return result;
        }
        error(line, $"{name} must be true or false.");
        return defaultValue;
    }

    private sealed class PassageBuilder
    {
        public string Key { get; init; } = "";
        public int Part { get; init; }
        public bool ShuffleQuestions { get; init; }
        public int Line { get; init; }
        public StringBuilder Content { get; } = new();
        public List<QuestionDraft> Questions { get; } = [];
    }

    private sealed class QuestionBuilder
    {
        public int Part { get; init; }
        public string? PassageKey { get; init; }
        public bool ShuffleQuestion { get; init; }
        public bool ShuffleAnswers { get; init; }
        public int Line { get; init; }
        public PassageBuilder? Passage { get; init; }
        public StringBuilder Prompt { get; } = new();
        public List<(string Letter, string Text)> Options { get; } = [];
        public string CorrectLetter { get; set; } = "";
        public string Hint { get; set; } = "";
    }
}
