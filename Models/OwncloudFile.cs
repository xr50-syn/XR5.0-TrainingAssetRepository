﻿using System.ComponentModel.DataAnnotations;
using XR5_0TrainingRepo.Models;


namespace XR5_0TrainingRepo.Models
{
    public enum ShareType{
        Group,
        User
    }
    public class Share
    {
        [Key]
        public string ShareId { get; set; }
        public ShareType Type { get; set;}
        public string Target {get; set;}
        public Share()
        {
            ShareId= Guid.NewGuid().ToString();
        }
    }
    public class OwncloudFile
    {
        public string TennantName;
        public string? Path { get; set; }
        public string? Description { get; set; }
        public string? OwncloudFileName { get; set; }
        public virtual List<Share>? ShareList { get; set; }
        
        [Key]
        public string FileId { get; set; }
        public OwncloudFile()
        {
            FileId= Guid.NewGuid().ToString();
        }
    }
}
