// Endpoint 1 of api-contracts.md: POST /api/auth/login.
//
// Login is the one endpoint that runs without a token - it is how a token is obtained. A failed login
// is data, never an error (BR35 lock, BR36 CAPTCHA), so the response stays 200 with AuthResult flags;
// only on success does the Stage-2 addition add accessToken / expiresIn (a JWT whose subject is the
// User's Guid and whose role claim drives the authorization policies).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkTime.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    private readonly AuthService _authService;

    private readonly IUserRepository _users;

    private readonly TokenService _tokenService;

    private readonly RequestContextProvider _requestContext;

    private readonly UserActivityTracker _activity;

    public AuthController(
        AuthService authService,
        IUserRepository users,
        TokenService tokenService,
        RequestContextProvider requestContext,
        UserActivityTracker activity)
    {
        _authService = authService;
        _users = users;
        _tokenService = tokenService;
        _requestContext = requestContext;
        _activity = activity;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    // Аудит 2.2: ограничен только вход — policy "login-rate-limit" из Program.cs.
    [EnableRateLimiting(Program.LoginRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<LoginWire> TryLogin(LoginRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();

        RequestParsers.TryRequireText(payload.login, "login");
        RequestParsers.TryRequireText(payload.password, "password");

        // AuthService never throws for a bad login: the lock countdown and the CAPTCHA prompt come
        // back as flags (BR35/BR36).
        var result = await _authService.LoginAsync(payload.login!, payload.password!, cancellationToken);

        if (!result.IsSuccess)
        {
            return WireConversion.TryFromAuthResult(result, null, 0);
        }

        // A successful login must resolve the User, because that Guid is what the access token names
        // as its subject. Missing user -> ENTITY_NOT_FOUND / "User" (404).
        var user = await _users.GetByLoginAsync(payload.login!, cancellationToken);

        if (user == null)
        {
            throw new EntityNotFoundException(Guid.Empty, "User");
        }

        // BR32 (правка аудита): успешный вход восстанавливает baseline активности,
        // иначе просроченная запись блокировала бы сессию навсегда.
        _activity.TryRecordActivity(user.Id.ToString(), DateTime.UtcNow);

        var accessToken = await _tokenService.TryIssueAccessToken(user.Id, user.Role, user.Login, cancellationToken);
        var expiresIn = _tokenService.TryExpiresInSeconds();

        return WireConversion.TryFromAuthResult(result, accessToken, expiresIn);
    }
}
