using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using XR50TrainingAssetRepo.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using XR50TrainingAssetRepo.Data;
using XR50TrainingAssetRepo.Services;
using XR50TrainingAssetRepo.Services.Materials;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

var builder = WebApplication.CreateBuilder(args);
var  MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
        options.JsonSerializerOptions.Converters.Add(new NullableIntToStringConverter());
        options.JsonSerializerOptions.Converters.Add(new IntToStringConverter());
    });

builder.Services.AddHttpContextAccessor();

// Configure JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var configuration = builder.Configuration;

        // Token validation parameters
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["IAM:Issuer"],
            ValidAudience = configuration["IAM:Audience"],
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        // For IAM token integration with external identity provider
        var authority = configuration["IAM:Authority"];
        if (!string.IsNullOrEmpty(authority))
        {
            options.Authority = authority;
            options.RequireHttpsMetadata = configuration.GetValue<bool>("IAM:RequireHttpsMetadata", true);

            var metadataAddress = configuration["IAM:MetadataEndpoint"];
            if (!string.IsNullOrEmpty(metadataAddress))
            {
                options.MetadataAddress = metadataAddress;
            }
        }

        // Optional: Configure events for logging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("JWT Authentication failed: {Exception}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("JWT token validated for user: {User}", context.Principal?.Identity?.Name ?? "Unknown");
                return Task.CompletedTask;
            }
        };
    });

// Configure Authorization Policies
builder.Services.AddAuthorization(options =>
{
    // Policy for regular tenant users - requires tenantName claim
    options.AddPolicy("TenantUser", policy =>
        policy.RequireClaim("tenantName"));

    // Policy for tenant administrators
    options.AddPolicy("TenantAdmin", policy =>
        policy.RequireClaim("role", "admin", "tenantadmin"));

    // Policy for system administrators (can manage all tenants)
    options.AddPolicy("SystemAdmin", policy =>
        policy.RequireClaim("role", "systemadmin", "superadmin"));
});

builder.Services.AddXR50MultitenancyWithDynamicDb(builder.Configuration);
/*builder.Services.AddScoped<IXR50TenantService, XR50TenantService>();
builder.Services.AddScoped<IXR50TenantManagementService, XR50TenantManagementService>();
builder.Services.AddScoped<XR50MigrationService>();
*/
/*string connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<XR50TrainingContext>(opt =>
    opt.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
*/
builder.Services.AddDbContext<XR50TrainingContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetService<IConfiguration>();
    
    // Default configuration - OnConfiguring will override this for tenant operations
    var baseConnectionString = configuration.GetConnectionString("DefaultConnection");
    options.UseMySql(baseConnectionString, ServerVersion.AutoDetect(baseConnectionString));
    
    // Enable detailed logging in development
    if (configuration.GetValue<string>("Environment") == "Development")
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

    
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();

/*builder.Services.AddAuthentication(
        CertificateAuthenticationDefaults.AuthenticationScheme)
    .AddCertificate(options =>
    {
        options.AllowedCertificateTypes = CertificateTypes.All;
        options.Events = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = context =>
            {
                if (validationService.ValidateCertificate(context.ClientCertificate))
                {
                    context.Success();
                }
                else
                {
                    context.Fail("invalid cert");
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                context.Fail("invalid cert");
                return Task.CompletedTask;
            }
        };
    });
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.ConfigureHttpsDefaults(options =>
        options.ClientCertificateMode = ClientCertificateMode.RequireCertificate);
});*/
//builder.Services.AddSwaggerGen();
var storageType = builder.Configuration.GetValue<string>("Storage__Type") ?? 
                  Environment.GetEnvironmentVariable("STORAGE_TYPE") ?? 
                  "OwnCloud";
