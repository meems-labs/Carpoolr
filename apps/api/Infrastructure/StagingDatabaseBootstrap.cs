using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace Carpoolr.Api.Infrastructure;

// A short-lived GitOps bootstrap Job uses a dedicated, narrowly scoped service account.
// The HTTP application never needs Kubernetes API credentials or these permissions.
public static class StagingDatabaseBootstrap
{
    private const string CredentialsDirectory = "/var/run/secrets/kubernetes.io/serviceaccount/";
    private const string SecretPath = "api/v1/namespaces/staging/secrets/carpoolr-database";

    public static async Task RunAsync(IHostEnvironment environment)
    {
        if (!environment.IsStaging() ||
            (await File.ReadAllTextAsync(CredentialsDirectory + "namespace")).Trim() != "staging")
        {
            throw new InvalidOperationException("Database bootstrap is restricted to the staging namespace.");
        }

        using var certificateTrust = new KubernetesCertificateTrust(
            await File.ReadAllTextAsync(CredentialsDirectory + "ca.crt"));
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                certificateTrust.IsValidServerCertificate(certificate, errors),
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://kubernetes.default.svc/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await File.ReadAllTextAsync(CredentialsDirectory + "token")).Trim());

        using var existing = await client.GetAsync(SecretPath);
        if (existing.IsSuccessStatusCode)
        {
            var retained = await existing.Content.ReadFromJsonAsync<JsonElement>();
            if (retained.ValueKind != JsonValueKind.Object ||
                !retained.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("POSTGRES_PASSWORD", out var retainedPassword) ||
                string.IsNullOrWhiteSpace(retainedPassword.GetString()) ||
                !data.TryGetProperty("ConnectionStrings__Default", out var retainedConnection) ||
                string.IsNullOrWhiteSpace(retainedConnection.GetString()))
            {
                throw new InvalidOperationException("The existing staging database Secret has incomplete configuration.");
            }
            Console.WriteLine("Existing staging database Secret retained.");
            return;
        }
        if (existing.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Could not inspect staging database configuration ({(int)existing.StatusCode}).");

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "carpoolr-db",
            Port = 5432,
            Database = "carpoolr",
            Username = "carpoolr",
            Password = password,
        };
        connectionString["GSS Encryption Mode"] = "Disable";
        using var created = await client.PostAsJsonAsync("api/v1/namespaces/staging/secrets", new
        {
            apiVersion = "v1",
            kind = "Secret",
            metadata = new { name = "carpoolr-database", @namespace = "staging" },
            type = "Opaque",
            stringData = new Dictionary<string, string>
            {
                ["POSTGRES_PASSWORD"] = password,
                ["ConnectionStrings__Default"] = connectionString.ConnectionString,
            },
        });
        // Another bootstrap can win the create race. Never rotate an existing password.
        if (!created.IsSuccessStatusCode && created.StatusCode != HttpStatusCode.Conflict)
            throw new InvalidOperationException($"Could not prepare staging database configuration ({(int)created.StatusCode}).");
        Console.WriteLine("Staging database Secret prepared.");
    }
}
