using Domain.DTOs;

namespace Domain.IServices;

public interface IOmniRouteService
{
    Task<ChannelOutputDTO> Extract(ChannelInputDTO channel,
        CancellationToken cancellationToken = default);

    Task InsertToDatabase(List<ExtractedChannelDTO> channels);
    Task AddChannel(ExtractedChannelDTO channel);
    Task<bool> ChannelWithIdExists(string channelId);
}