using FluentAssertions;
using XR50TrainingAssetRepo.Services.Migrations;
using Xunit;

namespace XR50TrainingAssetRepo.Tests.Migrations
{
    public class MigrateCliTests
    {
        [Fact]
        public void Split_RecognisesTheVerbOnlyInFirstPosition()
        {
            var (isMigrate, remaining) = MigrateCli.Split(new[] { "migrate", "--status" });
            isMigrate.Should().BeTrue();
            remaining.Should().Equal("--status");
            MigrateCli.Split(new[] { "--urls", "http://x", "migrate" }).IsMigrate.Should().BeFalse();
            MigrateCli.Split(Array.Empty<string>()).IsMigrate.Should().BeFalse();
        }

        [Theory]
        [InlineData(new string[0], MigrateCli.Mode.All)]
        [InlineData(new[] { "--all" }, MigrateCli.Mode.All)]
        [InlineData(new[] { "--central" }, MigrateCli.Mode.Central)]
        [InlineData(new[] { "--tenant", "a", "--tenant", "b" }, MigrateCli.Mode.Tenants)]
        [InlineData(new[] { "--status" }, MigrateCli.Mode.Status)]
        [InlineData(new[] { "--status", "--tenant", "a" }, MigrateCli.Mode.Status)]
        public void TryParse_ResolvesTheMode(string[] args, MigrateCli.Mode expected)
        {
            MigrateCli.TryParse(args, out var command, out var error).Should().BeTrue(error);
            command.Mode.Should().Be(expected);
        }

        [Fact]
        public void TryParse_ReadsFlags()
        {
            MigrateCli.TryParse(new[] { "--tenant", "a", "--target", "20260901000000_X", "--no-adopt-legacy", "--json" }, out var command, out _).Should().BeTrue();

            command.Tenants.Should().Equal("a");
            command.TargetMigration.Should().Be("20260901000000_X");
            command.AdoptLegacy.Should().BeFalse();
            command.Json.Should().BeTrue();
            command.ToOptions().TargetMigration.Should().Be("20260901000000_X");
        }

