using Domain.DTOs;
using Domain.IServices;
using Infrastructure.AppDbContext;
using Infrastructure.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inserter.Controllers;

[ApiController]
[Route("[controller]")]
public class ProductsController(ILlmExtractionService llm) : ControllerBase
{
    [HttpGet("Insert")]
    public async Task<IActionResult> Insert(CancellationToken cancellationToken, MessagesFilePathSettings messagesFilePath)
    {
        var path = messagesFilePath.FilePath;
        var text = await System.IO.File.ReadAllTextAsync(path, cancellationToken);
        var channels = System.Text.Json.JsonSerializer.Deserialize<List<ChannelInputDTO>>(text,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (channels is null)
            return BadRequest("Could not parse input JSON");

        foreach (var ch in channels)
        {
            await llm.ProcessChannelAsync(ch, cancellationToken);
        }

        return Ok();
    }

    [HttpGet("Get/{channelExternalId}")]
    public async Task<IActionResult> Get(string channelExternalId, [FromServices] MasterDbContext db, CancellationToken cancellationToken)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(x => x.ExternalId == channelExternalId, cancellationToken: cancellationToken);
        if (channel is null) return NotFound();

        var products = await db.Products.Where(p => p.ChannelId == channel.Id).Select(p => new {
            p.Id, p.Name, p.Brand, p.Price, p.Description, p.MessageUrl
        }).ToListAsync(cancellationToken);

        return Ok(new { channel = new { channel.Id, channel.ExternalId, channel.Description, channel.City, channel.PhoneNumbers }, products });
    }
}
