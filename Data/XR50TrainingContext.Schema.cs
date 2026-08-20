using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Data
{
    /// <summary>
    /// Column types, lengths, defaults, index names and constraint names of the tenant schema
    /// exactly as it exists in deployed tenant databases.
    ///
    /// The Baseline migration is generated from this model, and databases provisioned before
    /// migrations existed are adopted by stamping that Baseline without executing it. That only
    /// works if the model describes what is really in those databases, so this file follows the
    /// deployed shape (the former hand-written CREATE TABLE script) rather than EF conventions.
    /// Changing anything here is a schema change: add a migration for it.
    /// </summary>
    public partial class XR50TrainingContext
    {
        private static void ApplyDeployedSchemaShapes(ModelBuilder modelBuilder)
        {
            ConfigureIdentity(modelBuilder);
            ConfigureTenant(modelBuilder);
            ConfigureAssets(modelBuilder);
            ConfigurePrograms(modelBuilder);
            ConfigureMaterials(modelBuilder);
            ConfigureMaterialChildren(modelBuilder);
            ConfigureRelationships(modelBuilder);
            ConfigureUserProgress(modelBuilder);
            ConfigureAiJobs(modelBuilder);
        }

        private static void ConfigureIdentity(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(e =>
            {
                e.Property(u => u.UserName).HasMaxLength(255);
                e.Property(u => u.FullName).HasMaxLength(255);
                e.Property(u => u.UserEmail).HasMaxLength(255);
                e.Property(u => u.Password).HasMaxLength(255);
                e.Property(u => u.admin).HasDefaultValue(false);
            });

            modelBuilder.Entity<Group>(e =>
            {
                e.ToTable("Groups");
                e.Property(g => g.GroupName).HasMaxLength(255);
                e.Property(g => g.TenantName).HasMaxLength(255);
            });

            modelBuilder.Entity<GroupUser>(e =>
            {
                e.Property(gu => gu.GroupName).HasMaxLength(255);
                e.Property(gu => gu.UserName).HasMaxLength(255);
                // Point the navigations at the key columns; by convention EF would add shadow
                // GroupName1 / UserName1 foreign keys that exist in no deployed schema.
                e.HasOne(gu => gu.Group).WithMany(g => g.GroupUsers).HasForeignKey(gu => gu.GroupName);
                e.HasOne(gu => gu.User).WithMany().HasForeignKey(gu => gu.UserName);
                e.HasIndex(gu => gu.GroupName).HasDatabaseName("idx_group");
                e.HasIndex(gu => gu.UserName).HasDatabaseName("idx_user");
            });

            modelBuilder.Entity<TenantAdmin>(e =>
            {
                e.Property(ta => ta.TenantName).HasMaxLength(255);
                e.Property(ta => ta.UserName).HasMaxLength(255);
                e.HasIndex(ta => ta.TenantName).HasDatabaseName("idx_tenant");
                e.HasIndex(ta => ta.UserName).HasDatabaseName("idx_user");
            });
        }

        private static void ConfigureTenant(ModelBuilder modelBuilder)
        {
            // The per-tenant Tenants table only ever held the six descriptive columns below. The
            // storage, AI, INNOV and Hub settings live in the central registry
            // (XR50TenantRegistry, owned by XR50RegistryContext) and are never read through this
            // DbSet, so they are excluded from the tenant schema rather than added to it.
            modelBuilder.Entity<XR50Tenant>(e =>
            {
                e.Ignore(t => t.Owner);
                e.Ignore(t => t.StorageType);
                e.Ignore(t => t.StorageEndpoint);
                e.Ignore(t => t.S3BucketName);
                e.Ignore(t => t.S3BucketRegion);
                e.Ignore(t => t.S3BucketArn);
                e.Ignore(t => t.DefaultAICollection);
                e.Ignore(t => t.InnovChatbotBaseUrl);
                e.Ignore(t => t.InnovChatbotApiToken);
                e.Ignore(t => t.InnovChatbotDefaultPilot);
                e.Ignore(t => t.HubTenantId);
                e.Ignore(t => t.CreatedAt);
                e.Ignore(t => t.UpdatedAt);

                e.Property(t => t.TenantName).HasMaxLength(255);
                e.Property(t => t.TenantGroup).HasMaxLength(255);
                e.Property(t => t.TenantSchema).HasMaxLength(255);
                e.Property(t => t.Description).HasMaxLength(1000);
                e.Property(t => t.OwnerName).HasMaxLength(255);
                e.Property(t => t.TenantDirectory).HasMaxLength(500);
            });
        }

        private static void ConfigureAssets(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Asset>(e =>
            {
                e.Property(a => a.Description).HasMaxLength(1000);
                e.Property(a => a.Src).HasMaxLength(500);
                e.Property(a => a.Filetype).HasMaxLength(100);
                e.Property(a => a.Type).HasDefaultValue(AssetType.Image);
                e.Property(a => a.Filename).HasMaxLength(255);
                e.Property(a => a.URL).HasColumnName("Url").HasMaxLength(2000);
                // Hex SHA-256: fixed width, ASCII, case-sensitive for the unique index.
                e.Property(a => a.ContentHash).HasColumnType("char(64)").HasCharSet("ascii").UseCollation("ascii_bin");
                e.Property(a => a.StorageKey).HasMaxLength(512);
                e.Property(a => a.AiAvailable).HasMaxLength(20).HasDefaultValue("notready");
                e.Property(a => a.JobId).HasMaxLength(255);
                e.HasIndex(a => a.AiAvailable).HasDatabaseName("idx_ai_available");
            });

            modelBuilder.Entity<Share>(e =>
            {
                e.Property(s => s.ShareId).HasMaxLength(50);
                e.Property(s => s.FileId).HasMaxLength(50);
                e.Property(s => s.Target).HasMaxLength(255);
            });
        }

        private static void ConfigurePrograms(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TrainingProgram>(e =>
            {
                e.Property(tp => tp.Created_at).HasMaxLength(255);
                e.Property(tp => tp.Name).HasMaxLength(255);
                e.Property(tp => tp.Description).HasMaxLength(1000);
                e.Property(tp => tp.Objectives).HasMaxLength(1000);
                e.Property(tp => tp.Requirements).HasMaxLength(1000);
            });

            modelBuilder.Entity<LearningPath>(e =>
            {
                e.Property(lp => lp.Description).HasMaxLength(1000);
                e.Property(lp => lp.LearningPathName).HasMaxLength(255);
            });

            modelBuilder.Entity<ProgramMaterial>(e =>
            {
                e.Property(pm => pm.inherit_from_program).HasDefaultValue(true);
                e.HasIndex(pm => pm.TrainingProgramId).HasDatabaseName("idx_program");
                e.HasIndex(pm => pm.MaterialId).HasDatabaseName("idx_material");
            });

            modelBuilder.Entity<ProgramLearningPath>(e =>
            {
                e.Property(plp => plp.inherit_from_program).HasDefaultValue(true);
                e.HasIndex(plp => plp.TrainingProgramId).HasDatabaseName("idx_program");
                e.HasIndex(plp => plp.LearningPathId).HasDatabaseName("idx_path");
            });
        }

        private static void ConfigureMaterials(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Material>(e =>
            {
                e.Property(m => m.Name).HasMaxLength(255);
                e.Property(m => m.Description).HasMaxLength(1000);
                e.Property(m => m.Created_at).HasColumnType("datetime");
                e.Property(m => m.Updated_at).HasColumnType("datetime");
                e.Property<string>("Discriminator").HasMaxLength(50);
                e.HasIndex("Discriminator").HasDatabaseName("idx_discriminator");
                e.HasIndex(m => m.Type).HasDatabaseName("idx_type");
                e.HasIndex(m => m.Unique_id).HasDatabaseName("idx_unique_id");
            });

            modelBuilder.Entity<VideoMaterial>(e =>
            {
                e.Property(m => m.VideoPath).HasMaxLength(500);
                e.Property(m => m.VideoResolution).HasMaxLength(20);
                e.Property(m => m.startTime).HasMaxLength(50);
                e.HasIndex(m => m.VideoPath).HasDatabaseName("idx_video_path");
                // AssetId is one TPH column shared by every asset-backed subtype; index it once.
                e.HasIndex(m => m.AssetId).HasDatabaseName("idx_asset_id");
            });

            modelBuilder.Entity<ImageMaterial>(e =>
            {
                e.Property(m => m.ImagePath).HasMaxLength(500);
                e.Property(m => m.ImageFormat).HasMaxLength(20);
                e.HasIndex(m => m.ImagePath).HasDatabaseName("idx_image_path");
            });

            modelBuilder.Entity<PDFMaterial>(e =>
            {
                e.Property(m => m.PdfPath).HasMaxLength(500);
                e.HasIndex(m => m.PdfPath).HasDatabaseName("idx_pdf_path");
            });

            modelBuilder.Entity<MQTT_TemplateMaterial>(e =>
            {
                e.Property(m => m.message_type).HasMaxLength(255);
                e.Property(m => m.message_text).HasColumnType("text");
            });

            modelBuilder.Entity<UnityMaterial>(e =>
            {
                e.Property(m => m.UnityVersion).HasMaxLength(50);
                e.Property(m => m.UnityBuildTarget).HasMaxLength(50);
                e.Property(m => m.UnitySceneName).HasMaxLength(255);
                e.Property(m => m.UnityJson).HasColumnType("text");
            });

            modelBuilder.Entity<ChatbotMaterial>(e =>
            {
                e.Property(m => m.ChatbotConfig).HasColumnType("text");
                e.Property(m => m.ChatbotModel).HasMaxLength(100);
                e.Property(m => m.ChatbotPrompt).HasColumnType("text");
            });

            modelBuilder.Entity<QuestionnaireMaterial>(e =>
            {
                e.Property(m => m.QuestionnaireConfig).HasColumnType("text");
                e.Property(m => m.QuestionnaireType).HasMaxLength(50);
                e.Property(m => m.PassingScore).HasColumnType("decimal(5,2)");
            });

            modelBuilder.Entity<AIAssistantMaterial>(e =>
            {
                e.Property(m => m.ServiceJobId).HasMaxLength(255);
                e.Property(m => m.AIAssistantStatus).HasMaxLength(20).HasDefaultValue("notready");
                e.Property(m => m.AIAssistantAssetIds).HasColumnType("text");
            });

            modelBuilder.Entity<InnovChatbotMaterial>(e =>
            {
                e.Property(m => m.Pilot).HasMaxLength(255);
                e.Property(m => m.InnovStatus).HasMaxLength(20).HasDefaultValue("notready");
                e.Property(m => m.InnovAssetIds).HasColumnType("text");
                e.Property(m => m.ExpertiseLevel).HasMaxLength(50);
            });

            modelBuilder.Entity<QuizMaterial>(e =>
            {
                e.Property(m => m.EvaluationMode).HasDefaultValue(false);
            });
        }

        private static void ConfigureMaterialChildren(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<VideoTimestamp>(e =>
            {
                e.Property(t => t.Title).HasMaxLength(255);
                e.Property(t => t.startTime).HasMaxLength(50);
                e.Property(t => t.endTime).HasMaxLength(50);
                e.Property(t => t.Description).HasMaxLength(1000);
                e.Property(t => t.Type).HasMaxLength(255);
                e.HasIndex(t => t.VideoMaterialId).HasDatabaseName("idx_video_material");
            });

            modelBuilder.Entity<ChecklistEntry>(e =>
            {
                e.Property(c => c.Text).HasMaxLength(1000);
                e.Property(c => c.Description).HasMaxLength(1000);
                e.HasIndex(c => c.ChecklistMaterialId).HasDatabaseName("idx_checklist_material");
            });

            modelBuilder.Entity<QuestionnaireEntry>(e =>
            {
                e.Property(q => q.Text).HasMaxLength(1000);
                e.Property(q => q.Description).HasMaxLength(1000);
                e.HasIndex(q => q.QuestionnaireMaterialId).HasDatabaseName("idx_questionnaire_material");
            });

            modelBuilder.Entity<WorkflowStep>(e =>
            {
                e.Property(w => w.Title).HasMaxLength(255);
                e.Property(w => w.Content).HasColumnType("text");
                e.HasIndex(w => w.WorkflowMaterialId).HasDatabaseName("idx_workflow_material");
            });

            modelBuilder.Entity<ImageAnnotation>(e =>
            {
                e.Property(a => a.X).HasDefaultValue(0d);
                e.Property(a => a.Y).HasDefaultValue(0d);
                e.HasIndex(a => a.ImageMaterialId).HasDatabaseName("idx_image_material");
            });

            modelBuilder.Entity<QuizQuestion>(e =>
            {
                e.Property(q => q.QuestionType).HasDefaultValue("text");
                e.Property(q => q.Score).HasColumnType("decimal(10,2)");
                e.Property(q => q.AllowMultiple).HasDefaultValue(false);
                e.HasIndex(q => q.QuizMaterialId).HasDatabaseName("idx_quiz_material");
            });

            modelBuilder.Entity<QuizAnswer>(e =>
            {
                e.Property(a => a.CorrectAnswer).HasDefaultValue(false);
                e.HasIndex(a => a.QuizQuestionId).HasDatabaseName("idx_quiz_question");
            });
        }

        private static void ConfigureRelationships(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MaterialRelationship>(e =>
            {
                e.Property(mr => mr.RelatedEntityId).HasMaxLength(50);
                e.Property(mr => mr.RelatedEntityType).HasMaxLength(50);
                e.Property(mr => mr.RelationshipType).HasMaxLength(50);
                e.HasIndex(mr => mr.MaterialId).HasDatabaseName("idx_id");
                e.HasIndex(mr => new { mr.RelatedEntityId, mr.RelatedEntityType }).HasDatabaseName("idx_related_entity");
                e.HasIndex(mr => mr.RelationshipType).HasDatabaseName("idx_relationship_type");
            });

            modelBuilder.Entity<SubcomponentMaterialRelationship>(e =>
            {
                e.HasOne(smr => smr.RelatedMaterial)
                    .WithMany()
                    .HasForeignKey(smr => smr.RelatedMaterialId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("fk_subcomponent_material");
                e.HasIndex(smr => new { smr.SubcomponentId, smr.SubcomponentType }).HasDatabaseName("idx_subcomponent");
                e.HasIndex(smr => smr.RelatedMaterialId).HasDatabaseName("idx_material");
                e.HasIndex(smr => new { smr.SubcomponentId, smr.SubcomponentType, smr.RelatedMaterialId })
                    .HasDatabaseName("idx_unique_relationship");
            });
        }

        private static void ConfigureUserProgress(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserMaterialData>(e =>
            {
                e.Property(d => d.CreatedAt).HasColumnType("datetime");
                e.Property(d => d.UpdatedAt).HasColumnType("datetime");
                e.HasIndex(d => new { d.UserId, d.MaterialId, d.ProgramId }).HasDatabaseName("idx_user_material_program");
                e.HasIndex(d => d.UserId).HasDatabaseName("idx_user_id");
                e.HasIndex(d => d.MaterialId).HasDatabaseName("idx_material_id");
                e.HasIndex(d => d.ProgramId).HasDatabaseName("idx_program_id");
                e.HasIndex(d => d.LearningPathId).HasDatabaseName("idx_learning_path_id");
            });

            modelBuilder.Entity<UserMaterialScore>(e =>
            {
                e.Property(s => s.Score).HasDefaultValue(0m);
                e.Property(s => s.Progress).HasDefaultValue(0);
                e.Property(s => s.UpdatedAt).HasColumnType("datetime");
                e.HasIndex(s => s.MaterialId).HasDatabaseName("idx_material_id");
                e.HasIndex(s => s.ProgramId).HasDatabaseName("idx_program_id");
                e.HasIndex(s => s.LearningPathId).HasDatabaseName("idx_learning_path_id");
            });
        }

        private static void ConfigureAiJobs(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AIAssistantSession>(e =>
            {
                e.Property(s => s.Status).HasDefaultValue("active");
            });

            modelBuilder.Entity<AIAssistantMaterialAssetJob>(e =>
            {
                e.Property(j => j.Status).HasDefaultValue("pending");
                e.Property(j => j.ErrorMessage).HasColumnType("text");
                e.HasIndex(j => new { j.AIAssistantMaterialId, j.AssetId })
                    .HasDatabaseName("IX_AIAssistantMaterialAssetJobs_Material_Asset");
            });

            modelBuilder.Entity<InnovChatbotMaterialAssetJob>(e =>
            {
                e.Property(j => j.Status).HasDefaultValue("pending");
                e.Property(j => j.ErrorMessage).HasColumnType("text");
                e.HasOne(j => j.InnovChatbotMaterial)
                    .WithMany()
                    .HasForeignKey(j => j.InnovChatbotMaterialId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK_InnovChatbotMaterialAssetJobs_Materials");
                e.HasIndex(j => new { j.InnovChatbotMaterialId, j.AssetId })
                    .HasDatabaseName("IX_InnovChatbotMaterialAssetJobs_Material_Asset");
            });
        }
    }
}
