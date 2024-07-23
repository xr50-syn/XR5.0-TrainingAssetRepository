﻿using System.ComponentModel.DataAnnotations;
using XR5_0TrainingRepo.Models;

namespace XR5_0TrainingRepo.Models
{
    public class TrainingModule
    {

        public string? Pilot { get; set; }
        public string? UseCase { get; set; }
        public  List<ResourceManagement> ResourceList{ get; }

        [Key]
        public string? TrainingName { get; set; }
    }

}
