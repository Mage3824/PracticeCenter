// The identity of the caller, read from the access token and never from a body (api-contracts.md §6:
// "the client sends criteria - id / date / period / department - never a list of ids", and Stage 2
// fixes employeeId / currentUserId / editorId / actorId to come from the token subject).
using System.Security.Claims;
using WorkTime.Domain;

namespace WorkTime.Api;

public class RequestIdentity
{
    // ClaimTypes.NameIdentifier -> the Guid of the authenticated User. A token that carries no
    // subject, or a subject that is not a Guid, is a rejected request rather than a business rule.
    public static Guid TrySubjectUserId(HttpContext context)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (subject == null)
        {
            throw new InvalidRequestException(
                "IDENTITY_MISSING",
                "[RU] The access token carries no subject claim.");
        }

        if (!Guid.TryParse(subject, out var userId))
        {
            throw new InvalidRequestException(
                "IDENTITY_INVALID",
                "[RU] The access token subject is not a user id.");
        }

        return userId;
    }

    // ClaimTypes.Role -> UserRole, for the checks that need the role in code rather than through an
    // authorization policy (the same token both authorizes and identifies the caller).
    public static UserRole TryRole(HttpContext context)
    {
        var roleName = context.User.FindFirstValue(ClaimTypes.Role);

        if (roleName != null)
        {
            foreach (var role in Enum.GetValues<UserRole>())
            {
                if (role.ToString() == roleName)
                {
                    return role;
                }
            }
        }

        throw new InvalidRequestException(
            "ROLE_MISSING",
            "[RU] The access token carries no role claim.");
    }

    // Stage-2 identity gap of api-contracts.md §8: a service such as StartWorkDayAsync does not
    // resolve the user itself, so every controller checks that the token subject is a known User
    // before any business rule runs - an unknown subject is a 404, never a created record.
    public static async Task<User> TryRequireKnownUserAsync(
        IUserRepository users,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            throw new EntityNotFoundException(userId, "User");
        }

        return user;
    }
}
