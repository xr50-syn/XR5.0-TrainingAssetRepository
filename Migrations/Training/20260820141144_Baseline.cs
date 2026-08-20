using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XR50TrainingAssetRepo.Migrations.Training
{
    /// <inheritdoc />
    /// <summary>
    /// Baseline of the tenant schema as deployed before migrations existed. Generated from the
    /// model, then hand-edited in exactly one way: the foreign key constraints that the deployed
    /// databases never had were removed from <see cref="Up"/> (only the four constraints that
    /// really exist were kept). The model snapshot still declares those relationships, so later
    /// migrations will not try to re-add them; adding them is a deliberate future migration after
    /// an orphan-row audit. Existing databases adopt this migration by having its row stamped
    /// into the history table, so this file must keep describing what is really there.
    /// </summary>
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Src = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Filetype = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Filename = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Url = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentHash = table.Column<string>(type: "char(64)", maxLength: 64, nullable: true, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    StorageKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AiAvailable = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "notready")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    JobId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    GroupName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TenantName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.GroupName);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LearningPaths",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LearningPathName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningPaths", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Created_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    Updated_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Unique_id = table.Column<int>(type: "int", nullable: true),
                    Discriminator = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceJobId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AIAssistantStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, defaultValue: "notready")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AIAssistantAssetIds = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CollectionName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatbotConfig = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatbotModel = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatbotPrompt = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssetId = table.Column<int>(type: "int", nullable: true),
                    ImagePath = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImageWidth = table.Column<int>(type: "int", nullable: true),
                    ImageHeight = table.Column<int>(type: "int", nullable: true),
                    ImageFormat = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InnovPilot = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InnovStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, defaultValue: "notready")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InnovAssetIds = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InnovExpertiseLevel = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    message_type = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    message_text = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PdfPath = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PdfPageCount = table.Column<int>(type: "int", nullable: true),
                    PdfFileSize = table.Column<long>(type: "bigint", nullable: true),
                    QuestionnaireConfig = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuestionnaireType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PassingScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    EvaluationMode = table.Column<bool>(type: "tinyint(1)", nullable: true, defaultValue: false),
                    MinScore = table.Column<int>(type: "int", nullable: true),
                    UnityVersion = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UnityBuildTarget = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UnitySceneName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UnityJson = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VideoPath = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VideoDuration = table.Column<int>(type: "int", nullable: true),
                    VideoResolution = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    startTime = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Annotations = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Shares",
                columns: table => new
                {
                    ShareId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Target = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shares", x => x.ShareId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    TenantName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TenantGroup = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TenantSchema = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TenantDirectory = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.TenantName);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TrainingPrograms",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Created_at = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Objectives = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Requirements = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    min_level_rank = table.Column<int>(type: "int", nullable: true),
                    max_level_rank = table.Column<int>(type: "int", nullable: true),
                    required_upto_level_rank = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingPrograms", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FullName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserEmail = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Password = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    admin = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserName);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AIAssistantMaterialAssetJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AIAssistantMaterialId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    CollectionName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    JobId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DocumentName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "pending")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIAssistantMaterialAssetJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIAssistantMaterialAssetJobs_Materials_AIAssistantMaterialId",
                        column: x => x.AIAssistantMaterialId,
                        principalTable: "Materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AIAssistantSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AIAssistantMaterialId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "active")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AssetHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIAssistantSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIAssistantSessions_Materials_AIAssistantMaterialId",
                        column: x => x.AIAssistantMaterialId,
                        principalTable: "Materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Entries",
                columns: table => new
                {
                    ChecklistEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Text = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChecklistMaterialId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.ChecklistEntryId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ImageAnnotations",
                columns: table => new
                {
                    ImageAnnotationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FontSize = table.Column<int>(type: "int", nullable: true),
                    X = table.Column<double>(type: "double", nullable: false, defaultValue: 0.0),
                    Y = table.Column<double>(type: "double", nullable: false, defaultValue: 0.0),
                    ImageMaterialId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageAnnotations", x => x.ImageAnnotationId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InnovChatbotMaterialAssetJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InnovChatbotMaterialId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    Pilot = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CollectionName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "pending")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InnovChatbotMaterialAssetJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InnovChatbotMaterialAssetJobs_Materials",
                        column: x => x.InnovChatbotMaterialId,
                        principalTable: "Materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MaterialRelationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    RelatedEntityId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelatedEntityType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelationshipType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayOrder = table.Column<int>(type: "int", nullable: true),
                    inherit_from_program = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    min_level_rank = table.Column<int>(type: "int", nullable: true),
                    max_level_rank = table.Column<int>(type: "int", nullable: true),
                    required_upto_level_rank = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRelationships", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuestionnaireEntries",
                columns: table => new
                {
                    QuestionnaireEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Text = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuestionnaireMaterialId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionnaireEntries", x => x.QuestionnaireEntryId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuizQuestions",
                columns: table => new
                {
                    QuizQuestionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    QuestionNumber = table.Column<int>(type: "int", nullable: false),
                    QuestionType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "text")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Score = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    HelpText = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AllowMultiple = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    ScaleConfig = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuizMaterialId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestions", x => x.QuizQuestionId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubcomponentMaterialRelationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SubcomponentId = table.Column<int>(type: "int", nullable: false),
                    SubcomponentType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelatedMaterialId = table.Column<int>(type: "int", nullable: false),
                    RelationshipType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayOrder = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcomponentMaterialRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "fk_subcomponent_material",
                        column: x => x.RelatedMaterialId,
                        principalTable: "Materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Timestamps",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    startTime = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    endTime = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Duration = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VideoMaterialId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timestamps", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WorkflowSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WorkflowMaterialId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowSteps", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ProgramLearningPaths",
                columns: table => new
                {
                    TrainingProgramId = table.Column<int>(type: "int", nullable: false),
                    LearningPathId = table.Column<int>(type: "int", nullable: false),
                    inherit_from_program = table.Column<bool>(type: "tinyint(1)", nullable: true, defaultValue: true),
                    min_level_rank = table.Column<int>(type: "int", nullable: true),
                    max_level_rank = table.Column<int>(type: "int", nullable: true),
                    required_upto_level_rank = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramLearningPaths", x => new { x.TrainingProgramId, x.LearningPathId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ProgramMaterials",
                columns: table => new
                {
                    TrainingProgramId = table.Column<int>(type: "int", nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    inherit_from_program = table.Column<bool>(type: "tinyint(1)", nullable: true, defaultValue: true),
                    min_level_rank = table.Column<int>(type: "int", nullable: true),
                    max_level_rank = table.Column<int>(type: "int", nullable: true),
                    required_upto_level_rank = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramMaterials", x => new { x.TrainingProgramId, x.MaterialId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupUsers",
                columns: table => new
                {
                    GroupName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupUsers", x => new { x.GroupName, x.UserName });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TenantAdmins",
                columns: table => new
                {
                    TenantName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAdmins", x => new { x.TenantName, x.UserName });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserMaterialData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProgramId = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LearningPathId = table.Column<int>(type: "int", nullable: true),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMaterialData", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserMaterialScores",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProgramId = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    LearningPathId = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    Progress = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMaterialScores", x => new { x.UserId, x.MaterialId, x.ProgramId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuizAnswers",
                columns: table => new
                {
                    QuizAnswerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Text = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CorrectAnswer = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: true),
                    Extra = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuizQuestionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAnswers", x => x.QuizAnswerId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AIAssistantMaterialAssetJobs_Material_Asset",
                table: "AIAssistantMaterialAssetJobs",
                columns: new[] { "AIAssistantMaterialId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIAssistantMaterialAssetJobs_Status",
                table: "AIAssistantMaterialAssetJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AIAssistantSessions_AIAssistantMaterialId",
                table: "AIAssistantSessions",
                column: "AIAssistantMaterialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ai_available",
                table: "Assets",
                column: "AiAvailable");

            migrationBuilder.CreateIndex(
                name: "ux_assets_content_hash",
                table: "Assets",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_checklist_material",
                table: "Entries",
                column: "ChecklistMaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_group",
                table: "GroupUsers",
                column: "GroupName");

            migrationBuilder.CreateIndex(
                name: "idx_user",
                table: "GroupUsers",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "idx_image_material",
                table: "ImageAnnotations",
                column: "ImageMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_InnovChatbotMaterialAssetJobs_Material_Asset",
                table: "InnovChatbotMaterialAssetJobs",
                columns: new[] { "InnovChatbotMaterialId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InnovChatbotMaterialAssetJobs_Status",
                table: "InnovChatbotMaterialAssetJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_id",
                table: "MaterialRelationships",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_related_entity",
                table: "MaterialRelationships",
                columns: new[] { "RelatedEntityId", "RelatedEntityType" });

            migrationBuilder.CreateIndex(
                name: "idx_relationship_type",
                table: "MaterialRelationships",
                column: "RelationshipType");

            migrationBuilder.CreateIndex(
                name: "idx_asset_id",
                table: "Materials",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "idx_discriminator",
                table: "Materials",
                column: "Discriminator");

            migrationBuilder.CreateIndex(
                name: "idx_image_path",
                table: "Materials",
                column: "ImagePath");

            migrationBuilder.CreateIndex(
                name: "idx_pdf_path",
                table: "Materials",
                column: "PdfPath");

            migrationBuilder.CreateIndex(
                name: "idx_type",
                table: "Materials",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "idx_unique_id",
                table: "Materials",
                column: "Unique_id");

            migrationBuilder.CreateIndex(
                name: "idx_video_path",
                table: "Materials",
                column: "VideoPath");

            migrationBuilder.CreateIndex(
                name: "idx_path",
                table: "ProgramLearningPaths",
                column: "LearningPathId");

            migrationBuilder.CreateIndex(
                name: "idx_program",
                table: "ProgramLearningPaths",
                column: "TrainingProgramId");

            migrationBuilder.CreateIndex(
                name: "idx_material",
                table: "ProgramMaterials",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_program",
                table: "ProgramMaterials",
                column: "TrainingProgramId");

            migrationBuilder.CreateIndex(
                name: "idx_questionnaire_material",
                table: "QuestionnaireEntries",
                column: "QuestionnaireMaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_quiz_question",
                table: "QuizAnswers",
                column: "QuizQuestionId");

            migrationBuilder.CreateIndex(
                name: "idx_quiz_material",
                table: "QuizQuestions",
                column: "QuizMaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_material",
                table: "SubcomponentMaterialRelationships",
                column: "RelatedMaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_subcomponent",
                table: "SubcomponentMaterialRelationships",
                columns: new[] { "SubcomponentId", "SubcomponentType" });

            migrationBuilder.CreateIndex(
                name: "idx_unique_relationship",
                table: "SubcomponentMaterialRelationships",
                columns: new[] { "SubcomponentId", "SubcomponentType", "RelatedMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_tenant",
                table: "TenantAdmins",
                column: "TenantName");

            migrationBuilder.CreateIndex(
                name: "idx_user",
                table: "TenantAdmins",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "idx_video_material",
                table: "Timestamps",
                column: "VideoMaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_learning_path_id",
                table: "UserMaterialData",
                column: "LearningPathId");

            migrationBuilder.CreateIndex(
                name: "idx_material_id",
                table: "UserMaterialData",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_program_id",
                table: "UserMaterialData",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "idx_user_id",
                table: "UserMaterialData",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "idx_user_material_program",
                table: "UserMaterialData",
                columns: new[] { "UserId", "MaterialId", "ProgramId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_learning_path_id",
                table: "UserMaterialScores",
                column: "LearningPathId");

            migrationBuilder.CreateIndex(
                name: "idx_material_id",
                table: "UserMaterialScores",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "idx_program_id",
                table: "UserMaterialScores",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "idx_workflow_material",
                table: "WorkflowSteps",
                column: "WorkflowMaterialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIAssistantMaterialAssetJobs");

            migrationBuilder.DropTable(
                name: "AIAssistantSessions");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "Entries");

            migrationBuilder.DropTable(
                name: "GroupUsers");

            migrationBuilder.DropTable(
                name: "ImageAnnotations");

            migrationBuilder.DropTable(
                name: "InnovChatbotMaterialAssetJobs");

            migrationBuilder.DropTable(
                name: "MaterialRelationships");

            migrationBuilder.DropTable(
                name: "ProgramLearningPaths");

            migrationBuilder.DropTable(
                name: "ProgramMaterials");

            migrationBuilder.DropTable(
                name: "QuestionnaireEntries");

            migrationBuilder.DropTable(
                name: "QuizAnswers");

            migrationBuilder.DropTable(
                name: "Shares");

            migrationBuilder.DropTable(
                name: "SubcomponentMaterialRelationships");

            migrationBuilder.DropTable(
                name: "TenantAdmins");

            migrationBuilder.DropTable(
                name: "Timestamps");

            migrationBuilder.DropTable(
                name: "UserMaterialData");

            migrationBuilder.DropTable(
                name: "UserMaterialScores");

            migrationBuilder.DropTable(
                name: "WorkflowSteps");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "TrainingPrograms");

            migrationBuilder.DropTable(
                name: "QuizQuestions");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "LearningPaths");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Materials");
        }
    }
}
