using Domain.IServices;
using Infrastructure.Services;
using Infrastructure.AppDbContext;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Settings;
using Inserter.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();



#region  dependencyInjection

builder.Services.AddScoped<GeminiExtractor>();
builder.Services.AddScoped<OpenRouterExtractor>();
builder.Services.AddHttpClient<ILlmExtractionService, LlmExtractionService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseAddress"] ?? "http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(30);
});
var connectionString = builder.Configuration.GetConnectionString("AppDbContext");
builder.Services.AddDbContext<MasterDbContext>(options => options.UseSqlServer(connectionString));
#endregion


#region messagesFile  
MessagesFilePathSettings messageFilePath = new();
string? path = builder.Configuration.GetValue<string>("MessagesFilePath");
messageFilePath.FilePath = path!;
builder.Services.AddSingleton(messageFilePath);
#endregion

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
