using Domain;
using MediatR;

namespace Application;

public record AddProductsCommand() : IRequest<SuccessfulResult>;
public record SuccessfulResult();
public class AddProductsHandler(IProductService productService) : IRequestHandler<AddProductsCommand, SuccessfulResult>
{
    public async Task<SuccessfulResult> Handle(AddProductsCommand request, CancellationToken cancellationToken)
    {
        await productService.AddProducts();
        return new SuccessfulResult();
    }
}