# XR5.0 Training Asset Repository - Documentation

This documentation is organized for **frontend developers** integrating with the XR5.0 Training API.

## API Reference

| Document | Description |
|----------|-------------|
| [Materials API](api/materials-api.md) | Complete materials API contract - request/response formats for all material types (quiz, checklist, workflow, video, image, PDF) |
| [User Progress API](api/user-progress-api.md) | Quiz submission, scoring, and progress tracking endpoints |

## Guides

| Document | Description |
|----------|-------------|
| [Authentication](guides/authentication.md) | Keycloak JWT authentication setup and token usage |
| [Usage Examples](guides/usage-examples.md) | Complete workflow examples for common scenarios |
| [AI Voice Processing](guides/ai-voice-processing.md) | Voice material and asset AI processing integration |

## Setup

| Document | Description |
|----------|-------------|
| [Sandbox Setup](setup/sandbox.md) | Local MinIO sandbox for S3-compatible testing |
| [MinIO Public Access](setup/minio-public-access.md) | Temporary public bucket access for testing |

## Architecture

| Document | Description |
|----------|-------------|
| [Architecture Overview](architecture.md) | System architecture, multi-tenancy, storage backends, development guide |

## Quick Links

- **Swagger UI**: http://localhost:5286/swagger
- **API Base URL**: `http://localhost:5286/api/{tenantName}/`
- **Changelog**: [../CHANGELOG.md](../CHANGELOG.md)

## Key Concepts

### Tenant-Scoped API
All endpoints are scoped to a tenant:
```
/api/{tenantName}/materials
/api/{tenantName}/programs
/api/{tenantName}/assets
```

### Authentication
JWT tokens from Keycloak. See [Authentication Guide](guides/authentication.md).

User ID is extracted from token claims in this order:
1. `preferred_username`
2. `name`
3. `email`
4. `sub` (UUID fallback)

### Material Types
- `quiz` - Questions with scoring
- `checklist` - Checkable items
- `workflow` - Step-by-step procedures
- `video` - Video with timestamps
- `image` - Image assets
- `pdf` - PDF documents
- `questionnaire` - Survey/feedback forms

## Support

Contact: Emmanouil Mavrogiorgis (emaurog@synelixis.com)