Console.WriteLine($"Detected storage type: {storageType}");
Console.WriteLine($"Environment STORAGE_TYPE: {Environment.GetEnvironmentVariable("STORAGE_TYPE")}");
Console.WriteLine($"Config Storage__Type: {builder.Configuration.GetValue<string>("Storage__Type")}");
if (storageType.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    // Configure S3 Storage
    builder.Services.Configure<S3Settings>(builder.Configuration.GetSection("S3Settings"));
    
    builder.Services.AddSingleton<IAmazonS3>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var s3Settings = configuration.GetSection("S3Settings");
        
        var config = new AmazonS3Config
        {
            ServiceURL = s3Settings["ServiceUrl"],
            ForcePathStyle = bool.Parse(s3Settings["ForcePathStyle"] ?? "true"),
            UseHttp = s3Settings["ServiceUrl"]?.StartsWith("http://") == true,
            RegionEndpoint = RegionEndpoint.GetBySystemName(s3Settings["Region"] ?? "us-east-1"),
            UseAccelerateEndpoint = false,
            UseDualstackEndpoint = false,
            DisableLogging = false
        };

        // For custom S3-compatible endpoints
        if (!string.IsNullOrEmpty(s3Settings["ServiceUrl"]))
        {
            config.ServiceURL = s3Settings["ServiceUrl"];
            config.ForcePathStyle = true;
        }

        var accessKey = s3Settings["AccessKey"] ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var secretKey = s3Settings["SecretKey"] ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");

        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
        {
            throw new InvalidOperationException("S3 credentials not configured. Please set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY.");
        }

        return new AmazonS3Client(accessKey, secretKey, config);
    });

    // Register S3 Storage Service
    builder.Services.AddScoped<IStorageService, S3StorageServiceImplementation>();
    
    Console.WriteLine("Configured for S3 storage");
}
else
{
    // Configure OwnCloud Storage (default for lab environment)
    builder.Services.AddScoped<IStorageService, OwnCloudStorageServiceImplementation>();
    
    Console.WriteLine("Configured for OwnCloud storage");
}
builder.Services.AddSwaggerGen(c =>
{
    // Define multiple Swagger documents, one for each logical grouping
    c.SwaggerDoc("tenants", new OpenApiInfo { Title = "1. Tenant Management", Version = "v1" });
    c.SwaggerDoc("programs", new OpenApiInfo { Title = "2. Training Program Management", Version = "v1" });
    c.SwaggerDoc("paths", new OpenApiInfo { Title = "3. Learning Path Management", Version = "v1" });
    c.SwaggerDoc("materials", new OpenApiInfo { Title = "4. Material Management", Version = "v1" });
    c.SwaggerDoc("assets", new OpenApiInfo { Title = "5. Asset Management", Version = "v1" });
    c.SwaggerDoc("users", new OpenApiInfo   { Title = "6. User Management", Version = "v1" });

    c.SwaggerDoc("all", new OpenApiInfo { 
        Title = "Complete XR50 Training Asset Repository API", 
        Version = "v1",
        Description = "Complete API documentation covering all controllers and endpoints"
    });

    c.DocumentFilter<HierarchicalOrderDocumentFilter>();

    // Define which controllers go into which document
    c.DocInclusionPredicate((docName, apiDesc) =>
    {
         if (docName == "all") return true;
        var controllerName = apiDesc.ActionDescriptor.RouteValues["controller"];
        
        return docName switch
        {
            "tenants" => controllerName.Contains("Tenants"),
            "programs" => controllerName.Contains("programs"),
            "paths" => controllerName.Contains("LearningPaths"),
            "materials" => controllerName.Contains("materials"),
            "assets" => controllerName.Contains("Assets"),
            "users" => controllerName.Contains("Users"),
            "test" => controllerName.Contains("test"),
            _ => false
        };
    });
});


builder.Configuration.AddJsonFile("appsettings.json");
builder.Services.AddCors(options =>{
            options.AddDefaultPolicy(
                builder =>
                {
                    builder
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowAnyOrigin();
                });
        });
var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCors();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
      {
          // Add endpoints in desired order
          c.SwaggerEndpoint("/swagger/tenants/swagger.json", "1. Tenant Management");
          c.SwaggerEndpoint("/swagger/programs/swagger.json", "2. Training Program Management");
          c.SwaggerEndpoint("/swagger/paths/swagger.json", "3. Learning Path Management");
          c.SwaggerEndpoint("/swagger/materials/swagger.json", "4. Material Management");
          c.SwaggerEndpoint("/swagger/assets/swagger.json", "5. Asset Management");
          c.SwaggerEndpoint("/swagger/users/swagger.json", "6. User Management");
          c.SwaggerEndpoint("/swagger/v1/swagger.json", "Default");
      });
}

app.MapControllers();
 

app.Run();

