// API-layer exceptions: request validation and explicit unauthorized rejections.
// Mapped to ProblemDetails (RFC 7807) by WorkTimeExceptionHandler in Middleware.cs, so every
// rejection crosses the wire in one shape.
namespace WorkTime.Api;

public class InvalidRequestException : Exception
{
    public string Code { get; set; }

    public InvalidRequestException(string code, string message) : base(message)
    {
        this.Code = code;
    }
}

// An explicit access refusal (e.g. the BR32 session expired); mapped to 401.
public class UnauthorizedRequestException : Exception
{
    public string Code { get; set; }

    public UnauthorizedRequestException(string code, string message) : base(message)
    {
        this.Code = code;
    }
}
