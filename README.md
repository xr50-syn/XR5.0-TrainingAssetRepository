# XR5.0 Training Asset Repository - Installation & Testing Guide

## Table of Contents
1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Installation Options](#installation-options)
4. [Quick Start](#quick-start)
5. [Detailed Configuration](#detailed-configuration)
6. [Testing the Installation](#testing-the-installation)
7. [Troubleshooting](#troubleshooting)
8. [Support](#support)

## Overview

The XR5.0 Training Asset Repository is a cloud-based storage and management system for XR training materials, developed as part of the Horizon EU project XR5.0 (Grant Agreement No. 101135209). The repository supports multiple storage backends:

- **AWS S3** - For production environments with cloud storage
- **OwnCloud** - For lab/development environments with self-hosted storage
- **MinIO** - For sandbox S3 testing without AWS costs (for partners)

The system provides secure, scalable storage for training assets, including 3D models, documents, videos, and XR applications used in the XR5.0 Training Platform.

## Prerequisites For S3 Deployment

### AWS Account Setup
- AWS Account with S3 access
- AWS Access Key ID and Secret Access Key
- Appropriate IAM permissions for bucket operations (see below)

### S3 Bucket Pre-Provisioning (REQUIRED)

**IMPORTANT**: The application does NOT create S3 buckets automatically. All buckets must be pre-provisioned before creating tenants.

#### Bucket Naming Convention

When creating a tenant, you must specify an existing S3 bucket name in the tenant configuration. The recommended naming convention is:

```
{prefix}-tenant-{tenant-name}
```

Where:
- `{prefix}` is your base bucket prefix (default: `xr50`, configurable via `S3_BASE_BUCKET_PREFIX`)
- `{tenant-name}` is the sanitized tenant name (lowercase, alphanumeric and hyphens only)

**Examples**:
- Tenant: "demo-company" → Bucket: `xr50-tenant-demo-company`
- Tenant: "pilot1" → Bucket: `xr50-tenant-pilot1`
- Tenant: "Acme Corp" → Bucket: `xr50-tenant-acme-corp` (sanitized)

#### Required IAM Permissions

Your AWS credentials must have the following S3 permissions on the pre-provisioned buckets:
- `s3:GetObject`
- `s3:PutObject`
- `s3:DeleteObject`
- `s3:ListBucket`
- `s3:GetBucketLocation`

The application does NOT require:
- `s3:CreateBucket` (buckets must be pre-created)
- `s3:DeleteBucket` (optional, only if you want to allow tenant deletion)

### For Lab (OwnCloud) Deployment
- No additional requirements (all services run in containers)

### For Sandbox (MinIO) Deployment
- Docker and Docker Compose
- No AWS account needed
- Buckets must be created using the provided script or via MinIO Console
- Data persists across container restarts (stored in Docker volume)

## Installation Options

The repository supports three deployment profiles:

| Profile | Storage Backend | Use Case |
|---------|----------------|----------|
| `prod` | AWS S3 | Production environments with cloud storage |
| `lab` | OwnCloud | Development/testing with self-hosted storage |
| `sandbox` | MinIO | S3 sandbox testing without AWS costs (for partners) |

## Quick Start

### 1. Clone the Repository
```bash
git clone https://github.com/xr50-syn/XR5.0-TrainingAssetRepository
.git
cd XR5.0-TrainingAssetRepository

```

### 2. Configure Environment Variables
Modify the `.env` file in the project root with your configuration:

#### For S3 Production Deployment:
```env
# Storage Configuration
STORAGE_TYPE=S3

# AWS S3 Settings
AWS_ACCESS_KEY_ID=your_access_key_here
AWS_SECRET_ACCESS_KEY=your_secret_key_here
AWS_REGION=eu-west-1

# Database Configuration
XR50_REPO_DB_USER=xr50admin
XR50_REPO_DB_PASSWORD=secure_password_here
XR50_REPO_DB_NAME=xr50_repository

# Application Settings
ASPNETCORE_ENVIRONMENT=Production
```

#### For OwnCloud Lab Deployment:
```env
# Storage Configuration
STORAGE_TYPE=OwnCloud

# OwnCloud Settings
OWNCLOUD_ADMIN_USER=admin
OWNCLOUD_ADMIN_PASSWORD=admin_password_here
OWNCLOUD_DB_USER=owncloud
OWNCLOUD_DB_PASSWORD=owncloud_password_here
OWNCLOUD_TRUSTED_DOMAINS=localhost,owncloud,your_server_ip,your_domain.com

# Database Configuration
XR50_REPO_DB_USER=xr50admin
XR50_REPO_DB_PASSWORD=secure_password_here
XR50_REPO_DB_NAME=xr50_repository

# Application Settings
ASPNETCORE_ENVIRONMENT=Development
```

#### For LocalStack Sandbox Deployment:
```env
# Storage Configuration
STORAGE_TYPE=S3

# LocalStack AWS Settings (Sandbox - no real AWS costs)
AWS_HOST=http://localstack:4566
AWS_ACCESS_KEY_ID=test
AWS_SECRET_ACCESS_KEY=test
AWS_REGION=eu-west-1

# S3 Settings
S3_BASE_BUCKET_PREFIX=xr50-sandbox
S3_FORCE_PATH_STYLE=true

# Database Configuration
XR50_REPO_DB_USER=sandbox_user
XR50_REPO_DB_PASSWORD=sandbox_password
XR50_REPO_DB_NAME=magical_library

# Application Settings
ASPNETCORE_ENVIRONMENT=Development
```

> Note: For sandbox testing, use the provided `.env.sandbox` file or copy the configuration above. LocalStack emulates AWS services locally, so you won't incur any AWS costs.

### 3. Start the Services

#### Production with S3:
```bash
docker-compose --profile prod up -d
```

#### Lab with OwnCloud:
```bash
docker-compose --profile lab up -d
```

#### Sandbox with MinIO (S3 Testing):
```bash
# Copy the sandbox environment file
cp .env.sandbox .env

# Start with sandbox profile
docker-compose --profile sandbox up -d

# Create sample buckets (run after containers start)
chmod +x sandbox-init-buckets.sh
./sandbox-init-buckets.sh
```

Access MinIO Console at http://localhost:9001 (minioadmin/minioadmin)

The init script creates these sample S3 buckets:
- `xr50-sandbox-tenant-demo`
- `xr50-sandbox-tenant-pilot1`
- `xr50-sandbox-tenant-pilot2`

### 4. Verify Installation
Wait for all services to start (approximately 30-60 seconds), then verify:

- **Repository API**: http://localhost:5286/swagger
- **OwnCloud** (lab profile only): http://localhost:8080
- **MinIO Console** (sandbox profile only): http://localhost:9001


## Detailed Configuration

### Environment Variables Reference

#### Core Settings
| Variable | Description | Default |
|----------|-------------|---------|
| `STORAGE_TYPE` | Storage backend type: `S3`, `OwnCloud`, or `MinIO` | `OwnCloud` |
| `ASPNETCORE_ENVIRONMENT` | Application environment: `Development` or `Production` | `Development` |

#### S3 Configuration
| Variable | Description | Required for S3 |
|----------|-------------|-----------------|
| `AWS_ACCESS_KEY_ID` | AWS Access Key ID | Yes |
| `AWS_SECRET_ACCESS_KEY` | AWS Secret Access Key | Yes |
| `AWS_REGION` | AWS Region | Yes (default: `eu-west-1`) |
| `S3_BASE_BUCKET_PREFIX` | Prefix for S3 bucket names | Yes (default: `xr50`) |
| `S3_FORCE_PATH_STYLE` | Use path-style URLs | No (default: `false`) |

#### OwnCloud Configuration
| Variable | Description | Default |
|----------|-------------|---------|
| `OWNCLOUD_ADMIN_USER` | OwnCloud admin username | `admin` |
| `OWNCLOUD_ADMIN_PASSWORD` | OwnCloud admin password | `admin` |
| `OWNCLOUD_DB_USER` | OwnCloud database user | `owncloud` |
| `OWNCLOUD_DB_PASSWORD` | OwnCloud database password | `owncloud` |
| `OWNCLOUD_TRUSTED_DOMAINS` | Comma-separated list of trusted domains | `localhost,owncloud` |

#### Database Configuration
| Variable | Description | Default |
|----------|-------------|---------|
| `XR50_REPO_DB_USER` | Repository database user | `xr50admin` |
| `XR50_REPO_DB_PASSWORD` | Repository database password | Required |
| `XR50_REPO_DB_NAME` | Repository database name | `xr50_repository` |

#### MinIO Configuration (Sandbox Profile)
| Variable | Description | Default |
|----------|-------------|---------|
| `AWS_HOST` | MinIO endpoint URL | `http://minio:9000` |
| `AWS_ACCESS_KEY_ID` | MinIO access key | `minioadmin` |
| `AWS_SECRET_ACCESS_KEY` | MinIO secret key | `minioadmin` |
| `MINIO_ROOT_USER` | MinIO console username | `minioadmin` |
| `MINIO_ROOT_PASSWORD` | MinIO console password | `minioadmin` |
| `S3_FORCE_PATH_STYLE` | Use path-style URLs (required for MinIO) | `true` |

**Note**: MinIO provides full data persistence. Files are stored in Docker volume `minio_data` and survive container restarts.

### Network Configuration

For the `OWNCLOUD_TRUSTED_DOMAINS` variable, include all possible ways to access the server:
- `localhost` - Always include
- `owncloud` - Docker service name, always include
- Your server's IP address (e.g., `192.168.1.100`)
- Your server's hostname (e.g., `xr50-server`)
- Your domain name (e.g., `xr50.example.com`)

Example:
```env
OWNCLOUD_TRUSTED_DOMAINS=localhost,owncloud,192.168.1.100,xr50-server,xr50.example.com
```

## Testing the Installation

### 1. Health Check
Verify all containers are running:
```bash
docker-compose ps
```

Expected output should show all services as "Up" or "healthy".

### 2. API Testing

#### Access Swagger UI
Navigate to http://localhost:5286/swagger

#### Create a Test Tenant
Use the Swagger UI to create a test tenant:

1. Expand the **1. Tenant Management** section
2. Click on `POST /api/tenants/create`
3. Click "Try it out"
4. Use this example request:

For S3 (ensure bucket is pre-created in AWS):
```json
{
  "tenantName": "test-company",
  "tenantGroup": "pilot-1",
  "description": "Test tenant for Pilot 1",
  "storageType": "S3",
  "s3Config": {
    "bucketName": "xr50-sandbox-tenant-pilot5",
    "bucketRegion": "eu-west-1"
  },
  "owner": {
    "userName": "testadmin",
    "fullName": "Test Administrator",
    "userEmail": "admin@test-company.com",
    "password": "SecurePass123!",
    "admin": true
  }
}
```

**IMPORTANT**: The bucket `xr50-tenant-test-company` must already exist in your AWS account before creating this tenant. The application will verify the bucket exists but will NOT create it.

For OwnCloud:
```json
{
  "tenantName": "test-company",
  "tenantGroup": "pilot-1",
  "description": "Test tenant for Pilot 1",
  "storageType": "OwnCloud",
  "ownCloudConfig": {
    "tenantDirectory": "test-company-files"
  },
  "owner": {
    "userName": "testadmin",
    "fullName": "Test Administrator",
    "userEmail": "admin@test-company.com",
    "password": "SecurePass123!",
    "admin": true
  }
}
```

5. Click "Execute"
6. Verify you receive a 200 response

### 3. Storage Verification

#### For S3:

First, ensure your bucket is created:

**Using AWS Console:**
1. Go to AWS S3 Console
2. Click "Create bucket"
3. Enter bucket name (e.g., `xr50-tenant-test-company`)
4. Select region (e.g., `eu-west-1`)
5. Keep default settings or adjust as needed
6. Click "Create bucket"

**Using AWS CLI:**
```bash
# Create the bucket
aws s3 mb s3://xr50-tenant-test-company --region eu-west-1

# Verify it exists
aws s3 ls s3://xr50-tenant-test-company/
```

**Note**: In production, you typically create buckets with specific settings (encryption, versioning, lifecycle policies, etc.) using infrastructure-as-code tools like Terraform or CloudFormation.

#### For OwnCloud:
1. Access OwnCloud at http://localhost:8080
2. Login with the admin credentials from your `.env` file
3. Verify the tenant directory has been created

#### For MinIO (Sandbox):
Check that the bucket exists using AWS CLI or MinIO Console:

**Via AWS CLI**:
```bash
# List all buckets
aws --endpoint-url=http://localhost:9000 s3 ls

# List contents of specific bucket
aws --endpoint-url=http://localhost:9000 s3 ls s3://xr50-sandbox-tenant-demo/ --recursive
```

**Via MinIO Console** (easier):
1. Go to http://localhost:9001
2. Login with minioadmin/minioadmin
3. Browse buckets and files visually

You can also verify MinIO health:
```bash
curl http://localhost:9000/minio/health/live
```

### 4. Upload Test Asset
Use the Asset Management endpoints in Swagger to upload a test file:

1. Navigate to **5. Asset Management**
2. Use `POST /api/assets/upload` to upload a test file
3. Verify the file appears in the storage backend

## Troubleshooting

### Common Issues and Solutions

#### 1. Container Fails to Start
**Problem**: Docker containers exit immediately after starting

**Solution**:
- Check logs: `docker-compose logs [service-name]`
- Verify all required environment variables are set
- Ensure ports 5286, 8080 (lab), 3306 are not already in use

#### 2. Database Connection Errors
**Problem**: "Connection refused" or "Access denied" errors

**Solution**:
- Wait 60 seconds for database initialization
- Verify database credentials match in all configuration locations
- Check MariaDB container logs: `docker-compose logs mariadb`

#### 3. S3 Bucket Does Not Exist
**Problem**: Tenant creation fails with "S3 bucket does NOT exist" error

**Solution**:
The application does NOT create buckets automatically. You must pre-provision them:
1. Create the bucket in AWS S3 Console or using AWS CLI:
   ```bash
   aws s3 mb s3://xr50-tenant-yourname --region eu-west-1
   ```
2. Verify the bucket name in your tenant creation request matches exactly
3. Ensure your AWS credentials have access to the bucket

#### 4. S3 Access Denied
**Problem**: AWS S3 operations fail with permission errors

**Solution**:
- Verify AWS credentials are correct
- Ensure IAM user has appropriate S3 permissions (GetObject, PutObject, DeleteObject, ListBucket, GetBucketLocation)
- Check bucket naming follows convention: `xr50-tenant-[name]`
- Verify bucket exists and is in the correct region

#### 5. OwnCloud Access Issues
**Problem**: Cannot access OwnCloud web interface

**Solution**:
- Verify `OWNCLOUD_TRUSTED_DOMAINS` includes your access method
- Wait for OwnCloud initialization (can take 2-3 minutes on first run)
- Check OwnCloud logs: `docker-compose logs owncloud`

### Viewing Logs
```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f training-repo
docker-compose logs -f mariadb
docker-compose logs -f owncloud  # lab profile only
```

### Resetting the Installation
To completely reset and start fresh:

```bash
# Stop all containers
docker-compose down

# Remove volumes (WARNING: Deletes all data)
docker-compose down -v

# Remove all containers and images
docker-compose down --rmi all

# Start fresh
docker-compose --profile [lab|prod|minio] up --build
```


**Source Code**: https://github.com/xr50-syn/XR5.0-TrainingAssetRepository

## Support
For any issues contact Emmanouil Mavrogiorgis (emaurog@synelixis.com)

## License

MIT License

---

*This project has received funding from the European Union's Horizon Europe Research and Innovation Programme under grant agreement no 101135209.*
