﻿using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Eventing.Reader;

namespace XR5_0TrainingRepo.Models
{
    public class User
    {
        [Key]
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? UserEmail { get; set; }
        public string? Password { get; set; }

        public string AppName { get; set; }

    }

}
