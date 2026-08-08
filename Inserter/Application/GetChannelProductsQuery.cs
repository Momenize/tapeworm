using Domain;
using Domain.DTOs;
using MediatR;

namespace Application;

public class GetChannelProductsQuery : IRequest<GetChannelProductsResult>
{
    public required int ChannelId { get; set; }
}
public class GetChannelProductsHandler(IProductService productService) : IRequestHandler<GetChannelProductsQuery, GetChannelProductsResult>
{
    public async Task<GetChannelProductsResult> Handle(GetChannelProductsQuery request, CancellationToken cancellationToken)
    {
        var products = await productService.GetChannelProducts(request.ChannelId);
        return new GetChannelProductsResult(products);
    }
}

public record GetChannelProductsResult(List<ProductDTO> Products);
