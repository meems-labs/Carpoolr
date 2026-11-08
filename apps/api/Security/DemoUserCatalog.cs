namespace Carpoolr.Api.Security;

/// <summary>
/// Fixed identities for the explicitly enabled staging simulation, never real accounts.
/// </summary>
public static class DemoUserCatalog
{
    public const string HeaderName = "X-Carpoolr-Demo-User";
    public const string Issuer = "urn:carpoolr:staging-demo";

    public static IReadOnlyList<DemoUser> Users { get; } = Array.AsReadOnly<DemoUser>(
    [
        new(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Testperson 1"),
        new(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Testperson 2"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333333"), "Testperson 3"),
    ]);
}

public sealed record DemoUser(Guid Id, string DisplayName);

public sealed record DemoUsersResponse(bool Enabled, IReadOnlyList<DemoUser> Users);