public class HierarchicalOrderDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Create ordered tags
        var orderedTags = new List<OpenApiTag>();

        // Define tag order
        var tagOrder = new Dictionary<string, int>
        {
            { "tenants", 1 },
            { "trainingPrograms", 2 },
            { "learningPaths", 3 },
            { "materials", 4 },
            { "assets", 5 },
            { "users", 6 }
        };

        // Add existing tags in order
        if (swaggerDoc.Tags != null)
        {
            // Create a new ordered collection, preserving all existing tags
            var existingTags = swaggerDoc.Tags.ToList();

            // Order the existing tags
            swaggerDoc.Tags = existingTags
                .OrderBy(t => tagOrder.ContainsKey(t.Name) ? tagOrder[t.Name] : 999)
                .ToList();
        }

        // Don't modify paths unless you need to - they're already ordered by URL
        // For ordering paths, create a similar approach but be careful to preserve all paths
    }
}
// Updated Program.cs Registration
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXR50MultitenancyWithDynamicDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IXR50TenantService, XR50TenantService>();
        services.AddScoped<ITrainingProgramService, TrainingProgramService>();
        services.AddScoped<IXR50TenantManagementService, XR50TenantManagementService>();
        services.AddScoped<XR50MigrationService>();
        services.AddScoped<IXR50DatabaseInitializer, XR50DatabaseInitializer>();
        services.AddScoped<IXR50TenantTroubleshootingService, XR50TenantTroubleshootingService>();
        services.AddScoped<IXR50ManualTableCreator, XR50ManualTableCreator>();
        services.AddScoped<IXR50TenantDbContextFactory, XR50TenantDbContextFactory>();
        services.AddScoped<ILearningPathService, LearningPathService>();
        services.AddScoped<IMaterialService, MaterialService>();
        services.AddScoped<IAssetService, AssetService>();

        // New specialized material services
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IMaterialServiceBase, XR50TrainingAssetRepo.Services.Materials.MaterialServiceBase>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IMaterialRelationshipService, XR50TrainingAssetRepo.Services.Materials.MaterialRelationshipService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IVideoMaterialService, XR50TrainingAssetRepo.Services.Materials.VideoMaterialService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IQuizMaterialService, XR50TrainingAssetRepo.Services.Materials.QuizMaterialService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IChecklistMaterialService, XR50TrainingAssetRepo.Services.Materials.ChecklistMaterialService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IWorkflowMaterialService, XR50TrainingAssetRepo.Services.Materials.WorkflowMaterialService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IQuestionnaireMaterialService, XR50TrainingAssetRepo.Services.Materials.QuestionnaireMaterialService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IImageMaterialService, XR50TrainingAssetRepo.Services.Materials.ImageMaterialService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.ISimpleMaterialService, XR50TrainingAssetRepo.Services.Materials.SimpleMaterialService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IVoiceMaterialService, XR50TrainingAssetRepo.Services.Materials.VoiceMaterialService>();
        services.AddScoped<XR50TrainingAssetRepo.Services.Materials.IUserMaterialService, XR50TrainingAssetRepo.Services.Materials.UserMaterialService>();

        // Chatbot API Service (HttpClient-based)
        services.AddHttpClient<IChatbotApiService, ChatbotApiService>();

        // Background service for AI status synchronization (database-driven, adaptive polling)
        services.AddHostedService<AiStatusSyncService>();

        // Keep the original DbContext registration for admin operations
        
        services.AddDbContext<XR50TrainingContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var baseConnectionString = configuration.GetConnectionString("DefaultConnection");
            options.UseMySql(baseConnectionString, ServerVersion.AutoDetect(baseConnectionString));

            if (configuration.GetValue<string>("Environment") == "Development")
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        return services;
    }

    // Extension method to initialize databases
    public static async Task<IApplicationBuilder> InitializeXR50DatabasesAsync(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IXR50DatabaseInitializer>();

        try
        {
            await initializer.InitializeAsync();
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Failed to initialize databases during startup");
            throw;
        }

        return app;
    }
}
public class S3Settings
{
    public string ServiceUrl { get; set; } = "";
    public string PublicEndpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string BaseBucketPrefix { get; set; } = "xr50";
    public bool ForcePathStyle { get; set; } = true;
}
public static class TenantDebuggingExtensions
{
    public static async Task<IApplicationBuilder> DebugTenantSetupAsync(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var baseConnectionString = configuration.GetConnectionString("DefaultConnection");
        var baseDatabaseName = configuration.GetValue<string>("BaseDatabaseName") ?? "magical_library";

        logger.LogInformation("=== TENANT SETUP DEBUG INFO ===");
        logger.LogInformation("Base Database Name: {BaseDatabaseName}", baseDatabaseName);
        logger.LogInformation("Base Connection String: {ConnectionString}",
            baseConnectionString?.Replace("Password=", "Password=***"));

        // Test main database connection
        try
        {
            using var scope2 = app.ApplicationServices.CreateScope();
            var context = scope2.ServiceProvider.GetRequiredService<XR50TrainingContext>();
            var canConnect = await context.Database.CanConnectAsync();
            logger.LogInformation("Can connect to main database: {CanConnect}", canConnect);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cannot connect to main database");
        }

        logger.LogInformation("=== END TENANT DEBUG INFO ===");

        return app;
    }
}

// JSON Converters for ID serialization
public class IntToStringConverter : System.Text.Json.Serialization.JsonConverter<int>
{
    public override int Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            if (int.TryParse(reader.GetString(), out var value))
                return value;
        }
        return reader.GetInt32();
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, int value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public class NullableIntToStringConverter : System.Text.Json.Serialization.JsonConverter<int?>
{
    public override int? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Null)
            return null;

        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (string.IsNullOrEmpty(stringValue))
                return null;
            if (int.TryParse(stringValue, out var value))
                return value;
        }
        return reader.GetInt32();
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, int? value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString());
        else
            writer.WriteNullValue();
    }
}

// Make Program accessible to test project
public partial class Program { }