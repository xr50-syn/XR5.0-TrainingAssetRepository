using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services.Materials;
using XR50TrainingAssetRepo.Tests.Fixtures;

namespace XR50TrainingAssetRepo.Tests.Services;

/// <summary>
/// Pins the program progress formula: the percentage is measured over the program's
/// learning-path materials only. Materials assigned directly to the program (support
/// materials) can be completed and are recorded, but they neither inflate the denominator
/// nor count as completed.
/// </summary>
public class ProgramProgressCalculationTests : IClassFixture<WebApplicationFixture>
{
    private readonly WebApplicationFixture _factory;

    public ProgramProgressCalculationTests(WebApplicationFixture factory)
    {
        _factory = factory;
    }

    private sealed record Seed(string UserId, int ProgramId, int LearningPathId, int PathMaterialId, int SupportMaterialId);

    private async Task<Seed> SeedProgramAsync()
    {
        var userId = $"learner-{Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IXR50TenantDbContextFactory>().CreateDbContext();

        context.Users.Add(new User { UserName = userId, FullName = "Learner" });

        var pathMaterial = new DefaultMaterial { Name = "Path material" };
        var supportMaterial = new DefaultMaterial { Name = "Support material" };
        context.Materials.AddRange(pathMaterial, supportMaterial);

        var program = new TrainingProgram { Name = "Program" };
        var learningPath = new LearningPath { LearningPathName = "Path", Description = "Path" };
        context.TrainingPrograms.Add(program);
        context.LearningPaths.Add(learningPath);
        await context.SaveChangesAsync();

        context.ProgramLearningPaths.Add(new ProgramLearningPath
        {
            TrainingProgramId = program.id,
            LearningPathId = learningPath.id
        });
        context.MaterialRelationships.Add(new MaterialRelationship
        {
            MaterialId = pathMaterial.id,
            RelatedEntityType = "LearningPath",
            RelatedEntityId = learningPath.id.ToString(),
            RelationshipType = "required",
            DisplayOrder = 1
        });
        context.ProgramMaterials.Add(new ProgramMaterial
        {
            TrainingProgramId = program.id,
            MaterialId = supportMaterial.id
        });
        await context.SaveChangesAsync();

        return new Seed(userId, program.id, learningPath.id, pathMaterial.id, supportMaterial.id);
    }

    private IUserMaterialService Service(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUserMaterialService>();

    [Fact]
    public async Task CompletingTheOnlyLearningPathMaterial_ReportsProgram100Percent()
    {
        var seed = await SeedProgramAsync();
        using var scope = _factory.Services.CreateScope();

        var result = await Service(scope).MarkMaterialCompleteAsync(
            seed.UserId, seed.PathMaterialId, new MarkMaterialCompleteRequest { program_id = seed.ProgramId });

        result.progress.Should().Be(100, "the support material must not be part of the denominator");
        result.learning_path_id.Should().Be(seed.LearningPathId);
        result.learning_path_progress.Should().Be(100);
    }

    [Fact]
    public async Task CompletingOnlyTheSupportMaterial_LeavesProgramAtZero()
    {
        var seed = await SeedProgramAsync();
        using var scope = _factory.Services.CreateScope();

        var result = await Service(scope).MarkMaterialCompleteAsync(
            seed.UserId, seed.SupportMaterialId, new MarkMaterialCompleteRequest { program_id = seed.ProgramId });

        result.success.Should().BeTrue("support materials can still be completed");
        result.progress.Should().Be(0);
        result.learning_path_id.Should().BeNull();
    }

    [Fact]
    public async Task UserOverviewAndProgramProgress_AgreeOnLearningPathOnlyPercentage()
    {
        var seed = await SeedProgramAsync();
        using var scope = _factory.Services.CreateScope();
        var service = Service(scope);

        await service.MarkMaterialCompleteAsync(
            seed.UserId, seed.PathMaterialId, new MarkMaterialCompleteRequest { program_id = seed.ProgramId });
        await service.MarkMaterialCompleteAsync(
            seed.UserId, seed.SupportMaterialId, new MarkMaterialCompleteRequest { program_id = seed.ProgramId });

        var overview = await service.GetUserProgressAsync(seed.UserId);
        var programOverview = overview.programs.Single(p => p.id == seed.ProgramId.ToString());
        programOverview.progress.Should().Be(100);
        programOverview.materials.Select(m => m.id)
            .Should().BeEquivalentTo(new[] { seed.PathMaterialId.ToString(), seed.SupportMaterialId.ToString() },
                "support materials stay visible in the listing");

        var programProgress = await service.GetProgramProgressAsync(seed.ProgramId, seed.UserId, isAdmin: false);
        programProgress.total_materials.Should().Be(1);
        var user = programProgress.user_progress.Single(u => u.user_id == seed.UserId);
        user.progress.Should().Be(100);
        user.materials_completed.Should().Be(1);
        user.total_materials.Should().Be(1);
        user.materials.Should().HaveCount(2);
        user.materials.Single(m => m.material_id == seed.SupportMaterialId).learning_path_id.Should().BeNull();
        programProgress.learning_paths.Single().progress.Should().Be(100);
    }

    [Fact]
    public async Task BulkComplete_MeasuresProgramOverLearningPathMaterialsOnly()
    {
        var seed = await SeedProgramAsync();
        using var scope = _factory.Services.CreateScope();

        var result = await Service(scope).BulkMarkMaterialsCompleteAsync(
            seed.UserId, seed.ProgramId,
            new BulkMaterialCompleteRequest { material_ids = new List<int> { seed.SupportMaterialId } });

        result.program_progress.Should().Be(0);

        result = await Service(scope).BulkMarkMaterialsCompleteAsync(
            seed.UserId, seed.ProgramId,
            new BulkMaterialCompleteRequest { material_ids = new List<int> { seed.PathMaterialId } });

        result.program_progress.Should().Be(100);
    }
}
