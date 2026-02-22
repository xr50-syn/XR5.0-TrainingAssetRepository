using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using MaterialType = XR50TrainingAssetRepo.Models.Type;

namespace XR50TrainingAssetRepo.Services.Materials
{
    /// <summary>
    /// Service for Checklist material-specific operations including entries.
    /// </summary>
    public class ChecklistMaterialService : IChecklistMaterialService
    {
        private readonly IXR50TenantDbContextFactory _dbContextFactory;
        private readonly ILogger<ChecklistMaterialService> _logger;

        public ChecklistMaterialService(
            IXR50TenantDbContextFactory dbContextFactory,
            ILogger<ChecklistMaterialService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        #region Checklist Material CRUD

        public async Task<IEnumerable<ChecklistMaterial>> GetAllAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ChecklistMaterial>()
                .ToListAsync();
        }

        public async Task<ChecklistMaterial?> GetByIdAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ChecklistMaterial>()
                .FirstOrDefaultAsync(c => c.id == id);
        }

        public async Task<ChecklistMaterial?> GetWithEntriesAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();
            return await context.Materials
                .OfType<ChecklistMaterial>()
                .Include(c => c.Entries)
                .FirstOrDefaultAsync(c => c.id == id);
        }

        public async Task<ChecklistMaterial> CreateAsync(ChecklistMaterial checklist)
        {
            using var context = _dbContextFactory.CreateDbContext();

            checklist.Created_at = DateTime.UtcNow;
            checklist.Updated_at = DateTime.UtcNow;
            checklist.Type = MaterialType.Checklist;

            context.Materials.Add(checklist);
            await context.SaveChangesAsync();

            _logger.LogInformation("Created checklist material: {Name} with ID: {Id}", checklist.Name, checklist.id);

            return checklist;
        }

        public async Task<ChecklistMaterial> CreateWithEntriesAsync(ChecklistMaterial checklist, IEnumerable<ChecklistEntry>? entries = null)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                checklist.Created_at = DateTime.UtcNow;
                checklist.Updated_at = DateTime.UtcNow;
                checklist.Type = MaterialType.Checklist;

                context.Materials.Add(checklist);
                await context.SaveChangesAsync();

                if (entries != null && entries.Any())
                {
                    foreach (var entry in entries)
                    {
                        entry.ChecklistEntryId = 0; // Reset ID for new record
                        entry.ChecklistMaterialId = checklist.id;
                        context.Entries.Add(entry);
                    }
                    await context.SaveChangesAsync();

                    _logger.LogInformation("Added {EntryCount} initial entries to checklist {ChecklistId}",
                        entries.Count(), checklist.id);
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Created checklist material: {Name} with ID: {Id}", checklist.Name, checklist.id);

                return checklist;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create checklist material {Name} - Transaction rolled back", checklist.Name);
                throw;
            }
        }

        public async Task<ChecklistMaterial> UpdateAsync(ChecklistMaterial checklist)
        {
            using var context = _dbContextFactory.CreateDbContext();
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var existing = await context.Materials
                    .OfType<ChecklistMaterial>()
                    .FirstOrDefaultAsync(c => c.id == checklist.id);

                if (existing == null)
                {
                    throw new KeyNotFoundException($"Checklist material {checklist.id} not found");
                }

                // Preserve original values
                var createdAt = existing.Created_at;
                var uniqueId = existing.Unique_id;

                // Delete existing entries
                var existingEntries = await context.Entries
                    .Where(e => e.ChecklistMaterialId == checklist.id)
                    .ToListAsync();
                context.Entries.RemoveRange(existingEntries);

                // Remove and re-add the material
                context.Materials.Remove(existing);
                await context.SaveChangesAsync();

                checklist.id = existing.id;
                checklist.Created_at = createdAt;
                checklist.Updated_at = DateTime.UtcNow;
                checklist.Unique_id = uniqueId;
                checklist.Type = MaterialType.Checklist;

                context.Materials.Add(checklist);
                await context.SaveChangesAsync();

                // Re-add entries if present
                if (checklist.Entries?.Any() == true)
                {
                    foreach (var entry in checklist.Entries.ToList())
                    {
                        var newEntry = new ChecklistEntry
                        {
                            Text = entry.Text,
                            Description = entry.Description,
                            ChecklistMaterialId = checklist.id
                        };
                        context.Entries.Add(newEntry);
                    }
                    await context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Updated checklist material: {Id} ({Name})", checklist.id, checklist.Name);

                return await GetWithEntriesAsync(checklist.id) ?? checklist;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update checklist material {Id}", checklist.id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var checklist = await context.Materials
                .OfType<ChecklistMaterial>()
                .FirstOrDefaultAsync(c => c.id == id);

            if (checklist == null)
            {
                return false;
            }

            // Delete entries
            var entries = await context.Entries
                .Where(e => e.ChecklistMaterialId == id)
                .ToListAsync();
            context.Entries.RemoveRange(entries);

            // Delete material relationships
            var relationships = await context.MaterialRelationships
                .Where(mr => mr.MaterialId == id ||
                            (mr.RelatedEntityType == "Material" && mr.RelatedEntityId == id.ToString()))
                .ToListAsync();
            context.MaterialRelationships.RemoveRange(relationships);

            context.Materials.Remove(checklist);
            await context.SaveChangesAsync();

            _logger.LogInformation("Deleted checklist material: {Id} with {EntryCount} entries and {RelationshipCount} relationships",
                id, entries.Count, relationships.Count);

            return true;
        }

        #endregion

        #region Entry Operations

        public async Task<ChecklistEntry> AddEntryAsync(int checklistId, ChecklistEntry entry)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var checklist = await context.Materials
                .OfType<ChecklistMaterial>()
                .FirstOrDefaultAsync(c => c.id == checklistId);

            if (checklist == null)
            {
                throw new ArgumentException($"Checklist material with ID {checklistId} not found");
            }

            entry.ChecklistEntryId = 0;
            entry.ChecklistMaterialId = checklistId;

            context.Entries.Add(entry);
            await context.SaveChangesAsync();

            _logger.LogInformation("Added entry '{Text}' to checklist material {ChecklistId}",
                entry.Text, checklistId);

            return entry;
        }

        public async Task<bool> RemoveEntryAsync(int checklistId, int entryId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            var entry = await context.Entries.FindAsync(entryId);
            if (entry == null || entry.ChecklistMaterialId != checklistId)
            {
                return false;
            }

            context.Entries.Remove(entry);
            await context.SaveChangesAsync();

            _logger.LogInformation("Removed entry {EntryId} from checklist material {ChecklistId}",
                entryId, checklistId);

            return true;
        }

        public async Task<IEnumerable<ChecklistEntry>> GetEntriesAsync(int checklistId)
        {
            using var context = _dbContextFactory.CreateDbContext();

            return await context.Entries
                .Where(e => e.ChecklistMaterialId == checklistId)
                .ToListAsync();
        }

        #endregion
    }
}
