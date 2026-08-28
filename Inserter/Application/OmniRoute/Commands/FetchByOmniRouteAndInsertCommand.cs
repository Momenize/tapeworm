using System.Text.Json;
using Application.BaseClasses;
using Domain.DTOs;
using Domain.IServices;
using Infrastructure.Settings;
using MediatR;

namespace Application.OmniRoute.Commands;

public class FetchByOmniRouteAndInsertCommand : BaseRequest<SuccessfulResult>;



public class FetchByOmniRouteAndInsertHandler(IOmniRouteService omniRouteService,
    MessagesFileSettings fileSettings) : IRequestHandler<FetchByOmniRouteAndInsertCommand, SuccessfulResult>
{
    private readonly IOmniRouteService _omniRouteService = omniRouteService;
    public async Task<SuccessfulResult> Handle(FetchByOmniRouteAndInsertCommand request, CancellationToken cancellationToken)
    {
        var filePath = fileSettings.FilePath;

        if (!File.Exists(filePath))
            throw new FileNotFound(filePath);

        var json = await File.ReadAllTextAsync(
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
            throw new InvalidMessagesFile(filePath);

        var extractedChannels = new List<ExtractedChannelDTO>();
        
        foreach (var channel in channels)
        {
            
            var messagesCount = channel.Messages.Count;
            if (messagesCount == 0)
                continue;
            var batches = channel.Messages.Chunk(2).ToList();

            
            var extractedBatches = new List<ExtractedChannelDTO>();
            foreach (var batch in batches)
            {
                try
                {
                    var extractedBatch = await _omniRouteService.Extract(
                        [.. batch],
                        cancellationToken);
                    extractedBatches.Add(extractedBatch);
                }
                catch (Exception ex)
                {
                    // Log the error and continue with the next batch
                    Console.WriteLine($"Error extracting batch for channel {channel.ChannelId}: {ex.Message}");
                    break;
                }
            }

            var extracted = new ExtractedChannelDTO()
            {
                ChannelId = channel.ChannelId,
                Description = channel.Description,
                Products = [.. extractedBatches.SelectMany(b => b.Products)]
            };
            // Deterministic metadata � don't ask Gemini for it.
            
            foreach(var product in extracted.Products)
            {
                if(product.CategoryName is null)
                {
                    product.CategoryName = "General";
                }
            }
            extractedChannels.Add(extracted);
            
        }

        await _omniRouteService.InsertToDatabase(extractedChannels);
        return new SuccessfulResult();
    }
}
