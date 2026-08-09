using Domain.DTOs;

namespace Domain.IServices;

public interface ILlmExtractionService
{
    Task<ExtractedChannelDTO> ExtractChannelAsync(
        ChannelInputDTO channel,
        CancellationToken cancellationToken = default);

    Task ProcessChannelAsync(
        ChannelInputDTO input,
        CancellationToken cancellationToken = default);
}