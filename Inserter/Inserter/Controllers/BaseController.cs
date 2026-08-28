using Application.BaseClasses;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Inserter.Controllers;

public class BaseController(IMediator mediator) : Controller
{
    public string? Ip
    {
        get
        {
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            }
            return ip ?? "-";
        }
    }

    [ApiExplorerSettings(IgnoreApi = true)]
    protected async Task<IActionResult> HandleRequest<TResult>(BaseRequest<TResult> request)
    {
        request.Ip = Ip;
        var result = await mediator.Send(request);

        return Ok(result);
    }
}