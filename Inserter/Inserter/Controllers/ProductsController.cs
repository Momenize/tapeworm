using Application;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Inserter.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProductsController(IMediator mediator) : ControllerBase
    {
        [Route("Insert")]
        public async Task<IActionResult> AddProducts()
        {
            var result = await mediator.Send(new AddProductsCommand());
            if(result is not null)
            {
                return Ok(result);
            }
            return BadRequest();
        }

        [Route("Get/{Id}")]
        public async Task<IActionResult> GetChannelProducts([FromRoute] int Id)
        {
            var result = await mediator.Send(new GetChannelProductsQuery
            {
                ChannelId = Id
            });
            if(result is not null)
            {
                return Ok(result);
            }
            return BadRequest();
        }
    }
}
