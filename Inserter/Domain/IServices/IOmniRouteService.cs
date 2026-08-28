using Domain.DTOs;

namespace Domain.IServices;

public interface IOmniRouteService
{
    Task<ExtractedChannelDTO> Extract(List<MessageFileMessageDTO> messages,
        CancellationToken cancellationToken = default);

    Task InsertToDatabase(List<ExtractedChannelDTO> channels);
}