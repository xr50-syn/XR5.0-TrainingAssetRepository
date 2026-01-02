using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Questionnaire material-specific operations including entries.
    /// </summary>
    public class QuestionnaireMaterialService : IQuestionnaireMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<QuestionnaireMaterialService> _logger;

        public QuestionnaireMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<QuestionnaireMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Questionnaire Material CRUD

        public async Task<IEnumerable<QuestionnaireMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<QuestionnaireMaterial>()
                .ToListAsync();
        }

        public async Task<QuestionnaireMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<QuestionnaireMaterial>()
                .FirstOrDefaultAsync(q => q.id == id);
        }

        public async Task<QuestionnaireMaterial?> GetWithEntriesAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<QuestionnaireMaterial>()
                .Include(q => q.QuestionnaireEntries)
                .FirstOrDefaultAsync(q => q.id == id);
        }

        public async Task<QuestionnaireMaterial> CreateAsync(QuestionnaireMaterial questionnaire)
        {
            using var context = _dbContextFactory.CreateDbContext();

            questionnaire.Created_at = DateTime.UtcNow;
            questionnaire.Updated_at = DateTime.UtcNow;
            questionnaire.Type = MaterialType.Questionnaire;

            context.Materials.Add(questionnaire);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created questionnaire material: {Name} with ID: {Id}", questionnaire.Name, questionnaire.id);

            return questionnaire;
        }

        public async Task<QuestionnaireMaterial> CreateWithEntriesAsync(QuestionnaireMaterial questionnaire, IEnumerable<QuestionnaireEntry>? entries = null)
        {
            using var context = _dbContextFactory.CreateDbContext();

            questionnaire.Created_at = DateTime.UtcNow;
            questionnaire.Updated_at = DateTime.UtcNow;
            questionnaire.Type = MaterialType.Questionnaire;

            context.Materials.Add(questionnaire);
            await context.SaveChangesAsync();

            if (entries != null && entries.Any())
            {
                foreach (var entry in entries)
                {
                    entry.QuestionnaireEntryId = 0;
                    entry.QuestionnaireMaterialId = questionnaire.id;
                    context.QuestionnaireEntries.Add(entry);
                }
                await context.SaveChangesAsync();

                _logger.LogInformation("Added {EntryCount} initial entries to questionnaire {QuestionnaireId}",
                    entries.Count(), questionnaire.id);
            }

            _logger.LogInformation("Created questionnaire material: {Name} with ID: {Id}", questionnaire.Name, questionnaire.id);

            return questionnaire;
        }

        public async Task<QuestionnaireMaterial> UpdateAsync(QuestionnaireMaterial questionnaire)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var existing = await context.Materials
                    .OfType<QuestionnaireMaterial>()
                    .FirstOrDefaultAsync(q => q.id == questionnaire.id);

                if (existing == null)
                {
                    throw new KeyNotFoundException($"Questionnaire material {questionnaire.id} not found");
                }

                var createdAt = existing.Created_at;
                var uniqueId = existing.Unique_id;

                // Delete existing entries
                var existingEntries = await context.QuestionnaireEntries
                    .Where(e => e.QuestionnaireMaterialId == questionnaire.id)
                    .ToListAsync();
                context.QuestionnaireEntries.RemoveRange(existingEntries);

                context.Materials.Remove(existing);
                await context.SaveChangesAsync();

                questionnaire.id = existing.id;
                questionnaire.Created_at = createdAt;
                questionnaire.Updated_at = DateTime.UtcNow;
                questionnaire.Unique_id = uniqueId;
                questionnaire.Type = MaterialType.Questionnaire;

                context.Materials.Add(questionnaire);
                await context.SaveChangesAsync();

                if (questionnaire.QuestionnaireEntries?.Any() == true)
                {
                    foreach (var entry in questionnaire.QuestionnaireEntries)
                    {
                        var newEntry = new QuestionnaireEntry
                        {
                            Text = entry.Text,
                            Description = entry.Description,
                            QuestionnaireMaterialId = questionnaire.id
                        };
                        context.QuestionnaireEntries.Add(newEntry);
                    }
                    await context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Updated questionnaire material: {Id} ({Name})", questionnaire.id, questionnaire.Name);

                return questionnaire;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update questionnaire material {Id}", questionnaire.id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var questionnaire = await context.Materials
                .OfType<QuestionnaireMaterial>()
                .FirstOrDefaultAsync(q => q.id == id);

            if (questionnaire == null)
            {
                return false;
            }

            var entries = await context.QuestionnaireEntries
                .Where(e => e.QuestionnaireMaterialId == id)
                .ToListAsync();
            context.QuestionnaireEntries.RemoveRange(entries);

            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            context.Materials.Remove(questionnaire);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted questionnaire material: {Id} with {EntryCount} entries",
                id, entries.Count);

            return true;
        }

        #endregion

        #region Entry Operations

        public async Task<QuestionnaireEntry> AddEntryAsync(int questionnaireId, QuestionnaireEntry entry)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var questionnaire = await context.Materials
                .OfType<QuestionnaireMaterial>()
                .FirstOrDefaultAsync(q => q.id == questionnaireId);

            if (questionnaire == null)
            {
                throw new ArgumentException($"Questionnaire material with ID {questionnaireId} not found");
            }

            entry.QuestionnaireEntryId = 0;
            entry.QuestionnaireMaterialId = questionnaireId;

            context.QuestionnaireEntries.Add(entry);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added entry '{Text}' to questionnaire material {QuestionnaireId}",
                entry.Text, questionnaireId);

            return entry;
        }

        public async Task<bool> RemoveEntryAsync(int questionnaireId, int entryId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var entry = await context.QuestionnaireEntries.FindAsync(entryId);
            if (entry == null || entry.QuestionnaireMaterialId != questionnaireId)
            {
                return false;
            }

            context.QuestionnaireEntries.Remove(entry);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed entry {EntryId} from questionnaire material {QuestionnaireId}",
                entryId, questionnaireId);

            return true;
        }

        public async Task<IEnumerable<QuestionnaireEntry>> GetEntriesAsync(int questionnaireId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.QuestionnaireEntries
                .Where(e => e.QuestionnaireMaterialId == questionnaireId)
                .ToListAsync();
        }

        #endregion
    }
}
