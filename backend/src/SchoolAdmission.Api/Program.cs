var builder = WebApplication.CreateBuilder(args);

// Kestrel web server listen on port 8080
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "School Admission & Seat Allocation API", Version = "v1" });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "School Admission API v1");
    c.RoutePrefix = "swagger";
});

// Root ping endpoint
app.MapGet("/", () => Results.Ok(new
{
    Status = "Online",
    System = "School Admission System",
    Version = "1.0",
    Timestamp = DateTime.UtcNow
}));

app.Run();
