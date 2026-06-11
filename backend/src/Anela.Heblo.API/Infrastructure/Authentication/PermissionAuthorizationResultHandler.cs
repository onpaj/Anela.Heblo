using System.Text.Json;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>
/// Augments the bare 403 produced by ASP.NET Core's authorization middleware with a structured
/// <see cref="BaseResponse"/>-shaped body that names the permission(s) the caller is missing.
/// Anonymous requests (401 challenge) and permission-less forbids fall through to the framework
/// default handler unchanged.
/// </summary>
public sealed class PermissionAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly IAuthorizationMiddlewareResultHandler _fallback;
    private readonly JsonSerializerOptions _jsonOptions;

    public PermissionAuthorizationResultHandler(IOptions<JsonOptions> jsonOptions)
        : this(jsonOptions, new AuthorizationMiddlewareResultHandler())
    {
    }

    internal PermissionAuthorizationResultHandler(
        IOptions<JsonOptions> jsonOptions,
        IAuthorizationMiddlewareResultHandler fallback)
    {
        _jsonOptions = jsonOptions.Value.JsonSerializerOptions;
        _fallback = fallback;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure is not null)
        {
            var requiredPermissions = authorizeResult.AuthorizationFailure.FailedRequirements
                .OfType<RolesAuthorizationRequirement>()
                .SelectMany(requirement => requirement.AllowedRoles)
                .Where(role => role != AccessRoles.Base)
                .Distinct()
                .ToArray();

            if (requiredPermissions.Length > 0)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";

                // Anonymous object mirrors the BaseResponse envelope (success/errorCode/params) so the
                // frontend treats it as a structured error. BaseResponse itself is abstract and cannot
                // be instantiated here.
                var payload = new
                {
                    success = false,
                    errorCode = ErrorCodes.InsufficientPermissions,
                    @params = new Dictionary<string, string>
                    {
                        ["requiredPermission"] = string.Join(", ", requiredPermissions),
                    },
                };

                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(payload, _jsonOptions),
                    context.RequestAborted);
                return;
            }
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
