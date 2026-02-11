using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XR50TrainingAssetRepo.Models
{
    public class QuestionnaireEntry
    {
        [Key]

        public int QuestionnaireEntryId { get; set; }

        public string Text { get; set; } = null!;
        public string? Description { get; set; }

        [Required]
        public int QuestionnaireMaterialId { get; set; }
        public QuestionnaireEntry()
        {
           
        }


    }
}
