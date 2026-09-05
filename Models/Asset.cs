using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
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
        /// Detected from MIME type or filename if not explicitly provided
        /// </summary>
        public string? Filetype { get; set; }

        /// <summary>
        /// High-level asset category matching material types
        /// Inferred from MIME type or file extension
        /// </summary>
        [Required]
        public AssetType Type { get; set; }

        public string Filename  { get; set; }
        public string? URL { get; set; }

        /// <summary>
        /// SHA-256 hash of an uploaded file. Null for reference-only assets and legacy rows that
        /// predate content hashing. This is internal persistence metadata rather than API input.
        /// </summary>
        [MaxLength(64)]
        [JsonIgnore]
        public string? ContentHash { get; set; }

        /// <summary>
        /// Where this asset's bytes live inside the tenant's storage, relative to the tenant root.
        /// Recorded once when the file is stored and never recomputed, so it is unaffected by any
        /// later change to <see cref="Filename"/> - renaming an asset cannot orphan its file.
        ///
        /// Uploads set this to the content hash, which the unique index on <see cref="ContentHash"/>
        /// keeps unique per tenant. That is what makes one row own exactly one object, so a
        /// same-named upload cannot overwrite another asset and deleting one asset cannot remove a
        /// file another still points at. Null for reference-only assets, which store no file.
        /// </summary>
        [MaxLength(512)]
        [JsonIgnore]
        public string? StorageKey { get; set; }

        /// <summary>
        /// The storage key to actually address this asset by. Falls back to the filename for rows
        /// written before storage keys were recorded, which is where their files were placed.
        /// </summary>
        [NotMapped]
        [JsonIgnore]
        public string ResolvedStorageKey => !string.IsNullOrEmpty(StorageKey) ? StorageKey : Filename;

	    [Key]
        public int Id { get; set; }

        /// <summary>
        /// AI processing availability status: "ready", "process", "notready"
        /// Used for Chatbot AI document processing
        /// </summary>
        public string AiAvailable { get; set; } = "notready";

        /// <summary>
        /// Chatbot API job ID for tracking AI processing status
        /// </summary>
        public string? JobId { get; set; }
    }
}
