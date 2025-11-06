using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/[controller]")]
    [ApiController]
    public class LearningPathsController : ControllerBase
    {
        private readonly ILearningPathService _learningPathService;
        private readonly IMaterialService _materialService;
        private readonly ILogger<LearningPathsController> _logger;

        public LearningPathsController(
            ILearningPathService learningPathService,
            IMaterialService materialService,
            ILogger<LearningPathsController> logger)
        {
            _learningPathService = learningPathService;
            _materialService = materialService;
            _logger = logger;
        }

        // GET: api/{tenantName}/learningpaths
        [HttpGet]
        public async Task<ActionResult<IEnumerable<LearningPath>>> GetLearningPaths(string tenantName)
        {
            _logger.LogInformation("Getting learning paths for tenant: {TenantName}", tenantName);

            var learningPaths = await _learningPathService.GetAllLearningPathsAsync();

            _logger.LogInformation("Found {LearningPathCount} learning paths for tenant: {TenantName}",
                learningPaths.Count(), tenantName);

            return Ok(learningPaths);
        }

        // GET: api/{tenantName}/learningpaths/5
        [HttpGet("{id}")]
        public async Task<ActionResult<LearningPath>> GetLearningPath(string tenantName, int id)
        {
            _logger.LogInformation("Getting learning path {Id} for tenant: {TenantName}", id, tenantName);

            var learningPath = await _learningPathService.GetLearningPathAsync(id);

            if (learningPath == null)
            {
                _logger.LogWarning("Learning path {Id} not found in tenant: {TenantName}", id, tenantName);
                return NotFound();
            }

            return learningPath;
        }

        // POST: api/{tenantName}/learningpaths
        [HttpPost]
        public async Task<ActionResult<LearningPath>> PostLearningPath(string tenantName, LearningPath learningPath)
        {
            _logger.LogInformation("Creating learning path {Name} for tenant: {TenantName}",
                learningPath.LearningPathName, tenantName);

            var createdLearningPath = await _learningPathService.CreateLearningPathAsync(learningPath);

            _logger.LogInformation("Created learning path {Name} with ID {Id} for tenant: {TenantName}",
                createdLearningPath.LearningPathName, createdLearningPath.learningPath_id, tenantName);

            return CreatedAtAction(nameof(GetLearningPath),
                new { tenantName, id = createdLearningPath.learningPath_id },
                createdLearningPath);
        }

        // PUT: api/{tenantName}/learningpaths/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutLearningPath(string tenantName, int id, LearningPath learningPath)
        {
            if (id != learningPath.learningPath_id)
            {
                return BadRequest("ID mismatch");
            }

            _logger.LogInformation("Updating learning path {Id} for tenant: {TenantName}", id, tenantName);

            try
            {
                await _learningPathService.UpdateLearningPathAsync(learningPath);
                _logger.LogInformation("Updated learning path {Id} for tenant: {TenantName}", id, tenantName);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _learningPathService.LearningPathExistsAsync(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/{tenantName}/learningpaths/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLearningPath(string tenantName, int id)
        {
            _logger.LogInformation("Deleting learning path {Id} for tenant: {TenantName}", id, tenantName);

            var deleted = await _learningPathService.DeleteLearningPathAsync(id);

            if (!deleted)
            {
                return NotFound();
            }

            _logger.LogInformation("Deleted learning path {Id} for tenant: {TenantName}", id, tenantName);

            return NoContent();
        }

        // GET: api/{tenantName}/learningpaths/program/{trainingProgramId}
        [HttpGet("program/{trainingProgramId}")]
        public async Task<ActionResult<IEnumerable<LearningPath>>> GetLearningPathsByTrainingProgram(string tenantName, int trainingProgramId)
        {
            _logger.LogInformation("Getting learning paths for training program {TrainingProgramId} in tenant: {TenantName}",
                trainingProgramId, tenantName);

            var learningPaths = await _learningPathService.GetLearningPathsByTrainingProgramAsync(trainingProgramId);

            _logger.LogInformation("Found {Count} learning paths for training program {TrainingProgramId} in tenant: {TenantName}",
                learningPaths.Count(), trainingProgramId, tenantName);

            return Ok(learningPaths);
        }

        // POST: api/{tenantName}/learningpaths/{learningPathId}/assign/{trainingProgramId}
        [HttpPost("{learningPathId}/assign/{trainingProgramId}")]
        public async Task<IActionResult> AssignLearningPathToTrainingProgram(string tenantName, int learningPathId, int trainingProgramId)
        {
            _logger.LogInformation("Assigning learning path {LearningPathId} to training program {TrainingProgramId} for tenant: {TenantName}",
                learningPathId, trainingProgramId, tenantName);

            var success = await _learningPathService.AssignLearningPathToTrainingProgramAsync(trainingProgramId, learningPathId);

            if (!success)
            {
                return BadRequest("Association already exists or entities not found");
            }

            _logger.LogInformation("Successfully assigned learning path {LearningPathId} to training program {TrainingProgramId} for tenant: {TenantName}",
                learningPathId, trainingProgramId, tenantName);

            return Ok(new { Message = "Learning path successfully assigned to training program" });
        }

        // DELETE: api/{tenantName}/learningpaths/{learningPathId}/unassign/{trainingProgramId}
        [HttpDelete("{learningPathId}/unassign/{trainingProgramId}")]
        public async Task<IActionResult> RemoveLearningPathFromTrainingProgram(string tenantName, int learningPathId, int trainingProgramId)
        {
            _logger.LogInformation("Removing learning path {LearningPathId} from training program {TrainingProgramId} for tenant: {TenantName}",
                learningPathId, trainingProgramId, tenantName);

            var success = await _learningPathService.RemoveLearningPathFromTrainingProgramAsync(trainingProgramId, learningPathId);

            if (!success)
            {
                return NotFound("Association not found");
            }

            _logger.LogInformation("Successfully removed learning path {LearningPathId} from training program {TrainingProgramId} for tenant: {TenantName}",
                learningPathId, trainingProgramId, tenantName);

            return Ok(new { Message = "Learning path successfully removed from training program" });
        }

        [HttpGet("{learningPathId}/materials")]
        public async Task<ActionResult<IEnumerable<Material>>> GetLearningPathMaterials(
            string tenantName,
            int learningPathId,
            [FromQuery] bool includeOrder = true,
            [FromQuery] string? relationshipType = null)
        {
            _logger.LogInformation("📚 Getting materials for learning path {LearningPathId} for tenant: {TenantName}",
                learningPathId, tenantName);

            // Verify learning path exists
            var learningPath = await _learningPathService.GetLearningPathAsync(learningPathId);
            if (learningPath == null)
            {
                return NotFound($"Learning path {learningPathId} not found");
            }

            var materials = await _materialService.GetMaterialsByLearningPathAsync(learningPathId, includeOrder);

            _logger.LogInformation("Found {Count} materials for learning path {LearningPathId} for tenant: {TenantName}",
                materials.Count(), learningPathId, tenantName);

            return Ok(materials);
        }

        [HttpPost("{learningPathId}/assign-material/{materialId}")]
        public async Task<ActionResult<object>> AssignMaterialToLearningPath(
            string tenantName,
            int learningPathId,
            int materialId,
            [FromQuery] string relationshipType = "contains",
            [FromQuery] int? displayOrder = null)
        {
            _logger.LogInformation("Assigning material {MaterialId} to learning path {LearningPathId} for tenant: {TenantName}",
                materialId, learningPathId, tenantName);

            try
            {
                var relationshipId = await _materialService.AssignMaterialToLearningPathAsync(
                    materialId, learningPathId, relationshipType, displayOrder);

                _logger.LogInformation("Successfully assigned material {MaterialId} to learning path {LearningPathId} (Relationship: {RelationshipId}) for tenant: {TenantName}",
                    materialId, learningPathId, relationshipId, tenantName);

                return Ok(new
                {
                    Message = "Material successfully assigned to learning path",
                    RelationshipId = relationshipId,
                    LearningPathId = learningPathId,
                    MaterialId = materialId,
                    RelationshipType = relationshipType,
                    DisplayOrder = displayOrder
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

       
        /// Remove a material from this learning path
        
        [HttpDelete("{learningPathId}/remove-material/{materialId}")]
        public async Task<IActionResult> RemoveMaterialFromLearningPath(
            string tenantName,
            int learningPathId,
            int materialId)
        {
            _logger.LogInformation("Removing material {MaterialId} from learning path {LearningPathId} for tenant: {TenantName}",
                materialId, learningPathId, tenantName);

            var success = await _materialService.RemoveMaterialFromLearningPathAsync(materialId, learningPathId);

            if (!success)
            {
                return NotFound("Material assignment not found");
            }

            _logger.LogInformation("Successfully removed material {MaterialId} from learning path {LearningPathId} for tenant: {TenantName}",
                materialId, learningPathId, tenantName);

            return Ok(new { Message = "Material successfully removed from learning path" });
        }

       
        /// Reorder materials within this learning path
        
        [HttpPut("{learningPathId}/reorder-materials")]
        public async Task<IActionResult> ReorderLearningPathMaterials(
            string tenantName,
            int learningPathId,
            [FromBody] Dictionary<int, int> materialOrderMap)
        {
            _logger.LogInformation("Reordering {Count} materials in learning path {LearningPathId} for tenant: {TenantName}",
                materialOrderMap.Count, learningPathId, tenantName);

            var success = await _materialService.ReorderMaterialsInLearningPathAsync(learningPathId, materialOrderMap);

            if (!success)
            {
                return BadRequest("Failed to reorder materials");
            }

            _logger.LogInformation("Successfully reordered materials in learning path {LearningPathId} for tenant: {TenantName}",
                learningPathId, tenantName);

            return Ok(new { Message = "Materials successfully reordered" });
        }

       
        /// Bulk assign materials to this learning path
        
        /*  [HttpPost("{learningPathId}/bulk-assign-materials")]
          public async Task<ActionResult<BulkAssignmentResult>> BulkAssignMaterialsToLearningPath(
              string tenantName,
              int learningPathId,
              [FromBody] IEnumerable<BulkMaterialAssignment> assignments)
          {
              _logger.LogInformation("Bulk assigning {Count} materials to learning path {LearningPathId} for tenant: {TenantName}",
                  assignments.Count(), learningPathId, tenantName);

              var result = new BulkAssignmentResult();

              foreach (var assignment in assignments)
              {
                  try
                  {
                      var relationshipId = await _materialService.AssignMaterialToLearningPathAsync(
                          assignment.MaterialId,
                          learningPathId,
                          assignment.RelationshipType ?? "contains",
                          assignment.DisplayOrder);

                      result.SuccessfulAssignments++;
                  }
                  catch (Exception ex)
                  {
                      result.FailedAssignments++;
                      result.Errors.Add($"Error assigning material {assignment.MaterialId}: {ex.Message}");
                  }
              }

              _logger.LogInformation("Bulk assignment complete: {Success} successful, {Failed} failed for learning path {LearningPathId} for tenant: {TenantName}",
                  result.SuccessfulAssignments, result.FailedAssignments, learningPathId, tenantName);

              return Ok(result);
          }*/

        [HttpPost("detail")]
        public async Task<ActionResult<CreateLearningPathWithMaterialsResponse>> CreateCompleteLearningPath(
            string tenantName, 
            [FromBody] CreateLearningPathWithMaterialsRequest request)
        {
            _logger.LogInformation("Creating complete learning path: {Name} with {MaterialCount} materials for tenant: {TenantName}",
                request.LearningPathName, request.Materials.Count + (request.MaterialAssignments?.Count ?? 0), tenantName);

            try
            {
                var result = await _learningPathService.CreateLearningPathWithMaterialsAsync(request);

                _logger.LogInformation("Successfully created complete learning path {Id} with {MaterialCount} materials",
                    result.learningPath_id, result.MaterialCount);

                return CreatedAtAction(
                    nameof(GetCompleteLearningPath),
                    new { tenantName, id = result.learningPath_id },
                    result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid request for creating complete learning path: {Name}", request.LearningPathName);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create complete learning path: {Name}", request.LearningPathName);
                return StatusCode(500, new { Error = "Failed to create learning path", Details = ex.Message });
            }
        }

       
        /// Get a complete learning path with all materials and training programs
        
        [HttpGet("{id}/detail")]
        public async Task<ActionResult<CompleteLearningPathResponse>> GetCompleteLearningPath(
            string tenantName, 
            int id)
        {
            _logger.LogInformation("Getting complete learning path {Id} for tenant: {TenantName}", id, tenantName);

            var result = await _learningPathService.GetCompleteLearningPathAsync(id);

            if (result == null)
            {
                _logger.LogWarning("Learning path {Id} not found in tenant: {TenantName}", id, tenantName);
                return NotFound();
            }

            _logger.LogInformation("Retrieved complete learning path {Id}: {MaterialCount} materials, {ProgramCount} training programs",
                id, result.Summary.TotalMaterials, result.Summary.TotalTrainingPrograms);

            return Ok(result);
        }

        [HttpGet("detail")]
        public async Task<ActionResult<IEnumerable<CompleteLearningPathResponse>>> GetAllCompleteLearningPaths(
            string tenantName)
        {
            _logger.LogInformation("Getting all complete learning paths for tenant: {TenantName}", tenantName);

            var results = await _learningPathService.GetAllCompleteLearningPathsAsync();

            _logger.LogInformation("Retrieved {Count} complete learning paths for tenant: {TenantName}",
                results.Count(), tenantName);

            return Ok(results);
        }
        [HttpPost("{learningPathId}/assign-materials")]
        public async Task<ActionResult<object>> AssignMultipleMaterialsToLearningPath(
            string tenantName,
            int learningPathId,
            [FromBody] BulkMaterialAssignmentRequest request)
        {
            _logger.LogInformation("Assigning {Count} materials to learning path {LearningPathId} for tenant: {TenantName}",
                request.Materials.Count, learningPathId, tenantName);

            // Verify learning path exists
            var learningPath = await _learningPathService.GetLearningPathAsync(learningPathId);
            if (learningPath == null)
            {
                return NotFound($"Learning path {learningPathId} not found");
            }

            var results = new List<MaterialAssignmentResult>();

            foreach (var materialAssignment in request.Materials)
            {
                try
                {
                    var relationshipId = await _materialService.AssignMaterialToLearningPathAsync(
                        materialAssignment.MaterialId, 
                        learningPathId, 
                        materialAssignment.RelationshipType ?? "contains", 
                        materialAssignment.DisplayOrder);

                    results.Add(new MaterialAssignmentResult
                    {
                        MaterialId = materialAssignment.MaterialId,
                        Success = true,
                        RelationshipId = relationshipId,
                        RelationshipType = materialAssignment.RelationshipType ?? "contains",
                        DisplayOrder = materialAssignment.DisplayOrder,
                        Message = "Successfully assigned"
                    });

                    _logger.LogInformation("Successfully assigned material {MaterialId} to learning path {LearningPathId} (Relationship: {RelationshipId})",
                        materialAssignment.MaterialId, learningPathId, relationshipId);
                }
                catch (ArgumentException ex)
                {
                    results.Add(new MaterialAssignmentResult
                    {
                        MaterialId = materialAssignment.MaterialId,
                        Success = false,
                        Message = ex.Message
                    });

                    _logger.LogWarning(ex, "Failed to assign material {MaterialId} to learning path {LearningPathId}",
                        materialAssignment.MaterialId, learningPathId);
                }
                catch (Exception ex)
                {
                    results.Add(new MaterialAssignmentResult
                    {
                        MaterialId = materialAssignment.MaterialId,
                        Success = false,
                        Message = $"Unexpected error: {ex.Message}"
                    });

                    _logger.LogError(ex, "Unexpected error assigning material {MaterialId} to learning path {LearningPathId}",
                        materialAssignment.MaterialId, learningPathId);
                }
            }

            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);

            _logger.LogInformation("Bulk assignment completed for learning path {LearningPathId}: {SuccessCount} successful, {FailureCount} failed",
                learningPathId, successCount, failureCount);

            return Ok(new
            {
                Message = $"Bulk assignment completed: {successCount} successful, {failureCount} failed",
                LearningPathId = learningPathId,
                TotalRequested = request.Materials.Count,
                SuccessCount = successCount,
                FailureCount = failureCount,
                Results = results
            });
        }

    }

    // Supporting DTOs for bulk operations
    public class BulkMaterialAssignmentRequest
    {
        public List<MaterialAssignmentRequest> Materials { get; set; } = new();
    }

    public class MaterialAssignmentResult
    {
        public int MaterialId { get; set; }
        public bool Success { get; set; }
        public string? Message { get; set; }
        public int? RelationshipId { get; set; }
        public string? RelationshipType { get; set; }
        public int? DisplayOrder { get; set; }
    }

}
