using Backend.Data;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Services.AddDbContext<ExamDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton<DocxExamParser>();
builder.Services.AddControllers();

var frontendPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Frontend"));
if (!Directory.Exists(frontendPath))
{
    throw new DirectoryNotFoundException($"Frontend assets were not found at '{frontendPath}'.");
}

var frontendProvider = new PhysicalFileProvider(frontendPath);
var app = builder.Build();

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = frontendProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = frontendProvider });
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<ExamDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.Run();
