using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Services
{
    public interface IXR50ManualTableCreator
    {
        Task<bool> CreateAllTablesAsync(string tenantName);
        Task<bool> CreateTablesInDatabaseAsync(string databaseName);
        Task<List<string>> GetExistingTablesAsync(string tenantName);
        Task<bool> DropAllTablesAsync(string tenantName);
        Task<bool> MigrateAssetTypeColumnAsync(string tenantName);
        Task<bool> MigrateAnnotationsColumnsAsync(string tenantName);
        Task<bool> MigrateSubcomponentMaterialRelationshipsTableAsync(string tenantName);
        Task<bool> MigrateProgramAssignmentRanksAsync(string tenantName);
        Task<bool> MigrateQuizAnswersTableAsync(string tenantName);
        Task<bool> MigrateUserMaterialTablesAsync(string tenantName);
    }

    public class XR50ManualTableCreator : IXR50ManualTableCreator
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<XR50ManualTableCreator> _logger;
        private readonly IXR50TenantService _tenantService;

        public XR50ManualTableCreator(
            IConfiguration configuration,
            ILogger<XR50ManualTableCreator> logger,
            IXR50TenantService tenantService)
        {
            _configuration = configuration;
            _logger = logger;
            _tenantService = tenantService;
        }

        public async Task<bool> CreateAllTablesAsync(string tenantName)
        {
            var tenantDbName = _tenantService.GetTenantSchema(tenantName);
            return await CreateTablesInDatabaseAsync(tenantDbName);
        }

        public async Task<bool> CreateTablesInDatabaseAsync(string databaseName)
        {
            try
            {
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                
                _logger.LogInformation("=== Creating tables in database: {DatabaseName} ===", databaseName);
                _logger.LogInformation("Base connection: {BaseConnection}", baseConnectionString.Replace("Password=", "Password=***"));
                _logger.LogInformation("Base database name: {BaseDatabaseName}", baseDatabaseName);
                
                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={databaseName}", StringComparison.OrdinalIgnoreCase);
                _logger.LogInformation("Target connection: {TargetConnection}", connectionString.Replace("Password=", "Password=***"));
                
                // Check if replacement worked
                if (connectionString == baseConnectionString)
                {
                    _logger.LogError("Connection string replacement FAILED!");
                    _logger.LogError("Looking for: 'database={BaseDatabaseName}' in connection string", baseDatabaseName);
                    _logger.LogError("Full base connection: {FullConnection}", baseConnectionString);
                    throw new InvalidOperationException($"Could not replace database name in connection string. Looking for 'database={baseDatabaseName}' in: {baseConnectionString}");
                }

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Verify which database we're actually connected to
                var currentDbCommand = new MySqlCommand("SELECT DATABASE()", connection);
                var actualDatabase = await currentDbCommand.ExecuteScalarAsync();
                _logger.LogInformation(" Actually connected to database: {ActualDatabase}", actualDatabase);

                if (!actualDatabase.ToString().Equals(databaseName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Connected to wrong database! Expected: {databaseName}, Actual: {actualDatabase}");
                }

                // Execute each CREATE TABLE statement separately
                var createStatements = GetCreateTableStatements();
                _logger.LogInformation("Executing {StatementCount} CREATE TABLE statements...", createStatements.Count);
                
                foreach (var statement in createStatements)
                {
                    try
                    {
                        var tableName = ExtractTableName(statement);
                        _logger.LogDebug("Creating table: {TableName}", tableName);
                        var command = new MySqlCommand(statement, connection);
                        await command.ExecuteNonQueryAsync();
                        _logger.LogDebug(" Created table: {TableName}", tableName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to execute CREATE TABLE statement: {Statement}", 
                            statement.Substring(0, Math.Min(100, statement.Length)));
                        throw; // Re-throw to stop the process
                    }
                }

                // Verify tables were created in the correct database
                var tables = await GetExistingTablesInDatabaseAsync(databaseName);
                _logger.LogInformation(" Successfully created {TableCount} tables in database {DatabaseName}: {Tables}", 
                    tables.Count, databaseName, string.Join(", ", tables));

                return tables.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tables in database: {DatabaseName}", databaseName);
                return false;
            }
        }

        private string ExtractTableName(string createStatement)
        {
            // Simple extraction of table name from CREATE TABLE statement
            var lines = createStatement.Split('\n');
            var createLine = lines[0].Trim();
            var parts = createLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 6 && parts[0].Equals("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                return parts[5].Trim('`');
            }
            return "unknown";
        }

        public async Task<List<string>> GetExistingTablesAsync(string tenantName)
        {
            var tenantDbName = _tenantService.GetTenantSchema(tenantName);
            return await GetExistingTablesInDatabaseAsync(tenantDbName);
        }

        private async Task<List<string>> GetExistingTablesInDatabaseAsync(string databaseName)
        {
            var tables = new List<string>();

            try
            {
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                
                // Use case-insensitive replacement (same as table creation)
                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={databaseName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("Getting tables from database: {DatabaseName}", databaseName);
                _logger.LogInformation("Connection string for verification: {ConnectionString}", connectionString.Replace("Password=", "Password=***"));

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Verify which database we're actually connected to
                var currentDbCommand = new MySqlCommand("SELECT DATABASE()", connection);
                var actualDatabase = await currentDbCommand.ExecuteScalarAsync();
                _logger.LogInformation("Actually connected to database for table check: {ActualDatabase}", actualDatabase);

                // Try SHOW TABLES first
                var showTablesCommand = new MySqlCommand("SHOW TABLES", connection);
                using var reader = await showTablesCommand.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
                reader.Close();

                _logger.LogInformation("SHOW TABLES returned {TableCount} tables: {Tables}", 
                    tables.Count, string.Join(", ", tables));

                // Also try INFORMATION_SCHEMA query as backup
                var infoSchemaCommand = new MySqlCommand(
                    "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema", 
                    connection);
                infoSchemaCommand.Parameters.AddWithValue("@schema", databaseName);
                
                using var reader2 = await infoSchemaCommand.ExecuteReaderAsync();
                var infoSchemaTables = new List<string>();
                
                while (await reader2.ReadAsync())
                {
                    infoSchemaTables.Add(reader2.GetString(0));
                }

                _logger.LogInformation("INFORMATION_SCHEMA query returned {TableCount} tables: {Tables}", 
                    infoSchemaTables.Count, string.Join(", ", infoSchemaTables));

                // Return the larger list (in case one method works better)
                return tables.Count >= infoSchemaTables.Count ? tables : infoSchemaTables;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get tables from database: {DatabaseName}", databaseName);
            }

            return tables;
        }

        public async Task<bool> DropAllTablesAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";
                var connectionString = baseConnectionString.Replace($"Database={baseDatabaseName}", $"Database={tenantDbName}");

                _logger.LogInformation("Dropping all tables in tenant database: {TenantDatabase}", tenantDbName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Get all tables first
                var tables = await GetExistingTablesInDatabaseAsync(tenantDbName);

                // Drop all tables
                foreach (var table in tables)
                {
                    var dropCommand = new MySqlCommand($"DROP TABLE IF EXISTS `{table}`", connection);
                    await dropCommand.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Successfully dropped {TableCount} tables from tenant database: {TenantDatabase}", 
                    tables.Count, tenantDbName);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to drop tables for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        private List<string> GetCreateTableStatements()
        {
            return new List<string>
            {
                // Core Entity Tables
                @"CREATE TABLE IF NOT EXISTS `Users` (
                    `UserName` varchar(255) NOT NULL,
                    `FullName` varchar(255) DEFAULT NULL,
                    `UserEmail` varchar(255) DEFAULT NULL,
                    `Password` varchar(255) DEFAULT NULL,
                    `admin` tinyint(1) NOT NULL DEFAULT 0,
                    PRIMARY KEY (`UserName`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Assets` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Description` varchar(1000) DEFAULT NULL,
                    `Url` varchar(2000) DEFAULT NULL,
                    `Src` varchar(500) DEFAULT NULL,
                    `Filetype` varchar(100) DEFAULT NULL,
                    `Type` int NOT NULL DEFAULT 0 COMMENT 'AssetType enum: 0=Image, 1=PDF, 2=Video, 3=Unity',
                    `Filename` varchar(255) NOT NULL,
                    `AiAvailable` varchar(20) DEFAULT 'notready' COMMENT 'AI processing status: ready, process, notready',
                    `JobId` varchar(255) DEFAULT NULL COMMENT 'Chatbot API job ID for AI processing',
                    PRIMARY KEY (`Id`),
                    INDEX `idx_ai_available` (`AiAvailable`)
                )",

                @"CREATE TABLE IF NOT EXISTS `TrainingPrograms` (
                    `id` int NOT NULL AUTO_INCREMENT,
                    `Name` varchar(255) NOT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `Requirements` varchar(1000) DEFAULT NULL,
                    `Objectives` varchar(1000) DEFAULT NULL,
                    `Created_at` varchar(255) DEFAULT NULL,
                    `min_level_rank` int DEFAULT NULL,
                    `max_level_rank` int DEFAULT NULL,
                    `required_upto_level_rank` int DEFAULT NULL,
                    PRIMARY KEY (`id`)
                )",

                @"CREATE TABLE IF NOT EXISTS `LearningPaths` (
                    `id` int NOT NULL AUTO_INCREMENT,
                    `Description` varchar(1000) NOT NULL,
                    `LearningPathName` varchar(255) NOT NULL,
                    PRIMARY KEY (`id`)
                )",

        // Replace the Materials table creation in GetCreateTableStatements() method

        @"CREATE TABLE IF NOT EXISTS `Materials` (
            `id` int NOT NULL AUTO_INCREMENT,
            `Description` varchar(1000) DEFAULT NULL,
            `Name` varchar(255) DEFAULT NULL,
            `Created_at` datetime DEFAULT NULL,
            `Updated_at` datetime DEFAULT NULL,
            `Type` int NOT NULL,
            `Discriminator` varchar(50) NOT NULL,
            `Unique_id` int DEFAULT NULL,

            -- MQTT_TemplateMaterial specific columns
            `message_type` varchar(255) DEFAULT NULL,
            `message_text` text DEFAULT NULL,

            -- Asset-based materials (Video, Image, Unity, Default, PDF)
            `AssetId` int DEFAULT NULL,

            -- Video-specific columns
            `VideoPath` varchar(500) DEFAULT NULL,
            `VideoDuration` int DEFAULT NULL,
            `VideoResolution` varchar(20) DEFAULT NULL,
            `startTime` varchar(50) DEFAULT NULL,
            `Annotations` json DEFAULT NULL,

            -- Image-specific columns
            `ImagePath` varchar(500) DEFAULT NULL,
            `ImageWidth` int DEFAULT NULL,
            `ImageHeight` int DEFAULT NULL,
            `ImageFormat` varchar(20) DEFAULT NULL,

            -- PDF-specific columns
            `PdfPath` varchar(500) DEFAULT NULL,
            `PdfPageCount` int DEFAULT NULL,
            `PdfFileSize` bigint DEFAULT NULL,

            -- Chatbot-specific columns
            `ChatbotConfig` text DEFAULT NULL,
            `ChatbotModel` varchar(100) DEFAULT NULL,
            `ChatbotPrompt` text DEFAULT NULL,

            -- Questionnaire-specific columns
            `QuestionnaireConfig` text DEFAULT NULL,
            `QuestionnaireType` varchar(50) DEFAULT NULL,
            `PassingScore` decimal(5,2) DEFAULT NULL,

            -- Unity Demo specific columns
            `UnityVersion` varchar(50) DEFAULT NULL,
            `UnityBuildTarget` varchar(50) DEFAULT NULL,
            `UnitySceneName` varchar(255) DEFAULT NULL,
            `UnityJson` text DEFAULT NULL,

            -- Voice Material specific columns
            `ServiceJobId` varchar(255) DEFAULT NULL,
            `VoiceStatus` varchar(20) DEFAULT 'notready',
            `VoiceAssetIds` text DEFAULT NULL,

            PRIMARY KEY (`id`),
            INDEX `idx_discriminator` (`Discriminator`),
            INDEX `idx_type` (`Type`),
            INDEX `idx_unique_id` (`Unique_id`),
            INDEX `idx_asset_id` (`AssetId`),
            INDEX `idx_video_path` (`VideoPath`),
            INDEX `idx_image_path` (`ImagePath`),
            INDEX `idx_pdf_path` (`PdfPath`)
        )",

                // Updated table creation statements with proper foreign keys
                @"CREATE TABLE IF NOT EXISTS `Timestamps` (
                    `id` int NOT NULL AUTO_INCREMENT,
                    `Title` varchar(255) NOT NULL,
                    `startTime` varchar(50) NOT NULL,
                    `endTime` varchar(50) DEFAULT NULL,
                    `Duration` int DEFAULT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `type` varchar(255) DEFAULT NULL,
                    `VideoMaterialId` int DEFAULT NULL,
                    PRIMARY KEY (`id`),
                    INDEX `idx_video_material` (`VideoMaterialId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Entries` (
                    `ChecklistEntryId` int NOT NULL AUTO_INCREMENT,
                    `Text` varchar(1000) NOT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `ChecklistMaterialId` int DEFAULT NULL,
                    PRIMARY KEY (`ChecklistEntryId`),
                    INDEX `idx_checklist_material` (`ChecklistMaterialId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `WorkflowSteps` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Title` varchar(255) NOT NULL,
                    `Content` text DEFAULT NULL,
                    `WorkflowMaterialId` int DEFAULT NULL,
                    PRIMARY KEY (`Id`),
                    INDEX `idx_workflow_material` (`WorkflowMaterialId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `QuizQuestions` (
                    `QuizQuestionId` int NOT NULL AUTO_INCREMENT,
                    `QuestionNumber` int NOT NULL,
                    `QuestionType` varchar(50) NOT NULL DEFAULT 'text',
                    `Text` varchar(2000) NOT NULL,
                    `Description` varchar(2000) DEFAULT NULL,
                    `Score` decimal(10,2) DEFAULT NULL,
                    `HelpText` varchar(1000) DEFAULT NULL,
                    `AllowMultiple` tinyint(1) NOT NULL DEFAULT 0,
                    `ScaleConfig` varchar(500) DEFAULT NULL,
                    `QuizMaterialId` int DEFAULT NULL,
                    PRIMARY KEY (`QuizQuestionId`),
                    INDEX `idx_quiz_material` (`QuizMaterialId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `QuizAnswers` (
                    `QuizAnswerId` int NOT NULL AUTO_INCREMENT,
                    `Text` varchar(1000) NOT NULL,
                    `CorrectAnswer` tinyint(1) NOT NULL DEFAULT 0,
                    `DisplayOrder` int DEFAULT NULL,
                    `Extra` varchar(500) DEFAULT NULL,
                    `QuizQuestionId` int DEFAULT NULL,
                    PRIMARY KEY (`QuizAnswerId`),
                    INDEX `idx_quiz_question` (`QuizQuestionId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `QuestionnaireEntries` (
                    `QuestionnaireEntryId` int NOT NULL AUTO_INCREMENT,
                    `Text` varchar(1000) NOT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `QuestionnaireMaterialId` int DEFAULT NULL,
                    PRIMARY KEY (`QuestionnaireEntryId`),
                    INDEX `idx_questionnaire_material` (`QuestionnaireMaterialId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `ImageAnnotations` (
                    `ImageAnnotationId` int NOT NULL AUTO_INCREMENT,
                    `ClientId` varchar(50) DEFAULT NULL,
                    `Text` varchar(2000) DEFAULT NULL,
                    `FontSize` int DEFAULT NULL,
                    `X` double NOT NULL DEFAULT 0,
                    `Y` double NOT NULL DEFAULT 0,
                    `ImageMaterialId` int DEFAULT NULL,
                    PRIMARY KEY (`ImageAnnotationId`),
                    INDEX `idx_image_material` (`ImageMaterialId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Shares` (
                    `ShareId` varchar(50) NOT NULL,
                    `FileId` varchar(50) DEFAULT NULL,
                    `Type` int NOT NULL,
                    `Target` varchar(255) NOT NULL,
                    PRIMARY KEY (`ShareId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Groups` (
                    `GroupName` varchar(255) NOT NULL,
                    `TenantName` varchar(255) DEFAULT NULL,
                    PRIMARY KEY (`GroupName`)
                )",

                @"CREATE TABLE IF NOT EXISTS `TenantDirectories` (
                    `TenantPath` varchar(500) NOT NULL,
                    `TenantName` varchar(255) DEFAULT NULL,
                    PRIMARY KEY (`TenantPath`)
                )",

                @"CREATE TABLE IF NOT EXISTS `Tenants` (
                    `TenantName` varchar(255) NOT NULL,
                    `TenantGroup` varchar(255) DEFAULT NULL,
                    `TenantSchema` varchar(255) DEFAULT NULL,
                    `Description` varchar(1000) DEFAULT NULL,
                    `TenantDirectory` varchar(500) DEFAULT NULL,
                    `OwnerName` varchar(255) DEFAULT NULL,
                    PRIMARY KEY (`TenantName`)
                )",

                // Junction Tables for Many-to-Many Relationships
                @"CREATE TABLE IF NOT EXISTS `ProgramMaterials` (
                    `TrainingProgramId` int NOT NULL,
                    `MaterialId` int NOT NULL,
                    `inherit_from_program` tinyint(1) DEFAULT 1,
                    `min_level_rank` int DEFAULT NULL,
                    `max_level_rank` int DEFAULT NULL,
                    `required_upto_level_rank` int DEFAULT NULL,
                    PRIMARY KEY (`TrainingProgramId`, `MaterialId`),
                    INDEX `idx_program` (`TrainingProgramId`),
                    INDEX `idx_material` (`MaterialId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `ProgramLearningPaths` (
                    `TrainingProgramId` int NOT NULL,
                    `LearningPathId` int NOT NULL,
                    `inherit_from_program` tinyint(1) DEFAULT 1,
                    `min_level_rank` int DEFAULT NULL,
                    `max_level_rank` int DEFAULT NULL,
                    `required_upto_level_rank` int DEFAULT NULL,
                    PRIMARY KEY (`TrainingProgramId`, `LearningPathId`),
                    INDEX `idx_program` (`TrainingProgramId`),
                    INDEX `idx_path` (`LearningPathId`)
                )",

                @"CREATE TABLE IF NOT EXISTS `GroupUsers` (
                    `GroupName` varchar(255) NOT NULL,
                    `UserName` varchar(255) NOT NULL,
                    PRIMARY KEY (`GroupName`, `UserName`),
                    INDEX `idx_group` (`GroupName`),
                    INDEX `idx_user` (`UserName`)
                )",

                @"CREATE TABLE IF NOT EXISTS `TenantAdmins` (
                    `TenantName` varchar(255) NOT NULL,
                    `UserName` varchar(255) NOT NULL,
                    PRIMARY KEY (`TenantName`, `UserName`),
                    INDEX `idx_tenant` (`TenantName`),
                    INDEX `idx_user` (`UserName`)
                )",

                // Complex Material Relationships Table
                @"CREATE TABLE IF NOT EXISTS `MaterialRelationships` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `MaterialId` int NOT NULL,
                    `RelatedEntityId` varchar(50) NOT NULL,
                    `RelatedEntityType` varchar(50) NOT NULL,
                    `RelationshipType` varchar(50) DEFAULT NULL,
                    `DisplayOrder` int DEFAULT NULL,
                    PRIMARY KEY (`Id`),
                    INDEX `idx_id` (`MaterialId`),
                    INDEX `idx_related_entity` (`RelatedEntityId`, `RelatedEntityType`),
                    INDEX `idx_relationship_type` (`RelationshipType`)
                )",

                // Subcomponent-to-Material Relationships Table
                @"CREATE TABLE IF NOT EXISTS `SubcomponentMaterialRelationships` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `SubcomponentId` int NOT NULL,
                    `SubcomponentType` varchar(50) NOT NULL,
                    `RelatedMaterialId` int NOT NULL,
                    `RelationshipType` varchar(50) DEFAULT NULL,
                    `DisplayOrder` int DEFAULT NULL,
                    PRIMARY KEY (`Id`),
                    INDEX `idx_subcomponent` (`SubcomponentId`, `SubcomponentType`),
                    INDEX `idx_material` (`RelatedMaterialId`),
                    UNIQUE INDEX `idx_unique_relationship` (`SubcomponentId`, `SubcomponentType`, `RelatedMaterialId`),
                    CONSTRAINT `fk_subcomponent_material` FOREIGN KEY (`RelatedMaterialId`) REFERENCES `Materials` (`id`) ON DELETE CASCADE
                )",

                // User Material Data - stores raw answer submissions (historical data)
                @"CREATE TABLE IF NOT EXISTS `UserMaterialData` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `UserId` varchar(255) NOT NULL,
                    `ProgramId` int DEFAULT NULL,
                    `MaterialId` int NOT NULL,
                    `Data` json DEFAULT NULL,
                    `CreatedAt` datetime NOT NULL,
                    `UpdatedAt` datetime NOT NULL,
                    PRIMARY KEY (`Id`),
                    INDEX `idx_user_id` (`UserId`),
                    INDEX `idx_material_id` (`MaterialId`),
                    INDEX `idx_program_id` (`ProgramId`),
                    UNIQUE INDEX `idx_user_material` (`UserId`, `MaterialId`)
                )",

                // User Material Scores - cached scores for quick lookups
                @"CREATE TABLE IF NOT EXISTS `UserMaterialScores` (
                    `UserId` varchar(255) NOT NULL,
                    `ProgramId` int DEFAULT NULL,
                    `MaterialId` int NOT NULL,
                    `Score` decimal(10,2) NOT NULL DEFAULT 0,
                    `Progress` int NOT NULL DEFAULT 0,
                    `UpdatedAt` datetime NOT NULL,
                    PRIMARY KEY (`UserId`, `MaterialId`),
                    INDEX `idx_program_id` (`ProgramId`),
                    INDEX `idx_material_id` (`MaterialId`)
                )"
            };
        }

        public async Task<bool> MigrateAssetTypeColumnAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

                var connectionString = baseConnectionString.Replace($"Database={baseDatabaseName}", $"Database={tenantDbName}");

                _logger.LogInformation("=== Migrating Asset.Type column for tenant: {TenantName} ===", tenantName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if Type column already exists
                var checkColumnQuery = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @dbName
                    AND TABLE_NAME = 'Assets'
                    AND COLUMN_NAME = 'Type'";

                using (var checkCmd = new MySqlCommand(checkColumnQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                    var columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (columnExists)
                    {
                        _logger.LogInformation("Type column already exists in Assets table for tenant: {TenantName}", tenantName);
                        return true;
                    }
                }

                // Add the Type column with a default value
                var alterTableQuery = @"
                    ALTER TABLE `Assets`
                    ADD COLUMN `Type` int NOT NULL DEFAULT 0
                    COMMENT 'AssetType enum: 0=Image, 1=PDF, 2=Video, 3=Unity'
                    AFTER `Filetype`";

                using (var alterCmd = new MySqlCommand(alterTableQuery, connection))
                {
                    await alterCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Successfully added Type column to Assets table for tenant: {TenantName}", tenantName);
                }

                // Infer Type from Filetype for existing records
                var updateQuery = @"
                    UPDATE `Assets`
                    SET `Type` = CASE
                        WHEN LOWER(`Filetype`) IN ('mp4', 'avi', 'mov', 'wmv', 'flv', 'webm', 'mkv') THEN 2
                        WHEN LOWER(`Filetype`) IN ('pdf') THEN 1
                        WHEN LOWER(`Filetype`) IN ('png', 'jpg', 'jpeg', 'gif', 'bmp', 'svg', 'webp') THEN 0
                        WHEN LOWER(`Filetype`) IN ('unity', 'unitypackage', 'bundle') THEN 3
                        ELSE 0
                    END
                    WHERE `Type` = 0";

                using (var updateCmd = new MySqlCommand(updateQuery, connection))
                {
                    var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Updated {RowCount} existing asset records with inferred Type values for tenant: {TenantName}",
                        rowsAffected, tenantName);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating Asset.Type column for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        public async Task<bool> MigrateAnnotationsColumnsAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={tenantDbName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("=== Migrating Annotations columns for tenant: {TenantName} ===", tenantName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if startTime column exists
                var checkStartTimeQuery = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @dbName
                    AND TABLE_NAME = 'Materials'
                    AND COLUMN_NAME = 'startTime'";

                using (var checkCmd = new MySqlCommand(checkStartTimeQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                    var columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (!columnExists)
                    {
                        _logger.LogInformation("Adding startTime column to Materials table...");
                        var alterTableQuery = @"
                            ALTER TABLE `Materials`
                            ADD COLUMN `startTime` varchar(50) DEFAULT NULL AFTER `VideoResolution`";

                        using (var alterCmd = new MySqlCommand(alterTableQuery, connection))
                        {
                            await alterCmd.ExecuteNonQueryAsync();
                            _logger.LogInformation("Successfully added startTime column");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("startTime column already exists");
                    }
                }

                // Check if Annotations column exists
                var checkAnnotationsQuery = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @dbName
                    AND TABLE_NAME = 'Materials'
                    AND COLUMN_NAME = 'Annotations'";

                using (var checkCmd = new MySqlCommand(checkAnnotationsQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                    var columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (!columnExists)
                    {
                        _logger.LogInformation("Adding Annotations column to Materials table...");
                        var alterTableQuery = @"
                            ALTER TABLE `Materials`
                            ADD COLUMN `Annotations` json DEFAULT NULL AFTER `startTime`";

                        using (var alterCmd = new MySqlCommand(alterTableQuery, connection))
                        {
                            await alterCmd.ExecuteNonQueryAsync();
                            _logger.LogInformation("Successfully added Annotations column");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Annotations column already exists");
                    }
                }

                _logger.LogInformation("=== Migration completed for tenant: {TenantName} ===", tenantName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating Annotations columns for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        public async Task<bool> MigrateSubcomponentMaterialRelationshipsTableAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={tenantDbName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("=== Migrating SubcomponentMaterialRelationships table for tenant: {TenantName} ===", tenantName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if table exists
                var checkTableQuery = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_SCHEMA = @dbName
                    AND TABLE_NAME = 'SubcomponentMaterialRelationships'";

                using (var checkCmd = new MySqlCommand(checkTableQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                    var tableExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (tableExists)
                    {
                        _logger.LogInformation("SubcomponentMaterialRelationships table already exists for tenant: {TenantName}", tenantName);
                        return true;
                    }
                }

                // Create the table
                var createTableQuery = @"
                    CREATE TABLE `SubcomponentMaterialRelationships` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `SubcomponentId` int NOT NULL,
                        `SubcomponentType` varchar(50) NOT NULL,
                        `RelatedMaterialId` int NOT NULL,
                        `RelationshipType` varchar(50) DEFAULT NULL,
                        `DisplayOrder` int DEFAULT NULL,
                        PRIMARY KEY (`Id`),
                        INDEX `idx_subcomponent` (`SubcomponentId`, `SubcomponentType`),
                        INDEX `idx_material` (`RelatedMaterialId`),
                        UNIQUE INDEX `idx_unique_relationship` (`SubcomponentId`, `SubcomponentType`, `RelatedMaterialId`),
                        CONSTRAINT `fk_subcomponent_material` FOREIGN KEY (`RelatedMaterialId`) REFERENCES `Materials` (`id`) ON DELETE CASCADE
                    )";

                using (var createCmd = new MySqlCommand(createTableQuery, connection))
                {
                    await createCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Successfully created SubcomponentMaterialRelationships table for tenant: {TenantName}", tenantName);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating SubcomponentMaterialRelationships table for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        public async Task<bool> MigrateProgramAssignmentRanksAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={tenantDbName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("=== Migrating Program Assignment Rank columns for tenant: {TenantName} ===", tenantName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Migrate ProgramMaterials table
                var columnsToAdd = new[] { "inherit_from_program", "min_level_rank", "max_level_rank", "required_upto_level_rank" };

                foreach (var columnName in columnsToAdd)
                {
                    // Check if column exists in ProgramMaterials
                    var checkPmColumnQuery = @"
                        SELECT COUNT(*)
                        FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = @dbName
                        AND TABLE_NAME = 'ProgramMaterials'
                        AND COLUMN_NAME = @columnName";

                    using (var checkCmd = new MySqlCommand(checkPmColumnQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                        checkCmd.Parameters.AddWithValue("@columnName", columnName);
                        var columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                        if (!columnExists)
                        {
                            _logger.LogInformation("Adding {ColumnName} column to ProgramMaterials table...", columnName);
                            var alterQuery = columnName == "inherit_from_program"
                                ? $"ALTER TABLE `ProgramMaterials` ADD COLUMN `{columnName}` tinyint(1) DEFAULT 1"
                                : $"ALTER TABLE `ProgramMaterials` ADD COLUMN `{columnName}` int DEFAULT NULL";

                            using (var alterCmd = new MySqlCommand(alterQuery, connection))
                            {
                                await alterCmd.ExecuteNonQueryAsync();
                                _logger.LogInformation("Successfully added {ColumnName} column to ProgramMaterials", columnName);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("{ColumnName} column already exists in ProgramMaterials", columnName);
                        }
                    }

                    // Check if column exists in ProgramLearningPaths
                    var checkPlpColumnQuery = @"
                        SELECT COUNT(*)
                        FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = @dbName
                        AND TABLE_NAME = 'ProgramLearningPaths'
                        AND COLUMN_NAME = @columnName";

                    using (var checkCmd = new MySqlCommand(checkPlpColumnQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                        checkCmd.Parameters.AddWithValue("@columnName", columnName);
                        var columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                        if (!columnExists)
                        {
                            _logger.LogInformation("Adding {ColumnName} column to ProgramLearningPaths table...", columnName);
                            var alterQuery = columnName == "inherit_from_program"
                                ? $"ALTER TABLE `ProgramLearningPaths` ADD COLUMN `{columnName}` tinyint(1) DEFAULT 1"
                                : $"ALTER TABLE `ProgramLearningPaths` ADD COLUMN `{columnName}` int DEFAULT NULL";

                            using (var alterCmd = new MySqlCommand(alterQuery, connection))
                            {
                                await alterCmd.ExecuteNonQueryAsync();
                                _logger.LogInformation("Successfully added {ColumnName} column to ProgramLearningPaths", columnName);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("{ColumnName} column already exists in ProgramLearningPaths", columnName);
                        }
                    }
                }

                _logger.LogInformation("=== Migration completed for tenant: {TenantName} ===", tenantName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating Program Assignment Rank columns for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        /// <summary>
        /// Migrates the QuizAnswers table to:
        /// 1. Rename IsCorrect column to CorrectAnswer
        /// 2. Add Extra column (varchar 500, nullable)
        /// </summary>
        public async Task<bool> MigrateQuizAnswersTableAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={tenantDbName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("=== Migrating QuizAnswers table for tenant: {TenantName} ===", tenantName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if QuizAnswers table exists
                var checkTableQuery = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_SCHEMA = @dbName
                    AND TABLE_NAME = 'QuizAnswers'";

                using (var checkCmd = new MySqlCommand(checkTableQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                    var tableExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (!tableExists)
                    {
                        _logger.LogInformation("QuizAnswers table does not exist, skipping migration");
                        return true;
                    }
                }

                // Step 1: Check if IsCorrect column exists (needs rename to CorrectAnswer)
                var checkIsCorrectQuery = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @dbName
                    AND TABLE_NAME = 'QuizAnswers'
                    AND COLUMN_NAME = 'IsCorrect'";

                using (var checkCmd = new MySqlCommand(checkIsCorrectQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                    var columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (columnExists)
                    {
                        _logger.LogInformation("Renaming IsCorrect column to CorrectAnswer...");
                        var renameQuery = "ALTER TABLE `QuizAnswers` CHANGE COLUMN `IsCorrect` `CorrectAnswer` tinyint(1) NOT NULL DEFAULT 0";
                        using (var renameCmd = new MySqlCommand(renameQuery, connection))
                        {
                            await renameCmd.ExecuteNonQueryAsync();
                            _logger.LogInformation("Successfully renamed IsCorrect to CorrectAnswer");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("IsCorrect column does not exist (may already be renamed to CorrectAnswer)");
                    }
                }

                // Step 2: Add Extra column if it doesn't exist
                var checkExtraQuery = @"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @dbName
                    AND TABLE_NAME = 'QuizAnswers'
                    AND COLUMN_NAME = 'Extra'";

                using (var checkCmd = new MySqlCommand(checkExtraQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                    var columnExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (!columnExists)
                    {
                        _logger.LogInformation("Adding Extra column to QuizAnswers table...");
                        var addColumnQuery = "ALTER TABLE `QuizAnswers` ADD COLUMN `Extra` varchar(500) DEFAULT NULL";
                        using (var addCmd = new MySqlCommand(addColumnQuery, connection))
                        {
                            await addCmd.ExecuteNonQueryAsync();
                            _logger.LogInformation("Successfully added Extra column to QuizAnswers");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Extra column already exists in QuizAnswers");
                    }
                }

                _logger.LogInformation("=== QuizAnswers table migration completed for tenant: {TenantName} ===", tenantName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating QuizAnswers table for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        /// <summary>
        /// Migrates existing databases to add Voice Material and Asset AI processing columns.
        /// </summary>
        public async Task<bool> MigrateVoiceAndAiColumnsAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={tenantDbName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("=== Starting Voice/AI columns migration for tenant: {TenantName} ===", tenantName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Add Voice Material columns to Materials table
                var voiceColumns = new Dictionary<string, string>
                {
                    { "ServiceJobId", "ALTER TABLE `Materials` ADD COLUMN `ServiceJobId` varchar(255) DEFAULT NULL" },
                    { "VoiceStatus", "ALTER TABLE `Materials` ADD COLUMN `VoiceStatus` varchar(20) DEFAULT 'notready'" },
                    { "VoiceAssetIds", "ALTER TABLE `Materials` ADD COLUMN `VoiceAssetIds` text DEFAULT NULL" }
                };

                foreach (var column in voiceColumns)
                {
                    var checkQuery = @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = @dbName
                        AND TABLE_NAME = 'Materials'
                        AND COLUMN_NAME = @columnName";

                    using (var checkCmd = new MySqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                        checkCmd.Parameters.AddWithValue("@columnName", column.Key);
                        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                        if (!exists)
                        {
                            _logger.LogInformation("Adding {Column} column to Materials table...", column.Key);
                            using (var addCmd = new MySqlCommand(column.Value, connection))
                            {
                                await addCmd.ExecuteNonQueryAsync();
                                _logger.LogInformation("Successfully added {Column} column to Materials", column.Key);
                            }
                        }
                    }
                }

                // Add AI columns to Assets table
                var assetColumns = new Dictionary<string, string>
                {
                    { "AiAvailable", "ALTER TABLE `Assets` ADD COLUMN `AiAvailable` varchar(20) DEFAULT 'notready'" },
                    { "JobId", "ALTER TABLE `Assets` ADD COLUMN `JobId` varchar(255) DEFAULT NULL" }
                };

                foreach (var column in assetColumns)
                {
                    var checkQuery = @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = @dbName
                        AND TABLE_NAME = 'Assets'
                        AND COLUMN_NAME = @columnName";

                    using (var checkCmd = new MySqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                        checkCmd.Parameters.AddWithValue("@columnName", column.Key);
                        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                        if (!exists)
                        {
                            _logger.LogInformation("Adding {Column} column to Assets table...", column.Key);
                            using (var addCmd = new MySqlCommand(column.Value, connection))
                            {
                                await addCmd.ExecuteNonQueryAsync();
                                _logger.LogInformation("Successfully added {Column} column to Assets", column.Key);
                            }
                        }
                    }
                }

                // Add index on AiAvailable for faster lookups
                var indexCheckQuery = @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = @dbName
                    AND TABLE_NAME = 'Assets'
                    AND INDEX_NAME = 'idx_ai_available'";

                using (var checkCmd = new MySqlCommand(indexCheckQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@dbName", tenantDbName);
                    var indexExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                    if (!indexExists)
                    {
                        _logger.LogInformation("Adding idx_ai_available index to Assets table...");
                        using (var addCmd = new MySqlCommand("CREATE INDEX `idx_ai_available` ON `Assets` (`AiAvailable`)", connection))
                        {
                            await addCmd.ExecuteNonQueryAsync();
                            _logger.LogInformation("Successfully added idx_ai_available index to Assets");
                        }
                    }
                }

                _logger.LogInformation("=== Voice/AI columns migration completed for tenant: {TenantName} ===", tenantName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating Voice/AI columns for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        /// <summary>
        /// Migrates existing databases to add UserMaterialData and UserMaterialScores tables.
        /// </summary>
        public async Task<bool> MigrateUserMaterialTablesAsync(string tenantName)
        {
            try
            {
                var tenantDbName = _tenantService.GetTenantSchema(tenantName);
                var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
                var baseDatabaseName = _configuration["BaseDatabaseName"] ?? "magical_library";

                var connectionString = baseConnectionString.Replace($"database={baseDatabaseName}", $"database={tenantDbName}", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("=== Migrating User Material tables for tenant: {TenantName} ===", tenantName);

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                // Create UserMaterialData table if not exists
                var createUserMaterialDataQuery = @"
                    CREATE TABLE IF NOT EXISTS `UserMaterialData` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `UserId` varchar(255) NOT NULL,
                        `ProgramId` int DEFAULT NULL,
                        `MaterialId` int NOT NULL,
                        `Data` json DEFAULT NULL,
                        `CreatedAt` datetime NOT NULL,
                        `UpdatedAt` datetime NOT NULL,
                        PRIMARY KEY (`Id`),
                        INDEX `idx_user_id` (`UserId`),
                        INDEX `idx_material_id` (`MaterialId`),
                        INDEX `idx_program_id` (`ProgramId`),
                        UNIQUE INDEX `idx_user_material` (`UserId`, `MaterialId`)
                    )";

                using (var createCmd = new MySqlCommand(createUserMaterialDataQuery, connection))
                {
                    await createCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Created/verified UserMaterialData table for tenant: {TenantName}", tenantName);
                }

                // Create UserMaterialScores table if not exists
                var createUserMaterialScoresQuery = @"
                    CREATE TABLE IF NOT EXISTS `UserMaterialScores` (
                        `UserId` varchar(255) NOT NULL,
                        `ProgramId` int DEFAULT NULL,
                        `MaterialId` int NOT NULL,
                        `Score` decimal(10,2) NOT NULL DEFAULT 0,
                        `Progress` int NOT NULL DEFAULT 0,
                        `UpdatedAt` datetime NOT NULL,
                        PRIMARY KEY (`UserId`, `MaterialId`),
                        INDEX `idx_program_id` (`ProgramId`),
                        INDEX `idx_material_id` (`MaterialId`)
                    )";

                using (var createCmd = new MySqlCommand(createUserMaterialScoresQuery, connection))
                {
                    await createCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Created/verified UserMaterialScores table for tenant: {TenantName}", tenantName);
                }

                _logger.LogInformation("=== User Material tables migration completed for tenant: {TenantName} ===", tenantName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating User Material tables for tenant: {TenantName}", tenantName);
                return false;
            }
        }

        private string GetCreateTablesScript()
        {
            return @"
CREATE TABLE IF NOT EXISTS `Users` (
    `UserName` varchar(50) NOT NULL,
    `FullName` varchar(100) NOT NULL,
    `UserEmail` varchar(255) DEFAULT NULL,
    `Password` varchar(50) DEFAULT NULL,
    `admin` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`UserName`)
)

CREATE TABLE IF NOT EXISTS `TrainingPrograms` (
    `Id` int NOT NULL AUTOICREMENT,
    `Name` varchar(255) NOT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    `Requirements` varchar(1000) DEFAULT NULL,
    `Objectives` varchar(1000) DEFAULT NULL,
    PRIMARY KEY (`TrainingProgramId`)
)

CREATE TABLE IF NOT EXISTS `LearningPaths` (
    `LearningPathId` varchar(50) NOT NULL,
    `PathName` varchar(255) NOT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    PRIMARY KEY (`LearningPathId`)
)

CREATE TABLE IF NOT EXISTS `Materials` (
    `MaterialId` varchar(50) NOT NULL,
    `MaterialName` varchar(255) NOT NULL,
    `MaterialType` varchar(50) DEFAULT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    `Discriminator` varchar(255) NOT NULL,
    `VideoPath` varchar(500) DEFAULT NULL,
    `ImagePath` varchar(500) DEFAULT NULL,
    PRIMARY KEY (`MaterialId`)
)

CREATE TABLE IF NOT EXISTS `Assets` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Description` varchar(1000) DEFAULT NULL,
    `Url` varchar(2000) DEFAULT NULL,
    `Src` varchar(500) DEFAULT NULL,
    `Filetype` varchar(100) DEFAULT NULL,
    `Type` int NOT NULL DEFAULT 0 COMMENT 'AssetType enum: 0=Image, 1=PDF, 2=Video, 3=Unity',
    `Filename` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`)
)

CREATE TABLE IF NOT EXISTS `Shares` (
    `ShareId` varchar(50) NOT NULL,
    `ShareType` varchar(50) DEFAULT NULL,
    PRIMARY KEY (`ShareId`)
)

// Updated table creation statements with proper foreign keys
CREATE TABLE IF NOT EXISTS `Timestamps` (
    `id` int NOT NULL AUTO_INCREMENT,
    `Title` varchar(255) NOT NULL,
    `startTime` varchar(50) NOT NULL,
    `endTime` varchar(50) DEFAULT NULL,
    `Duration` int DEFAULT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    `type` varchar(255) DEFAULT NULL,
    `VideoMaterialId` int DEFAULT NULL,
    PRIMARY KEY (`id`),
    INDEX `idx_video_material` (`VideoMaterialId`)
)

CREATE TABLE IF NOT EXISTS `Entries` (
    `ChecklistEntryId` int NOT NULL AUTO_INCREMENT,
    `Text` varchar(1000) NOT NULL,
    `Description` varchar(1000) DEFAULT NULL,
    `ChecklistMaterialId` int DEFAULT NULL,
    PRIMARY KEY (`ChecklistEntryId`),
    INDEX `idx_checklist_material` (`ChecklistMaterialId`)
)

CREATE TABLE IF NOT EXISTS `QuizQuestions` (
    `QuizQuestionId` int NOT NULL AUTO_INCREMENT,
    `QuestionNumber` int NOT NULL,
    `QuestionType` varchar(50) NOT NULL DEFAULT 'text',
    `Text` varchar(2000) NOT NULL,
    `Description` varchar(2000) DEFAULT NULL,
    `Score` decimal(10,2) DEFAULT NULL,
    `HelpText` varchar(1000) DEFAULT NULL,
    `AllowMultiple` tinyint(1) NOT NULL DEFAULT 0,
    `ScaleConfig` varchar(500) DEFAULT NULL,
    `QuizMaterialId` int DEFAULT NULL,
    PRIMARY KEY (`QuizQuestionId`),
    INDEX `idx_quiz_material` (`QuizMaterialId`)
)

CREATE TABLE IF NOT EXISTS `QuizAnswers` (
    `QuizAnswerId` int NOT NULL AUTO_INCREMENT,
    `Text` varchar(1000) NOT NULL,
    `CorrectAnswer` tinyint(1) NOT NULL DEFAULT 0,
    `DisplayOrder` int DEFAULT NULL,
    `Extra` varchar(500) DEFAULT NULL,
    `QuizQuestionId` int DEFAULT NULL,
    PRIMARY KEY (`QuizAnswerId`),
    INDEX `idx_quiz_question` (`QuizQuestionId`)
)

CREATE TABLE IF NOT EXISTS `WorkflowSteps` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Title` varchar(255) NOT NULL,
    `Content` text DEFAULT NULL,
    `WorkflowMaterialId` int DEFAULT NULL,
    PRIMARY KEY (`Id`),
    INDEX `idx_workflow_material` (`WorkflowMaterialId`)
)

CREATE TABLE IF NOT EXISTS `Tenants` (
    `TenantName` varchar(100) NOT NULL,
    `TenantGroup` varchar(100) DEFAULT NULL,
    `Description` varchar(500) DEFAULT NULL,
    `TenantDirectory` varchar(500) DEFAULT NULL,
    `OwnerName` varchar(255) DEFAULT NULL,
    `TenantSchema` varchar(255) DEFAULT NULL,
    PRIMARY KEY (`TenantName`)
)";
        }
    }
}