using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Carpoolr.Api.Security;

// A subject is unique within its issuer, not necessarily across identity providers.
public sealed record UserIdentity(string Issuer, string Subject)
{
    public static bool HasUsableClaims(
        [NotNullWhen(true)] string? issuer,
        [NotNullWhen(true)] string? subject) =>
        IsUsableClaim(issuer, 512) && IsUsableClaim(subject, 255);

    private static bool IsUsableClaim(string? value, int maximumUtf8Bytes) =>
        !string.IsNullOrWhiteSpace(value) &&
        Encoding.UTF8.GetByteCount(value) <= maximumUtf8Bytes &&
        !value.Any(char.IsControl);
}
