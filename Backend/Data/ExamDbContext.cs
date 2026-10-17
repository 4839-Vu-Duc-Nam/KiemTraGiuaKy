using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public sealed class ExamDbContext(DbContextOptions<ExamDbContext> options) : DbContext(options)
{
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Passage> Passages => Set<Passage>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Exam>().Property(x => x.Title).HasMaxLength(200);
        modelBuilder.Entity<Passage>().Property(x => x.ExternalKey).HasMaxLength(50);
        modelBuilder.Entity<Passage>().HasIndex(x => new { x.ExamId, x.ExternalKey }).IsUnique();
        modelBuilder.Entity<Passage>()
            .HasMany(x => x.Questions)
            .WithOne(x => x.Passage)
            .HasForeignKey(x => x.PassageId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Exam>()
            .HasMany(x => x.Questions)
            .WithOne(x => x.Exam)
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Exam>()
            .HasMany(x => x.Passages)
            .WithOne(x => x.Exam)
            .HasForeignKey(x => x.ExamId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AttemptAnswer>()
            .HasIndex(x => new { x.AttemptId, x.QuestionId })
            .IsUnique();
    }
}
