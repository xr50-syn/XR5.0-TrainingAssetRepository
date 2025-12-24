using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XR50TrainingAssetRepo.Models
{
    public class User
    {
        [Key]
        public string UserName { get; set; } = "";
        
        public string? FullName { get; set; }
        public string? UserEmail { get; set; }
        public string? Password { get; set; }
        public bool admin { get; set; }
    }

    public class Group
    {
        [Key]
        public string GroupName { get; set; } = "";
        
        public string? TenantName { get; set; }
        
        public virtual ICollection<GroupUser> GroupUsers { get; set; } = new List<GroupUser>();
    }
    
    public class GroupUser
    {
        public string GroupName { get; set; } = "";
        public string UserName { get; set; } = "";
        
        // Navigation properties
        public virtual Group Group { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}