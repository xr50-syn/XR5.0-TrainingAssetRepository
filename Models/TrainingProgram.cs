﻿using Microsoft.EntityFrameworkCore;
using Mono.TextTemplating;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Models
{

    public class TrainingProgram
    {

        public string? Created_at { get; set; }
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public virtual ICollection<ProgramMaterial> ProgramMaterials { get; set; } = new List<ProgramMaterial>();
    public virtual ICollection<ProgramLearningPath> ProgramLearningPaths { get; set; } = new List<ProgramLearningPath>();
        public TrainingProgram()
        {

        }
        // Junction table models

    }
     public class ProgramMaterial
    {
        public int TrainingProgramId { get; set; }
        public int MaterialId { get; set; }
            
        // Navigation properties
        public virtual TrainingProgram TrainingProgram { get; set; }
        public virtual Material Material { get; set; }
    }

    public class ProgramLearningPath
    {
        public int TrainingProgramId { get; set; }
        public int LearningPathId { get; set; }
            
        // Navigation properties
        public virtual TrainingProgram TrainingProgram { get; set; }
        public virtual LearningPath LearningPath { get; set; }
     }

}
