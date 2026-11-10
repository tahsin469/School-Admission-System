using System.Net.Sockets;
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
// Checks if PostgreSQL port 5432 is actively listening; if not, seamlessly fallbacks to InMemory
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Port=5432;Database=school_admission_db;Username=postgres;Password=postgres";

bool isPostgresAvailable = false;
try
{
    using var tcpClient = new TcpClient();
    var connectTask = tcpClient.ConnectAsync("localhost", 5432);
    if (connectTask.Wait(TimeSpan.FromMilliseconds(400))) // Quick check
    {
        isPostgresAvailable = true;
    }
}
catch
{
    isPostgresAvailable = false;
}

// In development or when local postgres is not running, fallback to InMemory
var forceInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");
var useInMemory = forceInMemory || !isPostgresAvailable;

builder.Services.AddDbContext<SchoolDbContext>(options =>
{
    if (useInMemory)
    {
        options.UseInMemoryDatabase("SchoolAdmissionDb");
    }
    else
    {
        options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
        });
    }
});

// 3. Register Application Services & Validators
builder.Services.AddScoped<ISeatService, SeatService>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateClassRequestValidator>();

// 4. Register Background Worker for 120-second hold expirations
builder.Services.AddHostedService<SeatExpiryWorker>();

// 5. Add Controllers and Swagger
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
        if (!useInMemory)
        {
            logger.LogInformation("Applying PostgreSQL migrations...");
            context.Database.Migrate();
        }
        else
        {
            logger.LogInformation("Using In-Memory Database for local development.");
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
    database = useInMemory ? "InMemory" : "PostgreSQL",
    timestamp = DateTime.UtcNow
}));

app.MapControllers();

app.Run();

public partial class Program { }
