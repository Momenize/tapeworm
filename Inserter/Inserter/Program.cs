using Application.BaseClasses;
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
builder.Services.AddHttpClient<ILlmExtractionService, LlmExtractionService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseAddress"] ?? "http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddHttpClient<IOmniRouteService, OmniRouteService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OmniRoute:BaseUrl"]!);
    client.Timeout = TimeSpan.FromHours(2);
});
builder.Services.AddHttpClient<OpenRouterExtractor>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OpenRouter:Url"] ?? "https://api.openrouter.ai");
    client.Timeout = TimeSpan.FromHours(2);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
});
builder.Services.AddMediatR(r => r.RegisterServicesFromAssemblyContaining<MediatorDI>());
var connectionString = builder.Configuration.GetConnectionString("AppDbContext");
builder.Services.AddDbContext<MasterDbContext>(options => options.UseSqlServer(connectionString));
#endregion


#region messagesFile  
MessagesFileSettings messageFile = new();
string? path = builder.Configuration.GetValue<string>("MessagesFilePath");
messageFile.FilePath = path!;
builder.Services.AddSingleton(messageFile);
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
