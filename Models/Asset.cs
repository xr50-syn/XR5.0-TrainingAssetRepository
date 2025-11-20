using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using XR50TrainingAssetRepo.Models;

namespace XR50TrainingAssetRepo.Models
{
     public enum ShareType{
        Group,
        User
    }
    public class Share
    {
        [Key]
        public string ShareId { get; set; }
        public string? FileId { get; set; }
        public ShareType Type { get; set;}
        public string Target {get; set;}
        public Share()
        {
            ShareId= Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// High-level asset type categorization matching material types
    /// </summary>
    public enum AssetType
    {
        Image,    // png, jpg, jpeg, gif, etc.
        PDF,      // pdf documents
        Video,    // mp4, avi, mov, etc.
        Unity     // unity bundles and builds
    }

    public class Asset
    {
        public string? Description { get; set; }
        public string? Src { get; set; }

        /// <summary>
        /// Specific file format (e.g., "mp4", "png", "pdf", "jpeg")
        /// </summary>
        public string? Filetype { get; set; }

        /// <summary>
        /// High-level asset category matching material types
        /// </summary>
        [Required]
        public AssetType Type { get; set; }

        public string Filename  { get; set; }
        public string? URL { get; set; }
	    [Key]
        public int Id { get; set; }
        public Asset ()
        {

        }
    }
}
