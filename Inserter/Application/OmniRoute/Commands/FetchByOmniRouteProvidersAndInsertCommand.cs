using System.Globalization;
using System.Text.Json;
using Application.BaseClasses;
using Domain.DTOs;
using Domain.IServices;
using Infrastructure.Settings;
using MediatR;

namespace Application.OmniRoute.Commands;

public class FetchByOmniRouteProvidersAndInsertCommand : BaseRequest<Result>;



public class FetchByOmniRouteAndInsertHandler(IOmniRouteService omniRouteService,
    MessagesFileSettings fileSettings) : IRequestHandler<FetchByOmniRouteProvidersAndInsertCommand, Result>
{
    private readonly IOmniRouteService _omniRouteService = omniRouteService;
    public async Task<Result> Handle(FetchByOmniRouteProvidersAndInsertCommand request, CancellationToken cancellationToken)
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
                if (!await _omniRouteService.ChannelWithIdExists(channel.ChannelId))
                    await _omniRouteService.AddChannel(new ExtractedChannelDTO()
                    {
                        Description = channel.Description,
                        ChannelId = channel.ChannelId
                    });
                else return new NothingHappenedResult("Channel already exists. No products were added!");

            
            for (var i = 0; i < channel.Messages.Count; i++)
            {
                channel.Messages[i].Index = i;
            }

            var messagesList = channel.Messages
                .Select(x => new MessageInputDTO()
                {
                    Text = x.Text, 
                    Index = x.Index
                }).ToList();

            var channelDTO = new ChannelInputDTO()
            {
                Description = channel.Description,
                Messages = messagesList
            };
            
            
            var extractedChannel = await _omniRouteService
                .Extract(channelDTO, 
                         cancellationToken);
            var extracted = new ExtractedChannelDTO()
            {
                ChannelId = channel.ChannelId,
                Description = channel.Description,
                City = extractedChannel.City,
                PhoneNumbers = extractedChannel.PhoneNumbers,
                Products =
                [
                    .. extractedChannel.Products.Select(x => new ExtractedProductDTO()
                    {
                        Name = x.Name,
                        Description = x.Description,
                        MessageUrl = channel.Messages
                            .Where(y => y.Index == x.Index)
                            .Select(y => y.MessageUrl)
                            .First(),
                        Date = channel.Messages
                            .Where(y => y.Index == x.Index)
                            .Select(y => y.DatetimeUtc)
                            .Select(y => DateTime.ParseExact(y.Substring(0, 19), "yyyy-MM-ddTHH:mm:ss",
                                CultureInfo.InvariantCulture))
                            .First(),
                        CategoryName = x.CategoryName ?? "General",
                        Price = x.Price,
                        PurchaseMethod = x.PurchaseMethod,
                        Brand = x.Brand
                    })
                ]
            };
            
            extractedChannels.Add(extracted);
            
        }

        await _omniRouteService.InsertToDatabase(extractedChannels);
        return new SuccessfulResult(string.Empty);
    }
}
