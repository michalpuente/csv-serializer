using CsvSerializer.Api.Data;
using CsvSerializer.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connStr);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(dataSource));

builder.Services.AddSingleton<ImportService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var payload = JsonSerializer.Serialize(new
        {
            error = "Unexpected server error.",
            detail = error?.Message
        });

        await context.Response.WriteAsync(payload);
    });
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Served behind docker-gateway at /csv-serializer; requests without the prefix still work
app.UsePathBase("/csv-serializer");
app.UseRouting();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.MapFallbackToFile("index.html");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
