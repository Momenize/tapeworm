using MediatR;

namespace Application.BaseClasses;

public class BaseRequest<TResult> : IRequest<TResult>
{
    public string? Ip { get; set; }
}