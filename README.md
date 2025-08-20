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
- **MinIO** - For local S3-compatible testing (WIP)

The system provides secure, scalable storage for training assets, including 3D models, documents, videos, and XR applications used in the XR5.0 Training Platform.

## Prerequisites For S3 Deployment
- AWS Account with S3 access
- AWS Access Key ID and Secret Access Key
- Pre-provisioned S3 bucket(s) following naming convention: `xr50-tenant-[name]`
- Appropriate IAM permissions for bucket operations

### For Lab (OwnCloud) Deployment
- No additional requirements (all services run in containers)

## Installation Options

The repository supports three deployment profiles:

| Profile | Storage Backend | Use Case |
|---------|----------------|----------|
| `prod` | AWS S3 | Production environments with cloud storage |
| `lab` | OwnCloud | Development/testing with self-hosted storage |
| `minio` | MinIO | Local S3-compatible testing | (Still in progress)

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

### 3. Start the Services

#### Production with S3:
```bash
docker-compose --profile prod up -d
```

#### Lab with OwnCloud:
```bash
docker-compose --profile lab up -d
```


### 4. Verify Installation
Wait for all services to start (approximately 30-60 seconds), then verify:

- **Repository API**: http://localhost:5286/swagger
- **OwnCloud** (lab profile only): http://localhost:8080


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

For S3:
```json
{
  "tenantName": "test-company",
  "tenantGroup": "pilot-1",
  "description": "Test tenant for Pilot 1",
  "storageType": "S3",
  "s3Config": {
    "bucketName": "xr50-tenant-test-company",
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
Check that the bucket exists in your AWS S3 console or using AWS CLI:
```bash
aws s3 ls s3://xr50-tenant-test-company/
```

#### For OwnCloud:
1. Access OwnCloud at http://localhost:8080
2. Login with the admin credentials from your `.env` file
3. Verify the tenant directory has been created

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

#### 3. S3 Access Denied
**Problem**: AWS S3 operations fail with permission errors

**Solution**:
- Verify AWS credentials are correct
- Ensure IAM user has appropriate S3 permissions
- Check bucket naming follows convention: `xr50-tenant-[name]`
- Verify bucket exists and is in the correct region

#### 4. OwnCloud Access Issues
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
