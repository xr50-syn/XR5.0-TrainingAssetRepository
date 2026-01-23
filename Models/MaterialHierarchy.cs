namespace XR50TrainingAssetRepo.Models
{
    public class MaterialHierarchy
    {
        public Material RootMaterial { get; set; }
        public List<MaterialHierarchyNode> Children { get; set; } = new();
        public int TotalDepth { get; set; }
        public int TotalMaterials { get; set; }
    }

    public class MaterialHierarchyNode
    {
        public Material Material { get; set; }
        public string RelationshipType { get; set; }
        public int? DisplayOrder { get; set; }
        public List<MaterialHierarchyNode> Children { get; set; } = new();
        public int Depth { get; set; }
    }
}
