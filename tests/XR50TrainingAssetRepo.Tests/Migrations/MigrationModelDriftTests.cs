using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using XR50TrainingAssetRepo.Data;
using Xunit;

namespace XR50TrainingAssetRepo.Tests.Migrations
{
    /// <summary>
    /// Fails when the EF model has changed without a migration capturing the change. This
    /// replaces the old convention of editing the hand-written table creator alongside the
    /// model: the committed migrations are the schema, so model and snapshot must agree.
    /// No database is involved; the relational model is built against a pinned MariaDB version.
    /// </summary>
    public class MigrationModelDriftTests
    {
        // Never dialed: the server version is pinned, so the provider builds the model offline.
        private const string PlaceholderConnection = "Server=unused;Database=unused;User=unused;Password=unused";

        [Fact]
        public void TrainingContext_ModelMatchesLatestMigrationSnapshot()
        {
            var options = new DbContextOptionsBuilder<XR50TrainingContext>()
                .UseMySql(PlaceholderConnection, XR50ServerVersion.Resolve(null))
                .Options;

            using var context = new XR50TrainingContext(options);

            AssertNoDrift(context,
                "dotnet ef migrations add <Name> --context XR50TrainingContext --output-dir Migrations/Training");
        }

        [Fact]
        public void RegistryContext_ModelMatchesLatestMigrationSnapshot()
        {
            using var context = new XR50RegistryContext(
                XR50RegistryContext.BuildOptions(PlaceholderConnection, XR50ServerVersion.Resolve(null)));

            AssertNoDrift(context,
                "dotnet ef migrations add <Name> --context XR50RegistryContext --output-dir Migrations/Registry");
        }

        [Fact]
        public void TrainingContext_BaselineIsTheFirstMigration()
        {
            var options = new DbContextOptionsBuilder<XR50TrainingContext>()
                .UseMySql(PlaceholderConnection, XR50ServerVersion.Resolve(null))
                .Options;
            using var context = new XR50TrainingContext(options);

            var ids = context.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();

            ids.Should().NotBeEmpty();
            ids[0].Should().EndWith("_Baseline",
                "legacy tenant databases are adopted by stamping the first migration, which must be the Baseline");
        }

        [Fact]
        public void RegistryContext_BaselineIsTheFirstMigration()
        {
            using var context = new XR50RegistryContext(
                XR50RegistryContext.BuildOptions(PlaceholderConnection, XR50ServerVersion.Resolve(null)));

            var ids = context.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();

            ids.Should().NotBeEmpty();
            ids[0].Should().EndWith("_RegistryBaseline");
        }

        private static void AssertNoDrift(DbContext context, string howToFix)
        {
            var snapshotModel = context.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
            snapshotModel.Should().NotBeNull("a model snapshot must be committed under Migrations/");

            if (snapshotModel is IMutableModel mutable)
            {
                snapshotModel = mutable.FinalizeModel();
            }

            snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel!);

            var hasDifferences = context.GetService<IMigrationsModelDiffer>().HasDifferences(
                snapshotModel.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

            hasDifferences.Should().BeFalse(
                $"the model changed without a migration; run `dotnet build` then `{howToFix} --no-build` and commit the result");
        }
    }
}
