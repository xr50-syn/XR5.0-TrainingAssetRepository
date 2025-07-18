﻿using System;
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
    public class TrainingProgramsController : ControllerBase
    {
        private readonly ITrainingProgramService _trainingProgramService;
        private readonly IMaterialService _materialService;
        private readonly ILogger<TrainingProgramsController> _logger;

        public TrainingProgramsController(
            ITrainingProgramService trainingProgramService,
            IMaterialService materialService,
            ILogger<TrainingProgramsController> logger)
        {
            _trainingProgramService = trainingProgramService;
            _materialService = materialService;
            _logger = logger;
        }

        // POST: api/{tenantName}/trainingprograms
        [HttpPost]
        public async Task<ActionResult<CreateTrainingProgramWithMaterialsResponse>> PostTrainingProgram(
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
                var result = await _trainingProgramService.CreateTrainingProgramWithMaterialsAsync(request);

                _logger.LogInformation("Successfully created training program {Id} with {MaterialCount} materials for tenant: {TenantName}", 
                    result.Id, result.MaterialCount, tenantName);

                return CreatedAtAction(
                    nameof(GetTrainingProgram), 
                    new { tenantName, id = result.Id }, 
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

        // PUT: api/{tenantName}/trainingprograms/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTrainingProgram(string tenantName, int id, TrainingProgram program)
        {
            if (id != program.Id)
            {
                return BadRequest("ID mismatch");
            }

            _logger.LogInformation("Updating training program {Id} for tenant: {TenantName}", id, tenantName);
            
            try
            {
                await _trainingProgramService.UpdateTrainingProgramAsync(program);
                _logger.LogInformation("Updated training program {Id} for tenant: {TenantName}", id, tenantName);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _trainingProgramService.TrainingProgramExistsAsync(id))
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

        // DELETE: api/{tenantName}/trainingprograms/5
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
   #region Material Assignment Endpoints

        /// <summary>
        /// Get all materials assigned to this training program
        /// </summary>
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

        /// <summary>
        /// Assign a material to this training program
        /// </summary>
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
        #region Complete Training Program Endpoints

        /// <summary>
        /// Create a complete training program with materials and learning paths in one request
        /// </summary>
        [HttpPost("complete")]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> CreateCompleteTrainingProgram(
            string tenantName, 
            [FromBody] CompleteTrainingProgramRequest request)
        {
            _logger.LogInformation("Creating complete training program: {Name} with {MaterialCount} materials for tenant: {TenantName}",
                request.Name, request.Materials.Count + (request.MaterialsToCreate?.Count ?? 0), tenantName);

            try
            {
                var result = await _trainingProgramService.CreateCompleteTrainingProgramAsync(request);

                _logger.LogInformation("Successfully created complete training program {Id} with {MaterialCount} materials",
                    result.Id, result.Summary.TotalMaterials);

                return CreatedAtAction(
                    nameof(GetCompleteTrainingProgram),
                    new { tenantName, id = result.Id },
                    result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create complete training program: {Name}", request.Name);
                return StatusCode(500, new { Error = "Failed to create training program", Details = ex.Message });
            }
        }

        /// <summary>
        /// Get a complete training program with all materials and learning paths
        /// </summary>
        [HttpGet("{id}/complete")]
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
                id, result.Summary.TotalMaterials, result.Summary.TotalLearningPaths);

            return Ok(result);
        }

        /// <summary>
        /// Get all training programs with complete information
        /// </summary>
        [HttpGet("complete")]
        public async Task<ActionResult<IEnumerable<CompleteTrainingProgramResponse>>> GetAllCompleteTrainingPrograms(
            string tenantName)
        {
            _logger.LogInformation("Getting all complete training programs for tenant: {TenantName}", tenantName);

            var results = await _trainingProgramService.GetAllCompleteTrainingProgramsAsync();

            _logger.LogInformation("Retrieved {Count} complete training programs for tenant: {TenantName}",
                results.Count(), tenantName);

            return Ok(results);
        }

        #endregion

        // Update the existing GET method to return complete data by default
        [HttpGet("{id}")]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> GetTrainingProgram(string tenantName, int id)
        {
            // Redirect to complete endpoint for consistency
            return await GetCompleteTrainingProgram(tenantName, id);
        }

        // Update the existing GET all method to return complete data by default  
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompleteTrainingProgramResponse>>> GetTrainingPrograms(string tenantName)
        {
            // Redirect to complete endpoint for consistency
            return await GetAllCompleteTrainingPrograms(tenantName);
        }

        /// <summary>
        /// Bulk assign materials to this training program
        /// </summary>
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

        #endregion
    }
}