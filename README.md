<<<<<<< HEAD
# TOEIC Reading Exam Workspace

This starter implementation follows the midterm brief: an ASP.NET Core backend, SQL Server storage, a teacher workspace, and a responsive student test interface.

## Run locally

1. Install the .NET 8 SDK and SQL Server LocalDB, or edit `Backend/appsettings.json` to use a SQL Server connection string available on your machine.
2. From the workspace root, run:

   ```powershell
   dotnet run --project Backend
   ```

3. Open the local URL printed by ASP.NET Core. The frontend is served from `Frontend/`.

The application creates its initial database schema with `EnsureCreated`. For a deployed system, replace this prototype setup with EF Core migrations and configure the database connection outside source control.

## Word import format

The `.docx` file must contain one tag or field per Word paragraph. Each complete exam must follow the TOEIC Reading distribution (30 Part 5, 16 Part 6, and 54 Part 7 questions), with four choices per question, one correct choice, and a hint per question. Import reports paragraph numbers for malformed content and saves nothing unless the full document validates.

```text
[EXAM]
Title: TOEIC Reading practice
DurationMinutes: 75

[QUESTION part=5 shuffleQuestions=true shuffleAnswers=true]
Prompt: Complete the sentence: ...
A) choice one
B) choice two
C) choice three
D) choice four
Correct: B
Hint: Check the grammatical role required by the sentence.
[/QUESTION]

[PASSAGE id=P1 part=6 shuffleQuestions=true]
Reading passage text...
[QUESTION part=6 shuffleAnswers=true]
Prompt: Question about the passage...
A) choice one
B) choice two
C) choice three
D) choice four
Correct: A
Hint: Locate the relevant detail in the passage.
[/QUESTION]
[/PASSAGE]
[/EXAM]
```

Repeat the question blocks until the exam has 100 questions. Part 5 questions are independent and cannot be placed in a passage. Part 6 and Part 7 questions must be inside a matching passage. `shuffleQuestions` on a Part 5 question enables shuffling the Part 5 section; on a passage it shuffles that passage's questions. `shuffleAnswers` independently shuffles that question's options for each attempt. Both settings default to `false`.

## Included flows

- Teachers import, preview, and edit draft questions/passages, publish complete exams, and view participation, score, and hardest-question statistics.
- Students choose a published exam, enter a display name, take the timed test, have answers auto-saved, confirm unanswered questions before submission, and review the result.
- The API enforces the exam deadline and grades answers on the server.

This coursework prototype intentionally has no login; the learner name is recorded with each attempt for identification. Before production deployment, replace `EnsureCreated` with EF Core migrations and configure operational hosting and database security.
=======
# KiemTraGiuaKy
>>>>>>> 86d224c429a6f0c910caf63020bd033a9dd38ca2
