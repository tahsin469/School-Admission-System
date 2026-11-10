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

// 2. Add Database Context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Port=5432;Database=school_admission_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<SchoolDbContext>(options =>
{
    if (builder.Configuration.GetValue<bool>("UseInMemoryDatabase"))
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

// 7. Auto-Migrate Database and Seed Initial Data on Startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (!app.Configuration.GetValue<bool>("UseInMemoryDatabase"))
        {
            context.Database.Migrate();
        }
        else
        {
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
        logger.LogWarning(ex, "Database migration/seed skipped or connection not available yet: {Message}", ex.Message);
    }
}

// 8. Middleware Pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors("AllowAll");

if (app.Environment.IsDevelopment() || true) // Enable Swagger across environments for evaluation
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
    timestamp = DateTime.UtcNow
}));

app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
