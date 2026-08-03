using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using XR50TrainingAssetRepo.Models;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Models.DTOs;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Materials;
using XR50TrainingAssetRepo.Infrastructure.ErrorHandling;
using XR50TrainingAssetRepo.Infrastructure.Auth;

namespace XR50TrainingAssetRepo.Controllers
{
    [Route("api/{tenantName}/[controller]")]
    [Authorize(Policy = "TenantMember")]
    [ApiController]
    public class programsController : ControllerBase
    {
        private readonly ITrainingProgramService _trainingProgramService;
        private readonly IMaterialServiceBase _materialServiceBase;
        private readonly IMaterialRelationshipService _materialRelationshipService;
        private readonly IUserMaterialService _userMaterialService;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly IamOptions _iamOptions;
        private readonly ILogger<programsController> _logger;

        public programsController(
            ITrainingProgramService trainingProgramService,
            IMaterialServiceBase materialServiceBase,
            IMaterialRelationshipService materialRelationshipService,
            IUserMaterialService userMaterialService,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            Microsoft.Extensions.Options.IOptions<IamOptions> iamOptions,
            ILogger<programsController> logger)
        {
            _iamOptions = iamOptions.Value;
            _trainingProgramService = trainingProgramService;
            _materialServiceBase = materialServiceBase;
            _materialRelationshipService = materialRelationshipService;
            _userMaterialService = userMaterialService;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Policy = "TenantAdmin")]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> PostTrainingProgram(
            string tenantName)
        {
            try
            {
                // Read raw JSON body
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                _logger.LogInformation("Received training program creation request body: {Body}", body);

                var jsonElement = JsonSerializer.Deserialize<JsonElement>(body);

                // Parse the request
                var request = new CreateTrainingProgramWithMaterialsRequest
                {
                    Name = jsonElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                    Description = jsonElement.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    Objectives = jsonElement.TryGetProperty("objectives", out var obj) ? obj.GetString() : null,
                    Requirements = jsonElement.TryGetProperty("requirements", out var req) ? req.GetString() : null,
                    // Accept both JSON numbers and numeric strings. The GUI may serialize rank
                    // fields either way per field, and an inconsistent client previously caused
                    // silently dropped requirements (only Number was honored). ParseNullableInt
                    // matches what required_upto_level_rank already did and what
                    // ParseLearningPathArray uses for per-path ranks.
                    min_level_rank = jsonElement.TryGetProperty("min_level_rank", out var minRank) ? ParseNullableInt(minRank) : null,
                    max_level_rank = jsonElement.TryGetProperty("max_level_rank", out var maxRank) ? ParseNullableInt(maxRank) : null,
                    required_upto_level_rank = jsonElement.TryGetProperty("required_upto_level_rank", out var reqRank) ? ParseNullableInt(reqRank) : null
                };

                _logger.LogInformation("Creating training program '{Name}' for tenant: {TenantName}",
                    request.Name, tenantName);

                // Validate request
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return this.ProblemBadRequest("Training program name is required.");
                }

                // Parse materials array - handle both int[] and object[] with material creation data
                var createdMaterialIds = new List<int>();
                if (jsonElement.TryGetProperty("materials", out var materialsElement) &&
                    materialsElement.ValueKind == JsonValueKind.Array)
                {
                    var (existingIds, materialCreationRequests, materialAssignments) = ParseMaterialsArray(materialsElement);

                    // Create inline materials
                    if (materialCreationRequests.Any())
                    {
                        foreach (var materialData in materialCreationRequests)
                        {
                            // Forward to the material creation logic
                            var materialResult = await CreateMaterialFromJson(tenantName, materialData);
                            if (materialResult != null)
                            {
                                createdMaterialIds.Add(materialResult.id);
                                _logger.LogInformation("Created inline material {Id} for program '{Name}'",
                                    materialResult.id, request.Name);
                            }
                        }
                    }

                    // Combine existing IDs with newly created IDs
                    request.Materials = existingIds.Concat(createdMaterialIds).ToList();

                    // Add material assignments with rank data
                    request.MaterialAssignments = materialAssignments;
                }

                // Parse learning_path array - handle object[] with learning path creation details
                if (jsonElement.TryGetProperty("learning_path", out var learningPathElement) &&
                    learningPathElement.ValueKind == JsonValueKind.Array)
                {
                    request.learning_path = ParseLearningPathArray(learningPathElement);
                }

                // Parse LearningPaths (existing IDs)
                if (jsonElement.TryGetProperty("LearningPaths", out var learningPathsElement) &&
                    learningPathsElement.ValueKind == JsonValueKind.Array)
                {
                    request.LearningPaths = ParseIdArray(learningPathsElement);
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
                return this.ProblemBadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating training program with materials for tenant: {TenantName}", tenantName);
                return this.ProblemServerError("An error occurred while creating the training program.");
            }
        }

