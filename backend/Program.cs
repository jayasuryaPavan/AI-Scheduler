using Microsoft.EntityFrameworkCore;
using AiScheduler.Api.Agent;
using AiScheduler.Api.Data;
using AiScheduler.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Database (PostgreSQL via EF Core + pgvector) ────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql =>
        {
            npgsql.EnableRetryOnFailure(maxRetryCount: 5);
            npgsql.UseVector();
        }
    )
);

// ── Application Services ─────────────────────────────────────────────────
builder.Services.AddScoped<ISchedulingService, SchedulingService>();
builder.Services.AddScoped<IChatMemoryService, ChatMemoryService>();
builder.Services.AddScoped<IAgentMemoryService, AgentMemoryService>();
builder.Services.AddSingleton<IApprovalStore, ApprovalStore>();
builder.Services.AddSingleton<NotificationChannel>();
builder.Services.AddHostedService<ProactiveMonitoringService>();

// Agent is scoped (new per HTTP request) so it can use ISchedulingService
builder.Services.AddScoped<MasterProductionSchedulerAgent>();
builder.Services.AddScoped<FloorManagerAgent>();
builder.Services.AddScoped<ProcurementAgent>();

// ── Controllers + JSON ───────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // DateOnly serialization support
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
        opts.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// ── Swagger / OpenAPI ────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "AI Manufacturing Scheduler API",
        Version     = "v1",
        Description = "Autonomous Master Production Scheduler powered by Google Gemini via Vertex AI"
    });
});

// ── CORS (for Vue dev server on port 5173) ───────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ── Health checks ────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

// ── Cloud Run: honour PORT env var ──────────────────────────────────────
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Development middleware ───────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "AI Scheduler v1"));
    app.UseCors("DevCors");
}

// ── Static files (Vue SPA in wwwroot) ────────────────────────────────────
app.UseDefaultFiles();
app.UseStaticFiles();

// ── Routing ──────────────────────────────────────────────────────────────
app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health");

// ── SPA fallback: non-API routes → index.html ────────────────────────────
app.MapFallbackToFile("index.html");

// ── Auto-apply EF Core migrations on startup ────────────────────────────
// (init.sql handles the initial schema — migrations cover future changes)
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Database migration failed — the DB may already be initialized via init.sql.");
    }
}

app.Run();
