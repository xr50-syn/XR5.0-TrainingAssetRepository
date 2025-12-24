using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XR50TrainingAssetRepo.Models
{
    public class ChecklistEntry
    {
        [Key]

        public int ChecklistEntryId { get; set; }

        public string Text { get; set; } = null!;
        public string? Description { get; set; }
        public int? ChecklistMaterialId { get; set; }
        public ChecklistEntry()
        {
            
        }


    }
}
