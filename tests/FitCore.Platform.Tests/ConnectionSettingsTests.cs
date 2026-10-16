using FitCore.Platform.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Platform.Tests;

/// <summary>Connection settings the RLS spike showed to be unsafe are refused (RLS spike §3.2, §4 rule 5).</summary>
public sealed class ConnectionSettingsTests
{
    [Theory]
    [InlineData("Host=aws-0-us-west-2.pooler.supabase.com;Port=6543;Username=fitcore_api.ref")]
    [InlineData("Host=db.ref.supabase.co;Port=6543;Username=fitcore_api")]
    [InlineData("Host=10.0.4.12;Port=6543;Username=fitcore_api.ref")]
    [InlineData("Host=pooler.internal.example;Port=6543;Username=fitcore_api.ref")]
    [InlineData("Host=127.0.0.1;Port=54322;Username=fitcore_api;No Reset On Close=true")]
    public void Unsafe_settings_are_refused(string connectionString)
    {
        using var services = Services(connectionString);

        Assert.Throws<InvalidOperationException>(services.GetRequiredService<FitCoreDatabase>);
    }

    [Theory]
    [InlineData("Host=aws-0-us-west-2.pooler.supabase.com;Port=5432;Username=fitcore_api.ref")]
    [InlineData("Host=db.ref.supabase.co;Port=5432;Username=fitcore_api")]
    [InlineData("Host=127.0.0.1;Port=54322;Username=fitcore_api")]
    public void Session_mode_and_direct_connections_are_accepted(string connectionString)
    {
        using var services = Services(connectionString);

        Assert.NotNull(services.GetRequiredService<FitCoreDatabase>());
    }

    [Fact]
    public async Task Disposing_the_host_closes_the_connection_pool()
    {
        var services = Services("Host=127.0.0.1;Port=54322;Username=fitcore_api");
        var database = services.GetRequiredService<FitCoreDatabase>();

        await services.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => database.BeginAsync(new DbIdentity(Guid.NewGuid()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_missing_connection_string_fails_only_when_the_database_is_used()
    {
        using var services = Services(null);

        var error = Assert.Throws<InvalidOperationException>(services.GetRequiredService<FitCoreDatabase>);
        Assert.Contains("ConnectionStrings:Fitcore", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("aal0")]
    [InlineData("AAL2")]
    public void An_unknown_assurance_level_is_refused(string aal)
    {
        Assert.Throws<ArgumentException>(() => new DbIdentity(Guid.NewGuid(), aal));
    }

    [Fact]
    public void An_empty_subject_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new DbIdentity(Guid.Empty));
    }

    [Fact]
    public void The_claims_have_the_shape_of_a_Supabase_access_token()
    {
        var subject = Guid.Parse("a0000000-0000-4000-8000-000000000001");

        Assert.Equal(
            """{"sub":"a0000000-0000-4000-8000-000000000001","role":"authenticated","aud":"authenticated","aal":"aal1"}""",
            new DbIdentity(subject).ToClaimsJson());
    }

    private static ServiceProvider Services(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Fitcore"] = connectionString })
            .Build();
        return new ServiceCollection().AddFitCoreDatabase(configuration).BuildServiceProvider();
    }
}
