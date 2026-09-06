var allowedOrigins = new[]
{
    "http://localhost:5173",
    "http://127.0.0.1:5173",
};

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();