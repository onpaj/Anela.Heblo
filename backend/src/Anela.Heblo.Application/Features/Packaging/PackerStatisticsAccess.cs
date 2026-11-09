using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Packaging;

/// <summary>
/// The single rule for who may see how many orders each packer packed. Everyone with
/// packaging access sees the totals; only holders of <c>warehouse.packer_statistics.read</c>
/// (warehouse manager, management, super_user) get the per-packer breakdown. Enforced
/// server-side so the breakdown never leaves the API for anyone else.
/// </summary>
public static class PackerStatisticsAccess
{
    public static bool CanViewBreakdown(ICurrentUserService user) =>
        user.IsInRole(AccessRoles.WarehousePackerStatisticsRead);
}
