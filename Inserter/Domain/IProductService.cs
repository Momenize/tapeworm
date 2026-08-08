using Domain.DTOs;

namespace Domain;

public interface IProductService
{
    Task AddProducts();
    Task<List<ProductDTO>> GetChannelProducts(int channelId);
}
