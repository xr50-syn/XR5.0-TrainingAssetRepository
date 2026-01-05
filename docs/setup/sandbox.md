# Sandbox Setup Guide for Partners

This guide helps you set up a local S3-compatible sandbox environment for testing the XR5.0 Training Asset Repository **without any AWS costs**.

## What is MinIO?

MinIO is a high-performance, S3-compatible object storage system that runs locally. It provides full AWS S3 API compatibility and allows you to test the repository without:
- Creating an AWS account
- Incurring any AWS charges
- Needing internet connectivity (after initial setup)
- **Bonus**: Includes a web-based console for easy file management!

## Prerequisites

- Docker and Docker Compose installed on your system
- At least 4GB of available RAM
- 10GB of free disk space

## Quick Setup (3 Steps)

### Step 1: Get the Repository
```bash
# Clone or extract the repository
cd XR5.0-TrainingAssetRepository
```

### Step 2: Configure Environment
```bash
# Copy the sandbox environment configuration
cp .env.sandbox .env
```

### Step 3: Start the Sandbox
```bash
# Start all services with sandbox profile
docker-compose --profile sandbox up -d

# Wait 30-60 seconds for services to initialize
```

## Verify Installation

### Check Service Health
```bash
# Check all containers are running
docker-compose ps

# Verify MinIO is healthy
curl http://localhost:9000/minio/health/live

# Check Repository API
curl http://localhost:5286/health
```

### Create Sample S3 Buckets

**IMPORTANT**: The application does NOT create S3 buckets automatically. All buckets must be pre-provisioned.

Run the provided script to create sample buckets:

```bash
# Make the script executable
chmod +x sandbox-init-buckets.sh

# Run the bucket creation script
./sandbox-init-buckets.sh
```

This creates the following buckets in MinIO:
- `xr50-sandbox-tenant-demo`
- `xr50-sandbox-tenant-pilot1`
- `xr50-sandbox-tenant-pilot2`

**Alternative**: Create buckets manually via MinIO Console or AWS CLI:
```bash
# Using AWS CLI
aws --endpoint-url=http://localhost:9000 s3 mb s3://xr50-sandbox-tenant-demo
aws --endpoint-url=http://localhost:9000 s3 mb s3://xr50-sandbox-tenant-pilot1
aws --endpoint-url=http://localhost:9000 s3 mb s3://xr50-sandbox-tenant-pilot2

# Verify buckets were created
aws --endpoint-url=http://localhost:9000 s3 ls
```

**Note**: In a real AWS environment, you would need to create these buckets manually or through infrastructure-as-code before creating tenants.

### Access the Services
- **Repository API (Swagger)**: http://localhost:5286/swagger
- **MinIO Console (Web UI)**: http://localhost:9001 (Login: minioadmin/minioadmin)
- **MinIO API**: http://localhost:9000

## Testing the Repository

### 1. Create a Test Tenant

Open Swagger UI at http://localhost:5286/swagger and use the tenant creation endpoint:

**POST** `/api/tenants/create`

Example request body (using pre-created bucket):
```json
{
  "tenantName": "demo-company",
  "tenantGroup": "pilot-1",
  "description": "Demo tenant using pre-created bucket",
  "storageType": "S3",
  "s3Config": {
    "bucketName": "xr50-sandbox-tenant-demo",
    "bucketRegion": "eu-west-1"
  },
  "owner": {
    "userName": "demoadmin",
    "fullName": "Demo Administrator",
    "userEmail": "admin@demo-company.com",
    "password": "SecurePass123!",
    "admin": true
  }
}
```

**IMPORTANT**: The bucket must already exist! Use one of the pre-created buckets:
- `xr50-sandbox-tenant-demo`
- `xr50-sandbox-tenant-pilot1`
- `xr50-sandbox-tenant-pilot2`

If you need additional buckets, you can create them:

**Option 1 - Via MinIO Console (Easiest)**:
1. Go to http://localhost:9001
2. Login with minioadmin/minioadmin
3. Click "Create Bucket"
4. Enter bucket name (e.g., `xr50-sandbox-tenant-newcompany`)

**Option 2 - Via AWS CLI**:
```bash
aws --endpoint-url=http://localhost:9000 s3 mb s3://xr50-sandbox-tenant-newcompany
```

### 2. Upload Test Assets