        /// <summary>
        /// Bulk mark multiple materials as complete within a training program
        /// POST /api/{tenantName}/programs/{programId}/submit
        /// Requires authentication - user ID is extracted from JWT token claims
        /// </summary>
        [HttpPost("{programId}/submit")]
        public async Task<ActionResult<BulkMaterialCompleteResponse>> BulkSubmitMaterials(
            string tenantName,
            int programId,
            [FromBody] BulkMaterialCompleteRequest request)
        {
            try
            {
                // The development user only applies under the Development anonymous bypass.
                var userId = User.GetEffectiveUserId(_iamOptions, _environment);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Bulk material submission without a resolvable user identity rejected");
                    return Unauthorized();
                }

                _logger.LogInformation(
                    "User {UserId} bulk submitting {Count} materials in program {ProgramId}, tenant {TenantName}",
                    userId, request.material_ids?.Count ?? 0, programId, tenantName);

                var result = await _userMaterialService.BulkMarkMaterialsCompleteAsync(
                    userId, programId, request);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error in bulk submit");
                return this.ProblemBadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk submitting materials in program {ProgramId}", programId);
                return this.ProblemServerError("Failed to bulk submit materials.");
            }
        }

        // PUT: api/{tenantName}/programs/5
        [HttpPut("{id}")]
        [Authorize(Policy = "TenantAdmin")]
        public async Task<ActionResult<CompleteTrainingProgramResponse>> PutTrainingProgram(
            string tenantName,
            string id)
        {
            _logger.LogInformation("Updating training program {Id} for tenant: {TenantName}", id, tenantName);

            try
            {
                // Parse ID from string (UUID-ready)
                if (!int.TryParse(id, out int programId))
                {
                    return this.ProblemBadRequest($"Invalid training program ID format: {id}");
                }

                // Read raw JSON body
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(body);

                // Parse the request, handling both formats for learning_path and materials
                var request = new UpdateTrainingProgramRequest
                {
                    Name = jsonElement.GetProperty("name").GetString() ?? "",
                    Description = jsonElement.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    Objectives = jsonElement.TryGetProperty("objectives", out var obj) ? obj.GetString() : null,
                    Requirements = jsonElement.TryGetProperty("requirements", out var req) ? req.GetString() : null,
                    // Accept both JSON numbers and numeric strings. The GUI may serialize rank
                    // fields either way per field, and an inconsistent client previously caused
                    // silently dropped requirements (only Number was honored). ParseNullableInt
                    // matches what required_upto_level_rank already did and what
                    // ParseLearningPathArray uses for per-path ranks.
                    min_level_rank = jsonElement.TryGetProperty("min_level_rank", out var minRank) ? ParseNullableInt(minRank) : null,
                    max_level_rank = jsonElement.TryGetProperty("max_level_rank", out var maxRank) ? ParseNullableInt(maxRank) : null,
                    required_upto_level_rank = jsonElement.TryGetProperty("required_upto_level_rank", out var reqRank) ? ParseNullableInt(reqRank) : null
                };

                // Parse materials array - handle both int[] and object[] with id property and rank data
                if (jsonElement.TryGetProperty("materials", out var materialsElement) &&
                    materialsElement.ValueKind == JsonValueKind.Array)
                {
                    var (existingIds, _, materialAssignments) = ParseMaterialsArray(materialsElement);
                    request.Materials = existingIds;
                    request.MaterialAssignments = materialAssignments;
                }

                // Parse learning_path array - handle object[] with learning path creation details
                if (jsonElement.TryGetProperty("learning_path", out var learningPathElement) &&
                    learningPathElement.ValueKind == JsonValueKind.Array)
                {
                    request.learning_path = ParseLearningPathArray(learningPathElement);
                }

                var result = await _trainingProgramService.UpdateCompleteTrainingProgramAsync(programId, request);
                _logger.LogInformation("Updated training program {Id} for tenant: {TenantName} with {MaterialCount} materials and {LearningPathCount} learning paths",
                    programId, tenantName, result.Materials.Count, result.learning_path.Count);

                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return this.ProblemNotFound($"Training program {id} was not found.");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid request for updating training program {Id}: {Message}", id, ex.Message);
                return this.ProblemBadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating training program {Id} for tenant: {TenantName}", id, tenantName);
                return this.ProblemServerError("An error occurred while updating the training program.");
            }
        }

        private int? ParseNullableInt(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null)
                return null;
            if (element.ValueKind == JsonValueKind.String)
            {
                var str = element.GetString();
                return int.TryParse(str, out int val) ? val : null;
            }
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetInt32();
            return null;
        }

        private List<int> ParseIdArray(JsonElement arrayElement)
        {
            var ids = new List<int>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number)
                {
                    ids.Add(item.GetInt32());
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    // Handle object with id property (from GET response format)
                    if (item.TryGetProperty("id", out var idProp))
                    {
                        if (idProp.ValueKind == JsonValueKind.String)
                        {
                            if (int.TryParse(idProp.GetString(), out int id))
                                ids.Add(id);
                        }
                        else if (idProp.ValueKind == JsonValueKind.Number)
                        {
                            ids.Add(idProp.GetInt32());
                        }
                    }
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    if (int.TryParse(item.GetString(), out int id))
                        ids.Add(id);
                }
            }
            return ids;
        }

        private List<LearningPathCreationRequest> ParseLearningPathArray(JsonElement arrayElement)
        {
            var learningPaths = new List<LearningPathCreationRequest>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var learningPath = new LearningPathCreationRequest
                    {
                        id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null,
                        Name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                        Description = item.TryGetProperty("description", out var descProp) && descProp.ValueKind != JsonValueKind.Null ? descProp.GetString() : null,
                        inherit_from_program = item.TryGetProperty("inherit_from_program", out var inheritProp) && inheritProp.ValueKind == JsonValueKind.True ? true : (inheritProp.ValueKind == JsonValueKind.False ? false : null),
                        min_level_rank = item.TryGetProperty("min_level_rank", out var minProp) ? ParseNullableInt(minProp) : null,
                        max_level_rank = item.TryGetProperty("max_level_rank", out var maxProp) ? ParseNullableInt(maxProp) : null,
                        required_upto_level_rank = item.TryGetProperty("required_upto_level_rank", out var reqProp) ? ParseNullableInt(reqProp) : null
                    };
                    learningPaths.Add(learningPath);
                }
            }
            return learningPaths;
        }

        private (List<int> existingIds, List<JsonElement> materialCreationRequests, List<ProgramMaterialAssignmentRequest> materialAssignments) ParseMaterialsArray(JsonElement arrayElement)
        {
            var existingIds = new List<int>();
            var materialCreationRequests = new List<JsonElement>();
            var materialAssignments = new List<ProgramMaterialAssignmentRequest>();

            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number)
                {
                    // It's an existing material ID
                    existingIds.Add(item.GetInt32());
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    // Check if it's a reference to existing material (has numeric id) or a new material to create
                    if (item.TryGetProperty("id", out var idProp))
                    {
                        int? materialId = null;
                        if (idProp.ValueKind == JsonValueKind.Number)
                        {
                            materialId = idProp.GetInt32();
                        }
                        else if (idProp.ValueKind == JsonValueKind.String && int.TryParse(idProp.GetString(), out int parsedId))
                        {
                            materialId = parsedId;
                        }

                        if (materialId.HasValue)
                        {
                            existingIds.Add(materialId.Value);

                            // Extract rank properties if present
                            var assignment = new ProgramMaterialAssignmentRequest
                            {
                                id = materialId.Value,
                                inherit_from_program = item.TryGetProperty("inherit_from_program", out var inheritProp)
                                    ? (inheritProp.ValueKind == JsonValueKind.True ? true : (inheritProp.ValueKind == JsonValueKind.False ? false : null))
                                    : null,
                                min_level_rank = item.TryGetProperty("min_level_rank", out var minProp) ? ParseNullableInt(minProp) : null,
                                max_level_rank = item.TryGetProperty("max_level_rank", out var maxProp) ? ParseNullableInt(maxProp) : null,
                                required_upto_level_rank = item.TryGetProperty("required_upto_level_rank", out var reqProp) ? ParseNullableInt(reqProp) : null
                            };
                            materialAssignments.Add(assignment);
                        }
                        else
                        {
                            // Object without numeric id - treat as new material to create
                            materialCreationRequests.Add(item);
                        }
                    }
                    else
                    {
                        // Object without id property - treat as new material to create
                        materialCreationRequests.Add(item);
                    }
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    if (int.TryParse(item.GetString(), out int id))
                    {
                        existingIds.Add(id);
                    }
                }
            }

            return (existingIds, materialCreationRequests, materialAssignments);
        }

        private async Task<CreateMaterialResponse?> CreateMaterialFromJson(string tenantName, JsonElement materialData)
        {
            try
            {
                // Delegate to material service for creation
                var material = await _materialServiceBase.CreateFromJsonAsync(materialData);

                if (material != null)
                {
                    return new CreateMaterialResponse
                    {
                        id = material.id,
                        Name = material.Name,
                        Type = material.Type.ToString(),
                        Status = "success",
                        Message = "Material created successfully"
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create inline material for tenant: {TenantName}", tenantName);
                throw new ArgumentException($"Failed to create inline material: {ex.Message}", ex);
            }
        }
        [HttpDelete("{id}")]
        [Authorize(Policy = "TenantAdmin")]
        public async Task<IActionResult> DeleteTrainingProgram(string tenantName, int id)
        {
            _logger.LogInformation("Deleting training program {Id} for tenant: {TenantName}", id, tenantName);
            
            var deleted = await _trainingProgramService.DeleteTrainingProgramAsync(id);
            
            if (!deleted)
            {
                return this.ProblemNotFound($"Training program {id} was not found.");
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
            _logger.LogInformation(" Getting materials for training program {TrainingProgramId} for tenant: {TenantName}",
                trainingProgramId, tenantName);

            // Verify training program exists
            var program = await _trainingProgramService.GetTrainingProgramAsync(trainingProgramId);
            if (program == null)
            {
                return this.ProblemNotFound($"Training program {trainingProgramId} was not found.");
            }

            var materials = await _materialRelationshipService.GetMaterialsByTrainingProgramAsync(trainingProgramId);

            _logger.LogInformation("Found {Count} materials for training program {TrainingProgramId} for tenant: {TenantName}",
                materials.Count(), trainingProgramId, tenantName);

            return Ok(materials);
        }

        [HttpPost("{trainingProgramId}/assign-material/{materialId}")]
        [Authorize(Policy = "TenantAdmin")]
        public async Task<ActionResult<object>> AssignMaterialToTrainingProgram(
            string tenantName,
            int trainingProgramId,
            int materialId,
            [FromQuery] string relationshipType = "assigned",
            [FromQuery] int? displayOrder = null)
        {
            _logger.LogInformation("Assigning material {MaterialId} to training program {TrainingProgramId} for tenant: {TenantName}",
                materialId, trainingProgramId, tenantName);

            try
            {
                // Use TrainingProgramService for simple assignment
                var success = await _trainingProgramService.AssignMaterialToTrainingProgramAsync(trainingProgramId, materialId);

                if (!success)
                {
                    return this.ProblemBadRequest("Assignment already exists.");
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
                return this.ProblemBadRequest(ex.Message);
            }
        }
        [HttpDelete("{trainingProgramId}/remove-material/{materialId}")]
        [Authorize(Policy = "TenantAdmin")]
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
                return this.ProblemNotFound("Material assignment was not found.");
            }

            _logger.LogInformation("Successfully removed material {MaterialId} from training program {TrainingProgramId} for tenant: {TenantName}",
                materialId, trainingProgramId, tenantName);

            return Ok(new { Message = "Material successfully removed from training program" });
        }

        [HttpPost("detail")]
        [Authorize(Policy = "TenantAdmin")]
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
                return this.ProblemServerError("Failed to create training program.");
            }
        }

        [HttpGet("{id}/detail")]
        public async Task<ActionResult<SimplifiedCompleteTrainingProgramResponse>> GetCompleteTrainingProgram(
            string tenantName,
            int id)
        {
            _logger.LogInformation("Getting complete training program {Id} for tenant: {TenantName}", id, tenantName);

            var result = await _trainingProgramService.GetSimplifiedCompleteTrainingProgramAsync(id);

            if (result == null)
            {
                _logger.LogWarning("Training program {Id} not found in tenant: {TenantName}", id, tenantName);
                return this.ProblemNotFound($"Training program {id} was not found.");
            }

            _logger.LogInformation("Retrieved simplified training program {Id}: {MaterialCount} direct materials, {LearningPathMaterialCount} learning path materials",
                id, result.Materials.Count, result.learning_path.Count);

            return Ok(result);
        }
        [HttpGet("detail")]
        public async Task<ActionResult<IEnumerable<SimplifiedCompleteTrainingProgramResponse>>> GetAllCompleteTrainingPrograms(
            string tenantName)
        {
            _logger.LogInformation("Getting all simplified training programs for tenant: {TenantName}", tenantName);

            var results = await _trainingProgramService.GetAllSimplifiedCompleteTrainingProgramsAsync();

            _logger.LogInformation("Retrieved {Count} simplified training programs for tenant: {TenantName}",
                results.Count(), tenantName);

            return Ok(results);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SimplifiedCompleteTrainingProgramResponse>> GetTrainingProgram(string tenantName, int id)
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
                    var relationshipId = await _materialRelationshipService.AssignMaterialToTrainingProgramAsync(
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
