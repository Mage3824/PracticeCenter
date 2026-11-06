// Gives a controller access to the current request without declaring framework implicit parameters:
// the stop signal (RequestAborted) every repository/service call takes, and the HttpContext whose
// claims carry the caller's identity. Registered as a scoped service in Program.cs.
namespace WorkTime.Api;

public class RequestContextProvider
{
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _accessor;

    public RequestContextProvider(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Microsoft.AspNetCore.Http.HttpContext TryContext()
    {
        var context = _accessor.HttpContext;

        if (context == null)
        {
            throw new InvalidRequestException(
                "NO_REQUEST_CONTEXT",
                "[RU] The request context is unavailable for this call.");
        }

        return context;
    }

    public CancellationToken TryStopSignal()
    {
        return TryContext().RequestAborted;
    }
}
