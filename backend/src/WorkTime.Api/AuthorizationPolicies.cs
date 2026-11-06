// Named authorization policies for the controllers (Stage 2, phase "authorization").
//
//   AdminOnly      - administrator only            (BR29 audit-log read path)
//   ManagerOrAdmin - manager or administrator      (BR25/BR28: setting an absence for someone else)
//   EmployeeOnly   - any authenticated employee    (the everyday work paths)
//
// Each policy is DenyAnonymousAuthorizationRequirement + RolesAuthorizationRequirement over the
// role names TokenService puts into ClaimTypes.Role. Registered through AddAuthorization(options)
// in Program.cs, so [Authorize(Policy = "...")] resolves to the requirements below.
//
// DefaultPolicy and FallbackPolicy both require an authenticated caller: an endpoint that names no
// policy is still closed to anonymous requests. Only [AllowAnonymous] endpoints opt out (/api/auth/login),
// and Swagger / Swagger UI are middleware, not MVC endpoints, so they stay reachable anonymously.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace WorkTime.Api;

public static class WorkTimeAuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string ManagerOrAdmin = "ManagerOrAdmin";
    public const string EmployeeOnly = "EmployeeOnly";

    public static void TryConfigure(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        options.DefaultPolicy = TryAuthenticatedOnlyPolicy();
        options.FallbackPolicy = TryAuthenticatedOnlyPolicy();

        options.AddPolicy(AdminOnly, TryRolePolicy(new string[] { "Admin" }));
        options.AddPolicy(ManagerOrAdmin, TryRolePolicy(new string[] { "Manager", "Admin" }));
        options.AddPolicy(EmployeeOnly, TryRolePolicy(new string[] { "Employee" }));
    }

    // Any authenticated user: the token must be valid, whatever role it carries.
    private static AuthorizationPolicy TryAuthenticatedOnlyPolicy()
    {
        return new AuthorizationPolicyBuilder()
            .AddRequirements(new DenyAnonymousAuthorizationRequirement())
            .Build();
    }

    // A policy of allowed roles built from one RolesAuthorizationRequirement.
    private static AuthorizationPolicy TryRolePolicy(IEnumerable<string> allowedRoles)
    {
        return new AuthorizationPolicyBuilder()
            .AddRequirements(
                new DenyAnonymousAuthorizationRequirement(),
                new RolesAuthorizationRequirement(allowedRoles))
            .Build();
    }
}
