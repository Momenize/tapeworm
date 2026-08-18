using Domain.DTOs;
using Domain.IServices;
using Infrastructure.AppDbContext;
using Infrastructure.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inserter.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Inserter.Controllers;

[ApiController]
[Route("api/products/")]
public class ProductsController(ILlmExtractionService llm, MessagesFilePathSettings messagesFilePathSettings) : ControllerBase
{
    [HttpPost("gemini")]
    public async Task<IActionResult> ExtractWithGemini(GeminiExtractor geminiExtractor, CancellationToken cancellationToken)
    {
        var filePath = messagesFilePathSettings.FilePath;

        if (!System.IO.File.Exists(filePath))
            return NotFound($"File not found: {filePath}");

        var json = await System.IO.File.ReadAllTextAsync(
            filePath,
            cancellationToken);

        var channels =
            JsonSerializer.Deserialize<List<MessageFileChannelDTO>>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (channels is null)
            return BadRequest("Invalid messages JSON file.");

        var extractedChannels = new List<ExtractedChannelDTO>();

        foreach (var channel in channels)
        {
            if (channel.Messages.Count == 0)
                continue;

            var extracted = await geminiExtractor.Extract(
                channel.Messages,
                cancellationToken);

            // Deterministic metadata � don't ask Gemini for it.
            extracted.ChannelId = channel.ChannelId;
            extracted.Description = channel.Description;
            foreach(var product in extracted.Products)
            {
                if(product.CategoryName is null)
                {
                    product.CategoryName = "General";
                }
            }
            extractedChannels.Add(extracted);
        }

        await geminiExtractor.InsertToDatabase(extractedChannels);
        
        return Ok(extractedChannels);
    }

    [HttpGet("openrouter")]
    [ProducesResponseType(typeof(OkObjectResult), 200)]
    [ProducesResponseType(typeof(BadRequestObjectResult), 400)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> ExtractWithOpenRouter(OpenRouterExtractor extractor, CancellationToken cancellationToken)
    {
        var filePath = messagesFilePathSettings.FilePath;

        if (!System.IO.File.Exists(filePath))
            return NotFound($"File not found: {filePath}");

        var json = await System.IO.File.ReadAllTextAsync(
            filePath,
            cancellationToken);

        var channels =
            JsonSerializer.Deserialize<List<MessageFileChannelDTO>>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        if (channels is null)
            return BadRequest("Invalid messages JSON file.");

        var extractedChannels = new List<ExtractedChannelDTO>();
        
        foreach (var channel in channels)
        {
            if (channel.Messages.Count == 0)
                continue;

            var extracted = await extractor.Extract(
                channel.Messages,
                cancellationToken);

            // Deterministic metadata � don't ask Gemini for it.
            extracted.ChannelId = channel.ChannelId;
            extracted.Description = channel.Description;
            foreach(var product in extracted.Products)
            {
                if(product.CategoryName is null)
                {
                    product.CategoryName = "General";
                }
            }
            extractedChannels.Add(extracted);
        }

        await extractor.InsertToDatabase(extractedChannels);
        return Ok("Inserted into Database");

    }
    
    [HttpGet("InsertWithOllama")]
    public async Task<IActionResult> Insert(CancellationToken cancellationToken)
    {
        var path = messagesFilePathSettings.FilePath;
        var text = await System.IO.File.ReadAllTextAsync(path, cancellationToken);
        var channels = JsonSerializer.Deserialize<List<ChannelInputDTO>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (channels is null)
            return BadRequest("Could not parse input JSON");

        foreach (var ch in channels)
        {
            await llm.ProcessChannelAsync(ch, cancellationToken);
        }

        return Ok();
    }

    [HttpGet("Get/{channelExternalId}")]
    public async Task<IActionResult> Get([FromRoute] string channelExternalId, [FromServices] MasterDbContext db, CancellationToken cancellationToken)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(x => x.ExternalId == channelExternalId, cancellationToken: cancellationToken);
        if (channel is null) return NotFound();

        var products = await db.Products.Where(p => p.ChannelId == channel.Id).Select(p => new {
            p.Id, p.Name, p.Brand, p.Price, p.Description, p.MessageUrl
        }).ToListAsync(cancellationToken);

        return Ok(new { channel = new { channel.Id, channel.ExternalId, channel.Description, channel.City, channel.PhoneNumbers }, products });
    }
}




public sealed class MessageFileChannelDTO
{
    [JsonPropertyName("channel_id")]
    public string ChannelId { get; set; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("description")]
    public string Description { get; set; } = null!;

    [JsonPropertyName("messages")]
    public List<MessageFileMessageDTO> Messages { get; set; } = [];
}

public sealed class MessageFileMessageDTO
{
    [JsonPropertyName("message_url")]
    public string MessageUrl { get; set; } = null!;

    [JsonPropertyName("datetime_utc")]
    public string DatetimeUtc { get; set; } = null!;

    [JsonPropertyName("text")]
    public string Text { get; set; } = null!;

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }
}