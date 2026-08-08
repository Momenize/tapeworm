using Application.Assemblies;
using Infrastructure;
using Infrastructure.Service;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddInfrastructure(builder.Configuration);
var deepSeekSettings = new DeepSeekSettings();
builder.Configuration.Bind("DeepSeek", deepSeekSettings);
builder.Services.AddSingleton(deepSeekSettings);
var messagesPath = builder.Configuration.GetValue<string>("MessagesPath");
builder.Services.AddSingleton(new MessagesFilePath(messagesPath!));
builder.Services.AddMediatR(r => r.RegisterServicesFromAssemblyContaining<MediatorDI>());
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
