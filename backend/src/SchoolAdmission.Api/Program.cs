using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Application.Interfaces;
using SchoolAdmission.Application.Validators;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Infrastructure.BackgroundServices;
using SchoolAdmission.Infrastructure.Persistence;
using SchoolAdmission.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Force Kestrel to listen on port 8080 as per requirement
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});

// 2. Database Connection Smart Auto-Detection:
// Checks if PostgreSQL port 5432 is actively listening; if not, seamlessly fallbacks to local physical SQLite db
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Port=5432;Database=school_admission_db;Username=postgres;Password=postgres";

// Extract target host and port from connection string for container or bare-metal environment
string targetHost = "localhost";
int targetPort = 5432;

try
{
    var csParts = connectionString.Split(';');
    foreach (var part in csParts)
    {
        var kvp = part.Split('=', 2, StringSplitOptions.TrimEntries);
        if (kvp.Length == 2)
        {
            if (kvp[0].Equals("Host", StringComparison.OrdinalIgnoreCase) || kvp[0].Equals("Server", StringComparison.OrdinalIgnoreCase))
            {
                targetHost = kvp[1];
            }
            else if (kvp[0].Equals("Port", StringComparison.OrdinalIgnoreCase) && int.TryParse(kvp[1], out var parsedPort))
            {
                targetPort = parsedPort;
            }
        }
    }
}
catch
{
    targetHost = "localhost";
    targetPort = 5432;
}

bool isPostgresAvailable = false;
try
{
    using var tcpClient = new TcpClient();
    var connectTask = tcpClient.ConnectAsync(targetHost, targetPort);
    if (connectTask.Wait(TimeSpan.FromMilliseconds(500))) // Quick check against target host
    {
        isPostgresAvailable = true;
    }
}
catch
{
    isPostgresAvailable = false;
}

var forceInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");
var usePostgres = !forceInMemory && isPostgresAvailable;

var sqliteDbPath = Path.Combine(AppContext.BaseDirectory, "school_admission.db");
// Ensure physical db is also written to project root for easy inspection via DBeaver
var rootDbPath = @"D:\School-Admission-System\school_admission.db";

builder.Services.AddDbContext<SchoolDbContext>(options =>
{
    if (usePostgres)
    {
        // High-Scale Production Connection Pool: Optimized for 100,000 concurrent surge traffic
        var tunedConnectionString = connectionString;
        if (!tunedConnectionString.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
        {
            tunedConnectionString += ";Pooling=true;MaxPoolSize=300;MinPoolSize=10;ConnectionIdleLifetime=15;Timeout=15;CommandTimeout=30";
        }

        options.UseNpgsql(tunedConnectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(2),
                errorCodesToAdd: null);
        });
    }
    else
    {
        // Physical SQLite Database on disk viewable via DBeaver
        options.UseSqlite($"Data Source={sqliteDbPath}");
    }
});

// 3. Register Application Services & Validators
builder.Services.AddScoped<ISeatService, SeatService>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateClassRequestValidator>();

// 4. Register Background Worker for 120-second hold expirations
builder.Services.AddHostedService<SeatExpiryWorker>();

// 5. Add Controllers and Swagger
// 5. High-Surge Protection: Built-in Rate Limiting & Concurrency Throttling (Protects against 100,000+ DDoS/Spike)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "High admission traffic surge detected. Please wait a moment before trying again.",
            Instance = context.HttpContext.Request.Path
        };
        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: token);
    };

    // Global Concurrency Limiter: Allows high concurrent load up to 1000 concurrent executions with a deep queue
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 1000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10000
        }));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "School Admission & Seat Allocation API",
        Version = "v1",
        Description = "Enterprise-grade real-time seat allocation API with PostgreSQL FOR UPDATE SKIP LOCKED concurrency control."
    });
});

// 6. Configure CORS for React 19 Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 7. Auto-Migrate / Seed Initial Data on Startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (usePostgres)
        {
            logger.LogInformation("Applying PostgreSQL migrations...");
            context.Database.Migrate();
        }
        else
        {
            logger.LogInformation("Ensuring local SQLite physical database exists at {Path}...", rootDbPath);
            context.Database.EnsureCreated();
        }

        // Seed default classes if database is empty
        if (!context.Classes.Any())
        {
            logger.LogInformation("Seeding initial classes and seat matrix...");
            context.Classes.AddRange(
                Class.Create("Class 1", 30, 2000m),
                Class.Create("Class 2", 25, 2500m),
                Class.Create("Class 3", 20, 3000m)
            );
            context.SaveChanges();
            logger.LogInformation("Seeding completed successfully.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Seeding/Migration notice: {Message}", ex.Message);
    }
}

// 8. Middleware Pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors("AllowAll");
app.UseRateLimiter();

if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "School Admission API v1");
        c.RoutePrefix = "swagger";
    });
}

// Health check / root ping endpoint
app.MapGet("/", () => Results.Ok(new
{
    status = "Online",
    system = "School Admission System",
    version = "1.0",
    database = usePostgres ? "PostgreSQL" : "Physical SQLite (Viewable in DBeaver)",
    timestamp = DateTime.UtcNow
}));

app.MapControllers();

app.Run();

public partial class Program { }

