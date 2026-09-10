using System;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    /// <summary>
    /// Who is performing an Outlook → Heblo sync; stamped into CreatedBy/ModifiedBy/DeletedBy.
    /// </summary>
    public sealed record SyncActor(string UserId, string Username)
    {
        public const string SystemUserId = "system";

        public static readonly SyncActor System = new(SystemUserId, "Outlook sync");

        public static SyncActor FromUser(CurrentUser user)
        {
            var userId = user.Id
                ?? throw new InvalidOperationException(
                    "Outlook import requires an authenticated user context.");

            return new SyncActor(userId, user.Name ?? "Unknown User");
        }
    }
}