        [Theory]
        [InlineData(new object[] { new[] { "--all", "--central" } })]
        [InlineData(new object[] { new[] { "--central", "--tenant", "a" } })]
        [InlineData(new object[] { new[] { "--target", "m" } })]
        [InlineData(new object[] { new[] { "--target", "m", "--tenant", "a", "--tenant", "b" } })]
        [InlineData(new object[] { new[] { "--status", "--central" } })]
        [InlineData(new object[] { new[] { "--status", "--target", "m", "--tenant", "a" } })]
        [InlineData(new object[] { new[] { "--tenant" } })]
        [InlineData(new object[] { new[] { "--bogus" } })]
        public void TryParse_RejectsInvalidCombinations(string[] args)
        {
            MigrateCli.TryParse(args, out _, out var error).Should().BeFalse();
            error.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Run_UsageError_ExitsWith2()
        {
            var output = new StringWriter();

            var exit = await MigrateCli.RunAsync(new StubMigrator(), new[] { "--nope" }, output, CancellationToken.None);

            exit.Should().Be(MigrateCli.ExitUsage);
            output.ToString().Should().Contain("usage:");
        }

        [Fact]
        public async Task Run_All_MapsReportToExitCode()
        {
            var migrator = new StubMigrator { Report = Report(succeeded: true) };
            (await MigrateCli.RunAsync(migrator, Array.Empty<string>(), new StringWriter(), CancellationToken.None)).Should().Be(MigrateCli.ExitOk);
            migrator.AllOptions!.TolerateTenantFailures.Should().BeFalse();

            migrator.Report = Report(succeeded: false);
            (await MigrateCli.RunAsync(migrator, new[] { "--tolerate-tenant-failures" }, new StringWriter(), CancellationToken.None)).Should().Be(MigrateCli.ExitFailed);
            migrator.AllOptions!.TolerateTenantFailures.Should().BeTrue();

            migrator.Report = Report(succeeded: false, manual: true);
            (await MigrateCli.RunAsync(migrator, Array.Empty<string>(), new StringWriter(), CancellationToken.None)).Should().Be(MigrateCli.ExitManualIntervention);
        }

        [Fact]
        public async Task Run_Tenants_MigratesEachNamedTenant()
        {
            var migrator = new StubMigrator();
            var output = new StringWriter();

            var exit = await MigrateCli.RunAsync(migrator, new[] { "--tenant", "a", "--tenant", "b" }, output, CancellationToken.None);

            exit.Should().Be(MigrateCli.ExitOk);
            migrator.TenantsMigrated.Should().Equal("a", "b");
            output.ToString().Should().Contain("tenant:a").And.Contain("tenant:b").And.Contain("Migration succeeded.");
        }

        [Fact]
        public async Task Run_Status_PrintsStates_AndJsonWhenAsked()
        {
            var migrator = new StubMigrator();
            var text = new StringWriter();
            (await MigrateCli.RunAsync(migrator, new[] { "--status", "--tenant", "a" }, text, CancellationToken.None)).Should().Be(MigrateCli.ExitOk);
            text.ToString().Should().Contain("Managed").And.Contain("tenant:a");
            migrator.StatusTenant.Should().Be("a");

            var json = new StringWriter();
            await MigrateCli.RunAsync(migrator, new[] { "--status", "--json" }, json, CancellationToken.None);
            json.ToString().Should().Contain("\"state\": \"Managed\"");
            migrator.StatusTenant.Should().BeNull();
        }

        private static MigrationRunReport Report(bool succeeded, bool manual = false) =>
            new(new[]
            {
                new MigrationRunResult("registry@db", "db", SchemaState.Managed, true, false, Array.Empty<string>(), null),
                new MigrationRunResult("tenant:a@xr50_tenant_a", "xr50_tenant_a", SchemaState.Unknown, succeeded, false, Array.Empty<string>(),
                    succeeded ? null : "bad", manual)
            }, Array.Empty<string>(), succeeded);

        private sealed class StubMigrator : IXR50SchemaMigrator
        {
            public MigrationRunReport? Report { get; set; }
            public MigrateOptions? AllOptions { get; private set; }
            public List<string> TenantsMigrated { get; } = new();
            public string? StatusTenant { get; private set; } = "unset";

            public Task<IReadOnlyList<MigrationRunResult>> MigrateCentralAsync(MigrateOptions? options = null, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<MigrationRunResult>>(new[] { Ok("registry@db", "db"), Ok("training@db", "db") });

            public Task<MigrationRunResult> MigrateTenantAsync(string tenantName, MigrateOptions? options = null, CancellationToken ct = default)
            {
                TenantsMigrated.Add(tenantName);
                return Task.FromResult(Ok($"tenant:{tenantName}@xr50_tenant_{tenantName}", $"xr50_tenant_{tenantName}"));
            }

            public Task<MigrationRunReport> MigrateAllAsync(MigrateOptions? options = null, CancellationToken ct = default)
            {
                AllOptions = options;
                return Task.FromResult(Report ?? new MigrationRunReport(Array.Empty<MigrationRunResult>(), Array.Empty<string>(), true));
            }

            public Task<IReadOnlyList<MigrationTargetStatus>> GetStatusAsync(string? tenantName = null, CancellationToken ct = default)
            {
                StatusTenant = tenantName;
                var target = tenantName is null ? "registry@db" : $"tenant:{tenantName}@xr50_tenant_{tenantName}";
                return Task.FromResult<IReadOnlyList<MigrationTargetStatus>>(new[]
                {
                    new MigrationTargetStatus(target, "db", SchemaState.Managed, new[] { "20260820000000_Baseline" }, Array.Empty<string>(), null)
                });
            }

            private static MigrationRunResult Ok(string target, string db) =>
                new(target, db, SchemaState.Managed, true, false, Array.Empty<string>(), null);
        }
    }
}
