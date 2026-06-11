using System.Security.Cryptography;
using System.Text;

namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Produces RFC-4122-v5-style deterministic GUIDs so an app role's Entra id is stable
/// across regenerations. Changing a role's id would orphan its existing assignments.
/// </summary>
public static class DeterministicGuid
{
    private const string Namespace = "anela-heblo-access-role:";

    public static Guid ForRole(string roleValue)
    {
        ArgumentNullException.ThrowIfNull(roleValue);
        var input = Encoding.UTF8.GetBytes(Namespace + roleValue);
        var hash = SHA1.HashData(input);
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant
        return new Guid(bytes);
    }
}
