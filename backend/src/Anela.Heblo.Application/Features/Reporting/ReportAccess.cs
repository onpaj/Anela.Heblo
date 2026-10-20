using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Reporting;

/// <summary>The single permission rule for reporting data, shared by every entry point.</summary>
public static class ReportAccess
{
    public static string RequiredRole(ReportSource source) =>
        AccessRoles.For(ReportSources.FeatureFor(source), AccessLevel.Read);

    public static bool CanRead(ICurrentUserService user, ReportSource source) =>
        user.IsInRole(RequiredRole(source));
}
