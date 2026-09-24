using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services.Materials;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// MaterialServiceBase.UpdateAsync must keep the parent row in place. On MySQL, deleting it
/// cascades incoming SubcomponentMaterialRelationships, program assignments and progress rows
/// even when the row is reinserted with the same ID. The InMemory provider does not cascade to
/// untracked rows, so these tests record deletions directly instead of relying on the cascade.
/// </summary>
public class MaterialUpdateInPlaceTests
{
    [Fact]
    public async Task UpdateAsync_ImageWithNewAnnotation_DoesNotDeleteMaterialRow()
    {
        var recorder = new MaterialDeletionRecorder();
        var factory = CreateFactory(recorder);
        DateTime? createdAt;

        using (var context = factory.CreateDbContext())
        {
            var seededImage = new ImageMaterial
            {
                id = 10,
                Name = "Related image",
                Type = MaterialType.Image,
                ImageAnnotations = { new ImageAnnotation { ClientId = "first", Text = "First" } }
            };
            context.Materials.Add(seededImage);
            var checklist = new ChecklistMaterial
            {
                id = 20,
                Name = "Checklist with image",
                Type = MaterialType.Checklist,
                Entries = { new ChecklistEntry { Text = "Entry" } }
            };
            context.Materials.Add(checklist);
            await context.SaveChangesAsync();
            // XR50TrainingContext stamps Created_at on insert, so a reinserted row gets a new one.
            createdAt = seededImage.Created_at;

            context.SubcomponentMaterialRelationships.Add(new SubcomponentMaterialRelationship
            {
                SubcomponentId = checklist.Entries[0].ChecklistEntryId,
                SubcomponentType = "ChecklistEntry",
                RelatedMaterialId = 10
            });
            await context.SaveChangesAsync();
        }

        var service = new MaterialServiceBase(factory, NullLogger<MaterialServiceBase>.Instance);
        await service.UpdateAsync(new ImageMaterial
        {
            id = 10,
            Name = "Related image",
            ImageAnnotations =
            {
                new ImageAnnotation { ClientId = "first", Text = "First" },
                new ImageAnnotation { ClientId = "second", Text = "Second" }
            }
        });

        recorder.DeletedMaterialIds.Should().BeEmpty();

        using var verify = factory.CreateDbContext();
        var image = await verify.Materials.OfType<ImageMaterial>().SingleAsync(m => m.id == 10);
        image.Created_at.Should().Be(createdAt);
        (await verify.ImageAnnotations.CountAsync(a => a.ImageMaterialId == 10)).Should().Be(2);
        (await verify.SubcomponentMaterialRelationships.CountAsync(r => r.RelatedMaterialId == 10))
            .Should().Be(1);
    }

    private static NewContextFactory CreateFactory(MaterialDeletionRecorder recorder)
    {
        var options = new DbContextOptionsBuilder<XR50TrainingContext>()
            .UseInMemoryDatabase($"material-update-in-place-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(recorder)
            .Options;
        return new NewContextFactory(options);
    }

    private sealed class MaterialDeletionRecorder : SaveChangesInterceptor
    {
        public List<int> DeletedMaterialIds { get; } = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null)
            {
                DeletedMaterialIds.AddRange(eventData.Context.ChangeTracker.Entries<Material>()
                    .Where(e => e.State == EntityState.Deleted)
                    .Select(e => e.Entity.id));
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class NewContextFactory : IXR50TenantDbContextFactory
    {
        private readonly DbContextOptions<XR50TrainingContext> _options;

        public NewContextFactory(DbContextOptions<XR50TrainingContext> options)
        {
            _options = options;
        }

        public XR50TrainingContext CreateDbContext() => new(_options);

        public XR50TrainingContext CreateAdminDbContext() => new(_options);
    }
}
