using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace XR50TrainingAssetRepo.Models
{
    public class LearningPath
    {
        [Key]
        public int id { get; set; }
        public string Description { get; set; }
        public string LearningPathName { get; set; }
        [JsonIgnore]
        public virtual ICollection<ProgramLearningPath> ProgramLearningPaths { get; set; } = new List<ProgramLearningPath>();
        public LearningPath()
        {

        }
    }

}