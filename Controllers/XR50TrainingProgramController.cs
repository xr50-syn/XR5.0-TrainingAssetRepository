using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/[controller]")]
    [ApiController]
    public class programsController : ControllerBase
    {
        private readonly ITrainingProgramService _trainingProgramService;
        private readonly IMaterialService _materialService;
        private readonly ILogger<programsController> _logger;

        public programsController(
            ITrainingProgramService trainingProgramService,
            IMaterialService materialService,
            ILogger<programsController> logger)
        {
            _trainingProgramService = trainingProgramService;
            _materialService = materialService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> PostTrainingProgram(
            string tenantName,
            [FromBody] CreateTrainingProgramWithMaterialsRequest request)
        {
            _logger.LogInformation("Creating training program '{Name}' with {MaterialCount} materials for tenant: {TenantName}",
                request.Name, request.Materials.Count, tenantName);

            try
            {
                // Validate request
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest("Training program name is required");
                }

                // Create the training program with materials (empty list is fine)
                var result = await _trainingProgramService.CreateTrainingProgramAsync(request);

                _logger.LogInformation("Successfully created training program {Id} for tenant: {TenantName}",
                    result.id, tenantName);

                return CreatedAtAction(
                    nameof(GetTrainingProgram),
                    new { tenantName, id = result.id },
                    result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for tenant {TenantName}: {Message}", tenantName, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating training program with materials for tenant: {TenantName}", tenantName);
                return StatusCode(500, "An error occurred while creating the training program");
            }
        }

        // PUT: api/{tenantName}/programs/5
        [HttpPut("{id}")]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> PutTrainingProgram(
            string tenantName,
            int id,
            [FromBody] UpdateTrainingProgramRequest request)
        {
            _logger.LogInformation("Updating training program {Id} for tenant: {TenantName}", id, tenantName);

            try
            {
                var result = await _trainingProgramService.UpdateCompleteTrainingProgramAsync(id, request);
                _logger.LogInformation("Updated training program {Id} for tenant: {TenantName} with {MaterialCount} materials and {LearningPathCount} learning paths",
                    id, tenantName, result.Materials.Count, result.learning_path.Count);

                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound($"Training program {id} not found");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for updating training program {Id}: {Message}", id, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating training program {Id} for tenant: {TenantName}", id, tenantName);
                return StatusCode(500, "An error occurred while updating the training program");
            }
        }
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTrainingProgram(string tenantName, int id)
        {
            _logger.LogInformation("Deleting training program {Id} for tenant: {TenantName}", id, tenantName);
            
            var deleted = await _trainingProgramService.DeleteTrainingProgramAsync(id);
            
            if (!deleted)
            {
                return NotFound();
            }

            _logger.LogInformation("Deleted training program {Id} for tenant: {TenantName}", id, tenantName);

            return NoContent();
        }

        [HttpGet("{trainingProgramId}/materials")]
        public async Task<ActionResult<IEnumerable<Material>>> GetTrainingProgramMaterials(
            string tenantName,
            int trainingProgramId,
            [FromQuery] bool includeOrder = true)
        {
            _logger.LogInformation("📚 Getting materials for training program {TrainingProgramId} for tenant: {TenantName}",
                trainingProgramId, tenantName);

            // Verify training program exists
            var program = await _trainingProgramService.GetTrainingProgramAsync(trainingProgramId);
            if (program == null)
            {
                return NotFound($"Training program {trainingProgramId} not found");
            }

            var materials = await _materialService.GetMaterialsByTrainingProgramAsync(trainingProgramId);

            _logger.LogInformation("Found {Count} materials for training program {TrainingProgramId} for tenant: {TenantName}",
                materials.Count(), trainingProgramId, tenantName);

            return Ok(materials);
        }

        [HttpPost("{trainingProgramId}/assign-material/{materialId}")]
        public async Task<ActionResult<object>> AssignMaterialToTrainingProgram(
            string tenantName,
            int trainingProgramId,
            int materialId,
            [FromQuery] string relationshipType = "assigned",
            [FromQuery] int? displayOrder = null)
        {
            _logger.LogInformation("🔗 Assigning material {MaterialId} to training program {TrainingProgramId} for tenant: {TenantName}",
                materialId, trainingProgramId, tenantName);

            try
            {
                // Use TrainingProgramService for simple assignment
                var success = await _trainingProgramService.AssignMaterialToTrainingProgramAsync(trainingProgramId, materialId);

                if (!success)
                {
                    return BadRequest("Assignment already exists");
                }

                _logger.LogInformation("Successfully assigned material {MaterialId} to training program {TrainingProgramId} for tenant: {TenantName}",
                    materialId, trainingProgramId, tenantName);

                return Ok(new
                {
                    Message = "Material successfully assigned to training program",
                    TrainingProgramId = trainingProgramId,
                    MaterialId = materialId,
                    RelationshipType = relationshipType,
                    AssignmentType = "Simple" // Indicate this is a simple assignment
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpDelete("{trainingProgramId}/remove-material/{materialId}")]
        public async Task<IActionResult> RemoveMaterialFromTrainingProgram(
            string tenantName,
            int trainingProgramId,
            int materialId)
        {
            _logger.LogInformation("Removing material {MaterialId} from training program {TrainingProgramId} for tenant: {TenantName}",
                materialId, trainingProgramId, tenantName);

            // Use TrainingProgramService for simple removal
            var success = await _trainingProgramService.RemoveMaterialFromTrainingProgramAsync(trainingProgramId, materialId);

            if (!success)
            {
                return NotFound("Material assignment not found");
            }

            _logger.LogInformation("Successfully removed material {MaterialId} from training program {TrainingProgramId} for tenant: {TenantName}",
                materialId, trainingProgramId, tenantName);

            return Ok(new { Message = "Material successfully removed from training program" });
        }

        [HttpPost("detail")]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> CreateCompleteTrainingProgram(
            string tenantName, 
            [FromBody] CompleteTrainingProgramRequest request)
        {
            _logger.LogInformation("Creating complete training program: {Name} with {MaterialCount} materials for tenant: {TenantName}",
                request.Name, request.Materials.Count, tenantName);

            try
            {
                var result = await _trainingProgramService.CreateCompleteTrainingProgramAsync(request);

                _logger.LogInformation("Successfully created complete training program {Id} with {MaterialCount} materials",
                    result.id, result.Materials.Count);

                return CreatedAtAction(
                    nameof(GetCompleteTrainingProgram),
                    new { tenantName, id = result.id },
                    result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create complete training program: {Name}", request.Name);
                return StatusCode(500, new { Error = "Failed to create training program", Details = ex.Message });
            }
        }

        [HttpGet("{id}/detail")]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> GetCompleteTrainingProgram(
            string tenantName, 
            int id)
        {
            _logger.LogInformation("Getting complete training program {Id} for tenant: {TenantName}", id, tenantName);

            var result = await _trainingProgramService.GetCompleteTrainingProgramAsync(id);

            if (result == null)
            {
                _logger.LogWarning("Training program {Id} not found in tenant: {TenantName}", id, tenantName);
                return NotFound();
            }

            _logger.LogInformation("Retrieved complete training program {Id}: {MaterialCount} materials, {PathCount} learning paths",
                id, result.Materials.Count, result.learning_path.Count);

            return Ok(result);
        }
        [HttpGet("detail")]
        public async Task<ActionResult<IEnumerable<CompleteTrainingProgramResponse>>> GetAllCompleteTrainingPrograms(
            string tenantName)
        {
            _logger.LogInformation("Getting all complete training programs for tenant: {TenantName}", tenantName);

            var results = await _trainingProgramService.GetAllCompleteTrainingProgramsAsync();

            _logger.LogInformation("Retrieved {Count} complete training programs for tenant: {TenantName}",
                results.Count(), tenantName);

            return Ok(results);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> GetTrainingProgram(string tenantName, int id)
        {
            return await GetCompleteTrainingProgram(tenantName, id);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TrainingProgram>>> GetTrainingPrograms(string tenantName)
        {
            _logger.LogInformation("Getting all training programs (summary) for tenant: {TenantName}", tenantName);

            var results = await _trainingProgramService.GetAllTrainingProgramsAsync();

            _logger.LogInformation("Retrieved {Count} training programs for tenant: {TenantName}",
                results.Count(), tenantName);

            return Ok(results);
        }

        /*[HttpPost("{trainingProgramId}/bulk-assign-materials")]
        public async Task<ActionResult<BulkAssignmentResult>> BulkAssignMaterialsToTrainingProgram(
            string tenantName,
            int trainingProgramId,
            [FromBody] IEnumerable<BulkMaterialAssignment> assignments)
        {
            _logger.LogInformation("Bulk assigning {Count} materials to training program {TrainingProgramId} for tenant: {TenantName}",
                assignments.Count(), trainingProgramId, tenantName);

            var result = new BulkAssignmentResult();

            foreach (var assignment in assignments)
            {
                try
                {
                    var relationshipId = await _materialService.AssignMaterialToTrainingProgramAsync(
                        assignment.MaterialId,
                        trainingProgramId,
                        assignment.RelationshipType ?? "assigned");

                    result.SuccessfulAssignments++;
                }
                catch (Exception ex)
                {
                    result.FailedAssignments++;
                    result.Errors.Add($"Error assigning material {assignment.MaterialId}: {ex.Message}");
                }
            }

            _logger.LogInformation("Bulk assignment complete: {Success} successful, {Failed} failed for training program {TrainingProgramId} for tenant: {TenantName}",
                result.SuccessfulAssignments, result.FailedAssignments, trainingProgramId, tenantName);

            return Ok(result);
        }*/

    }
}