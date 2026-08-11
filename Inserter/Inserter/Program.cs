using Domain.IServices;
using Infrastructure.Services;
using Infrastructure.AppDbContext;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Settings;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

var connectionString = builder.Configuration.GetConnectionString("AppDbContext");

builder.Services.AddDbContext<MasterDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddHttpClient<ILlmExtractionService, LlmExtractionService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseAddress"] ?? "http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(30);
});


builder.Services.AddScoped<GeminiExtractor>();

MessagesFilePathSettings messageFilePath = new();
string? path = builder.Configuration.GetValue<string>("MessagesFilePath");
messageFilePath.FilePath = path!;
builder.Services.AddSingleton(messageFilePath);

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