Use the Asset Management endpoints in Swagger to upload test files and experiment with the API.

### 3. Verify Data in MinIO

**Option 1 - Via MinIO Console (Visual)**:
1. Go to http://localhost:9001
2. Login with minioadmin/minioadmin
3. Click on your bucket
4. Browse files visually, download, upload, etc.

**Option 2 - Via AWS CLI**:
```bash
# List all S3 buckets
aws --endpoint-url=http://localhost:9000 s3 ls

# List contents of a specific bucket
aws --endpoint-url=http://localhost:9000 s3 ls s3://xr50-sandbox-tenant-demo/

# List with details
aws --endpoint-url=http://localhost:9000 s3 ls s3://xr50-sandbox-tenant-demo/ --recursive

# Download a file
aws --endpoint-url=http://localhost:9000 s3 cp s3://xr50-sandbox-tenant-demo/myfile.pdf ./
```

## Important Notes

### Sandbox vs Production
- **Sandbox credentials**: minioadmin/minioadmin (MinIO defaults)
- **Production credentials**: Must be valid AWS credentials
- **Sandbox endpoint**: http://minio:9000 (inside Docker) or http://localhost:9000 (from host)
- **Production endpoint**: Uses real AWS endpoints

### Data Persistence ✅

**MinIO provides excellent data persistence!** Your uploaded files will survive container restarts.

Data is stored in a Docker volume named `minio_data` and persists until you explicitly delete it.

**To verify persistence**:
```bash
# Upload a file via Swagger

# Stop the sandbox
docker-compose --profile sandbox down

# Restart the sandbox
docker-compose --profile sandbox up -d

# File is still there!
aws --endpoint-url=http://localhost:9000 s3 ls s3://your-bucket/ --recursive
```

**To reset and start fresh** (WARNING: Deletes all data):
```bash
# Stop containers and remove volumes
docker-compose --profile sandbox down -v

# Start fresh
docker-compose --profile sandbox up -d

# Recreate buckets
./sandbox-init-buckets.sh
```

## Troubleshooting

### Bucket Does Not Exist Error

**Problem**: Tenant creation fails with "S3 bucket does NOT exist" error.

**Solution**:
The application requires buckets to be pre-provisioned. This is by design for security and control.

1. Check if the bucket exists:
```bash
aws --endpoint-url=http://localhost:9000 s3 ls
```

2. If missing, create the bucket via MinIO Console or CLI:
```bash
aws --endpoint-url=http://localhost:9000 s3 mb s3://your-bucket-name
```

3. Verify the bucket name in your tenant creation request matches exactly.

### Cannot Access MinIO Console

**Problem**: http://localhost:9001 doesn't load.

**Solution**:
```bash
# Check if MinIO is running
docker-compose ps | grep minio

# Check MinIO logs
docker-compose logs minio

# Restart MinIO
docker-compose restart minio
```

### Services Won't Start
```bash
# Check logs
docker-compose logs -f

# Check specific service
docker-compose logs training-repo
docker-compose logs minio
```

### Port Conflicts
If ports 9000, 9001, 5286, or 3306 are already in use:
```bash
# Find what's using the port (example for port 9000)
netstat -ano | findstr :9000    # Windows
lsof -i :9000                    # Linux/Mac
```

### Reset Everything
```bash
# Complete reset
docker-compose --profile sandbox down -v
rm -rf .localstack
docker-compose --profile sandbox up -d --build
```

## MinIO Features

MinIO provides:
- ✅ Full S3 API compatibility
- ✅ Excellent data persistence (unlike LocalStack free version)
- ✅ Web-based console for easy file management
- ✅ High performance
- ✅ 100% free and open source
- ✅ Production-ready (used by many companies)

All S3 features needed for this repository work perfectly with MinIO!

## Support

For issues related to:
- **Repository setup**: Contact Emmanouil Mavrogiorgis (emaurog@synelixis.com)
- **MinIO**: Visit https://min.io/docs/minio/linux/index.html

## Next Steps

1. Explore the API through Swagger UI
2. Create multiple tenants to test multi-tenancy
3. Upload various asset types (documents, 3D models, videos)
4. Test user management and permissions
5. Experiment with the storage operations

---

**Remember**: This is a sandbox environment. No real AWS resources are created, and you won't be charged anything!
