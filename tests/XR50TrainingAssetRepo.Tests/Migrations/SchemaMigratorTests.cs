using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XR50TrainingAssetRepo.Services.Migrations;
using Xunit;

namespace XR50TrainingAssetRepo.Tests.Migrations
{
    /// <summary>
    /// Orchestration of the schema migrator against an in-memory picture of the server: state
    /// detection, legacy adoption order, failure isolation, orphan reporting. No database.
    /// </summary>
    public class SchemaMigratorTests
    {
        private const string Base = "magical_library";
        private const string TrainingHistory = "__EFMigrationsHistory";
        private const string RegistryHistory = "__EFMigrationsHistory_Registry";

        private readonly FakeSchemaInspector _inspector = new();
        private readonly FakeTargetFactory _targets;
        private readonly FakeReconciler _reconciler;

        public SchemaMigratorTests()
        {
            _targets = new FakeTargetFactory(_inspector);
            _reconciler = new FakeReconciler(_inspector);
        }

        private XR50SchemaMigrator CreateMigrator(int lockTimeoutSeconds = 120)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Server=unused;Database={Base};User=u;Password=p;",
                [XR50SchemaMigrator.LockTimeoutKey] = lockTimeoutSeconds.ToString()
            }).Build();

            return new XR50SchemaMigrator(configuration, _inspector, _targets, _reconciler, NullLogger<XR50SchemaMigrator>.Instance);
        }

        private FakeDatabase ManagedBase()
        {
            return _inspector.Add(Base)
                .WithTables(FakeTargetFactory.TrainingModelTables)
                .WithTables(FakeTargetFactory.RegistryModelTables)
                .WithHistory(TrainingHistory, FakeTargetFactory.TrainingBaseline, FakeTargetFactory.TrainingSecond)
                .WithHistory(RegistryHistory, FakeTargetFactory.RegistryBaseline);
        }

        private static FakeDatabase LegacyRawDdl(FakeDatabase db) =>
            db.WithTables("Users", "Groups", "Materials", "Assets", "TenantDirectories")
              .WithColumn("Materials", "Discriminator", "varchar(50)")
              .WithColumn("Assets", "Description", "varchar(1000)");

        // ----- single-target states -----

        [Fact]
        public async Task EmptyDatabase_IsMigratedWithoutAdoption()
        {
            _inspector.Add("xr50_tenant_fresh");

            var result = await CreateMigrator().MigrateTenantAsync("fresh");

            result.Succeeded.Should().BeTrue();
            result.StateBefore.Should().Be(SchemaState.Empty);
            result.Adopted.Should().BeFalse();
            result.AppliedNow.Should().Equal(FakeTargetFactory.TrainingBaseline, FakeTargetFactory.TrainingSecond);
            var target = _targets.Target("xr50_tenant_fresh", TrainingHistory)!;
            target.Stamped.Should().BeEmpty();
            target.Migrated.Should().Equal(new string?[] { null });
            _reconciler.TrainingCalls.Should().BeEmpty();
        }

        [Fact]
        public async Task ManagedDatabase_AppliesOnlyPendingMigrations()
        {
            _inspector.Add("xr50_tenant_acme")
                .WithTables(FakeTargetFactory.TrainingModelTables)
                .WithHistory(TrainingHistory, FakeTargetFactory.TrainingBaseline);

            var result = await CreateMigrator().MigrateTenantAsync("acme");

            result.StateBefore.Should().Be(SchemaState.Managed);
            result.AppliedNow.Should().Equal(FakeTargetFactory.TrainingSecond);
            _targets.Target("xr50_tenant_acme", TrainingHistory)!.Stamped.Should().BeEmpty();
        }

        [Fact]
        public async Task ManagedAndCurrent_IsANoOp()
        {
            _inspector.Add("xr50_tenant_acme")
                .WithTables(FakeTargetFactory.TrainingModelTables)
                .WithHistory(TrainingHistory, FakeTargetFactory.TrainingBaseline, FakeTargetFactory.TrainingSecond);

            var result = await CreateMigrator().MigrateTenantAsync("acme");

            result.Succeeded.Should().BeTrue();
            result.AppliedNow.Should().BeEmpty();
            _targets.Target("xr50_tenant_acme", TrainingHistory)!.Migrated.Should().BeEmpty();
        }

        [Fact]
        public async Task LegacyRawDdl_ReconcilesThenStampsBaselineThenMigrates()
        {
            LegacyRawDdl(_inspector.Add("xr50_tenant_legacy"));

            var result = await CreateMigrator().MigrateTenantAsync("legacy");

            result.Succeeded.Should().BeTrue();
            result.StateBefore.Should().Be(SchemaState.LegacyRawDdl);
            result.Adopted.Should().BeTrue();
            result.AppliedNow.Should().Equal(new[] { FakeTargetFactory.TrainingSecond }, "the Baseline is stamped, not executed");
            _inspector.Log.Should().ContainInOrder(
                "lock:xr50_tenant_legacy",
                "reconcile:xr50_tenant_legacy",
                $"stamp:xr50_tenant_legacy:{FakeTargetFactory.TrainingBaseline}",
                $"migrate:xr50_tenant_legacy:{TrainingHistory}",
                "unlock:xr50_tenant_legacy");
        }

        [Fact]
        public async Task LegacyRawDdl_ReconcileFailure_LeavesNoHistoryRow()
        {
            LegacyRawDdl(_inspector.Add("xr50_tenant_legacy"));
            _reconciler.Failure = new SchemaMigrationException("Users.Name has NULLs", manualInterventionRequired: true);

            var result = await CreateMigrator().MigrateTenantAsync("legacy");

            result.Succeeded.Should().BeFalse();
            result.ManualInterventionRequired.Should().BeTrue();
            result.Error.Should().Contain("Users.Name has NULLs");
            var target = _targets.Target("xr50_tenant_legacy", TrainingHistory)!;
            target.Stamped.Should().BeEmpty();
            target.Migrated.Should().BeEmpty();
            _inspector.Released.Should().Contain("xr50_tenant_legacy", "the lock is released on failure");
        }

        [Fact]
        public async Task LegacyRawDdl_IsRefusedWhenAdoptionIsDisabled()
        {
            LegacyRawDdl(_inspector.Add("xr50_tenant_legacy"));

            var result = await CreateMigrator().MigrateTenantAsync("legacy", new MigrateOptions { AdoptLegacy = false });

            result.Succeeded.Should().BeFalse();
            result.ManualInterventionRequired.Should().BeTrue();
            _reconciler.TrainingCalls.Should().BeEmpty();
        }

        [Fact]
        public async Task LegacyRawDdl_CrashBetweenReconcileAndStamp_ResumesWithoutStampingTwice()
        {
            // The history table exists (created by a previous attempt) but holds no rows.
            LegacyRawDdl(_inspector.Add("xr50_tenant_legacy")).WithHistory(TrainingHistory);

            var result = await CreateMigrator().MigrateTenantAsync("legacy");

            result.Succeeded.Should().BeTrue();
            result.StateBefore.Should().Be(SchemaState.LegacyRawDdl);
            _targets.Target("xr50_tenant_legacy", TrainingHistory)!.Stamped.Should().Equal(FakeTargetFactory.TrainingBaseline);
        }

        [Fact]
        public async Task LegacyEfConvention_EmptyTables_AreDroppedAndRebuilt()
        {
            _inspector.Add("xr50_tenant_old")
                .WithTables("Users", "Group", "Materials", "Assets", "SomethingElse")
                .WithHistory(TrainingHistory, "20260803080837_InitialCreate")
                .WithColumn("Materials", "Description", "longtext");

            var result = await CreateMigrator().MigrateTenantAsync("old");

            result.Succeeded.Should().BeTrue();
            result.StateBefore.Should().Be(SchemaState.LegacyEfConvention);
            result.Adopted.Should().BeTrue();
            _inspector.Dropped.Select(d => d.Table).Should().BeEquivalentTo("Users", "Group", "Materials", "Assets");
            _inspector.Dropped.Should().NotContain(d => d.Table == "SomethingElse", "tables outside the model are left alone");
            var target = _targets.Target("xr50_tenant_old", TrainingHistory)!;
            target.HistoryCleared.Should().Be(1);
            target.Stamped.Should().BeEmpty("EF-convention databases are rebuilt, not stamped");
            result.AppliedNow.Should().Equal(FakeTargetFactory.TrainingBaseline, FakeTargetFactory.TrainingSecond);
            _reconciler.TrainingCalls.Should().BeEmpty();
        }

        [Fact]
        public async Task LegacyEfConvention_WithData_IsRefusedWithoutDropping()
        {
            _inspector.Add("xr50_tenant_old")
                .WithTables("Users", "Group", "Materials")
                .WithHistory(TrainingHistory, "20260803080837_InitialCreate")
                .WithRows("Materials", 3);

            var result = await CreateMigrator().MigrateTenantAsync("old");

            result.Succeeded.Should().BeFalse();
            result.ManualInterventionRequired.Should().BeTrue();
            result.Error.Should().Contain("Materials");
            _inspector.Dropped.Should().BeEmpty();
            _targets.Target("xr50_tenant_old", TrainingHistory)!.HistoryCleared.Should().Be(0);
        }

        [Fact]
        public async Task UnknownShape_IsRefused()
        {
            _inspector.Add("xr50_tenant_weird").WithTables("Users");

            var result = await CreateMigrator().MigrateTenantAsync("weird");

            result.Succeeded.Should().BeFalse();
            result.StateBefore.Should().Be(SchemaState.Unknown);
            result.ManualInterventionRequired.Should().BeTrue();
            _targets.Target("xr50_tenant_weird", TrainingHistory)!.Migrated.Should().BeEmpty();
        }

        [Fact]
        public async Task MissingDatabase_IsNeverCreated()
        {
            var result = await CreateMigrator().MigrateTenantAsync("nope");

            result.Succeeded.Should().BeFalse();
            result.StateBefore.Should().Be(SchemaState.Missing);
            _targets.Created.Should().BeEmpty();
            _inspector.Locked.Should().BeEmpty();
        }

        [Fact]
        public async Task TargetMigration_OlderThanBaseline_IsRefused()
        {
            _inspector.Add("xr50_tenant_acme")
                .WithTables(FakeTargetFactory.TrainingModelTables)
                .WithHistory(TrainingHistory, FakeTargetFactory.TrainingBaseline, FakeTargetFactory.TrainingSecond);

            var result = await CreateMigrator().MigrateTenantAsync("acme", new MigrateOptions { TargetMigration = "20200101000000_Ancient" });

            result.Succeeded.Should().BeFalse();
            result.Error.Should().Contain("predates the Baseline");
            _targets.Target("xr50_tenant_acme", TrainingHistory)!.Migrated.Should().BeEmpty();
        }

        [Fact]
        public async Task TargetMigration_IsPassedThroughToTheTarget()
        {
            _inspector.Add("xr50_tenant_acme")
                .WithTables(FakeTargetFactory.TrainingModelTables)
                .WithHistory(TrainingHistory, FakeTargetFactory.TrainingBaseline, FakeTargetFactory.TrainingSecond);

            var result = await CreateMigrator().MigrateTenantAsync("acme", new MigrateOptions { TargetMigration = FakeTargetFactory.TrainingBaseline });

            result.Succeeded.Should().BeTrue();
            _targets.Target("xr50_tenant_acme", TrainingHistory)!.Migrated.Should().Equal(FakeTargetFactory.TrainingBaseline);
        }

        [Fact]
        public async Task MixedCaseTenantName_ReachesTheServerUnchanged()
        {
            _inspector.Add("xr50_tenant_Acme_Corp");

            var result = await CreateMigrator().MigrateTenantAsync("Acme-Corp");

            result.Succeeded.Should().BeTrue();
            result.DatabaseName.Should().Be("xr50_tenant_Acme_Corp");
            _inspector.Locked.Should().Equal("xr50_tenant_Acme_Corp");
        }

        // ----- central -----

        [Fact]
        public async Task Central_LegacyBase_AdoptsRegistryByStampAndTrainingByRebuild()
        {
            // The sandbox's real shape: EF-convention training tables (empty) with the old
            // InitialCreate in the history, a raw registry table, no registry history.
            _inspector.Add(Base)
                .WithTables("Users", "Group", "Materials", "Assets", "XR50TenantRegistry")
                .WithHistory(TrainingHistory, "20260803080837_InitialCreate")
                .WithColumn("Materials", "Description", "longtext")
                .WithRows("XR50TenantRegistry", 1);

            var results = await CreateMigrator().MigrateCentralAsync();

            results.Should().HaveCount(2);
            results[0].Target.Should().Be($"registry@{Base}");
            results[0].StateBefore.Should().Be(SchemaState.LegacyRawDdl);
            results[0].Adopted.Should().BeTrue();
            _reconciler.RegistryCalls.Should().Equal(Base);
            _targets.Target(Base, RegistryHistory)!.Stamped.Should().Equal(FakeTargetFactory.RegistryBaseline);

            results[1].Target.Should().Be($"training@{Base}");
            results[1].StateBefore.Should().Be(SchemaState.LegacyEfConvention);
            results[1].Adopted.Should().BeTrue();
            _inspector.Dropped.Select(d => d.Table).Should().BeEquivalentTo("Users", "Group", "Materials", "Assets");
            _inspector.Dropped.Should().NotContain(d => d.Table == "XR50TenantRegistry", "the registry belongs to the other context");
        }

        [Fact]
        public async Task Central_RegistryFailure_StopsBeforeTraining()
        {
            _inspector.Add(Base).WithTables("XR50TenantRegistry");
            _reconciler.Failure = new InvalidOperationException("boom");

            var results = await CreateMigrator().MigrateCentralAsync();

            results.Should().HaveCount(1);
            results[0].Succeeded.Should().BeFalse();
        }

        // ----- fan-out -----

        [Fact]
        public async Task MigrateAll_CentralThenEveryActiveTenant_ReportsOrphans()
        {
            ManagedBase();
            _inspector.Add("xr50_tenant_a");
            _inspector.Add("xr50_tenant_b");
            _inspector.Add("xr50_tenant_inactive");
            _inspector.Add("xr50_tenant_orphan");
            _inspector.Registry.Add(new RegisteredTenant("a", "xr50_tenant_a", true));
            _inspector.Registry.Add(new RegisteredTenant("b", "xr50_tenant_b", true));
            _inspector.Registry.Add(new RegisteredTenant("inactive", "xr50_tenant_inactive", false));

            var report = await CreateMigrator().MigrateAllAsync();

            report.Succeeded.Should().BeTrue();
            report.Results.Select(r => r.Target).Should().Equal(
                $"registry@{Base}", $"training@{Base}", "tenant:a@xr50_tenant_a", "tenant:b@xr50_tenant_b");
            report.OrphanSchemas.Should().Equal("xr50_tenant_orphan");
            _inspector.Locked.Should().NotContain("xr50_tenant_orphan");
            _inspector.Locked.Should().NotContain("xr50_tenant_inactive");
        }

        [Fact]
        public async Task MigrateAll_OrphanDetection_IsCaseExactUnlessServerFoldsCase()
        {
            ManagedBase();
            _inspector.Add("xr50_tenant_Acme");
            _inspector.Registry.Add(new RegisteredTenant("Acme", "xr50_tenant_Acme", true));

            var exact = await CreateMigrator().MigrateAllAsync();
            exact.OrphanSchemas.Should().BeEmpty();

            _inspector.Add("xr50_tenant_acme");
            var stillExact = await CreateMigrator().MigrateAllAsync();
            stillExact.OrphanSchemas.Should().Equal(new[] { "xr50_tenant_acme" }, "with lower_case_table_names=0 these are different databases");

            _inspector.LowerCaseTableNames = true;
            var folded = await CreateMigrator().MigrateAllAsync();
            folded.OrphanSchemas.Should().BeEmpty("with lower_case_table_names=1 the registry name covers both spellings");
        }

        [Fact]
        public async Task MigrateAll_TenantFailure_DoesNotStopOtherTenants_AndFailsTheRunUnlessTolerated()
        {
            ManagedBase();
            _inspector.Add("xr50_tenant_a");
            _inspector.Add("xr50_tenant_b");
            _inspector.Registry.Add(new RegisteredTenant("a", "xr50_tenant_a", true));
            _inspector.Registry.Add(new RegisteredTenant("b", "xr50_tenant_b", true));
            _targets.MigrateFailure = db => db == "xr50_tenant_a" ? new InvalidOperationException("disk full") : null;

            var strict = await CreateMigrator().MigrateAllAsync();

            strict.Succeeded.Should().BeFalse();
            strict.Results.Should().HaveCount(4);
            strict.Results.Single(r => r.Target.StartsWith("tenant:a")).Succeeded.Should().BeFalse();
            strict.Results.Single(r => r.Target.StartsWith("tenant:b")).Succeeded.Should().BeTrue("one tenant's failure must not block the next");
            _inspector.Released.Should().Contain("xr50_tenant_a");

            var tolerant = await CreateMigrator().MigrateAllAsync(new MigrateOptions { TolerateTenantFailures = true });
            tolerant.Succeeded.Should().BeTrue();
            tolerant.Results.Single(r => r.Target.StartsWith("tenant:a")).Succeeded.Should().BeFalse("tolerating does not hide the failure");
        }

        [Fact]
        public async Task MigrateAll_CentralFailure_AbortsBeforeTenants_EvenWhenTolerant()
        {
            _inspector.Add(Base).WithTables("Users");
            _inspector.Add("xr50_tenant_a");
            _inspector.Registry.Add(new RegisteredTenant("a", "xr50_tenant_a", true));
            _targets.MigrateFailure = db => db == Base ? new InvalidOperationException("registry down") : null;

            var report = await CreateMigrator().MigrateAllAsync(new MigrateOptions { TolerateTenantFailures = true });

            report.Succeeded.Should().BeFalse();
            report.Results.Should().HaveCount(1);
            _inspector.Locked.Should().NotContain("xr50_tenant_a");
        }

        // ----- status -----

        [Fact]
        public async Task Status_ReportsEveryTargetWithoutTouchingAnything()
        {
            ManagedBase();
            LegacyRawDdl(_inspector.Add("xr50_tenant_legacy"));
            _inspector.Add("xr50_tenant_fresh");
            _inspector.Registry.Add(new RegisteredTenant("legacy", "xr50_tenant_legacy", true));
            _inspector.Registry.Add(new RegisteredTenant("fresh", "xr50_tenant_fresh", true));

            var statuses = await CreateMigrator().GetStatusAsync();

            statuses.Select(s => (s.Target, s.State)).Should().Equal(
                ($"registry@{Base}", SchemaState.Managed),
                ($"training@{Base}", SchemaState.Managed),
                ("tenant:legacy@xr50_tenant_legacy", SchemaState.LegacyRawDdl),
                ("tenant:fresh@xr50_tenant_fresh", SchemaState.Empty));
            statuses[1].Applied.Should().Equal(FakeTargetFactory.TrainingBaseline, FakeTargetFactory.TrainingSecond);
            statuses[1].Pending.Should().BeEmpty();
            statuses[2].Pending.Should().Equal(FakeTargetFactory.TrainingBaseline, FakeTargetFactory.TrainingSecond);
            _inspector.Locked.Should().BeEmpty();
            _targets.Created.Should().OnlyContain(t => !t.Migrated.Any() && !t.Stamped.Any());
        }

        [Fact]
        public async Task Status_ForOneTenant_DoesNotNeedTheRegistry()
        {
            _inspector.Add("xr50_tenant_x")
                .WithTables(FakeTargetFactory.TrainingModelTables)
                .WithHistory(TrainingHistory, FakeTargetFactory.TrainingBaseline);

            var statuses = await CreateMigrator().GetStatusAsync("x");

            statuses.Should().ContainSingle().Which.Pending.Should().Equal(FakeTargetFactory.TrainingSecond);
        }
    }
}
