# Sandbox Setup Guide for Partners

This guide helps you set up a local AWS sandbox environment for testing the XR5.0 Training Asset Repository **without any AWS costs**.

## What is LocalStack?

LocalStack is a fully functional local AWS cloud stack that emulates AWS services (S3, DynamoDB, Lambda, SQS, SNS, etc.) on your local machine. This allows you to test the repository without:
- Creating an AWS account
- Incurring any AWS charges
- Needing internet connectivity (after initial setup)

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

# Verify LocalStack is healthy
curl http://localhost:4566/_localstack/health

# Check Repository API
curl http://localhost:5286/health
```

### Pre-created S3 Buckets

**IMPORTANT**: The application does NOT create S3 buckets automatically. All buckets must be pre-provisioned.

For the sandbox environment, this is handled automatically by init scripts. On startup, the following buckets are created in LocalStack:
- `xr50-sandbox-tenant-demo`
- `xr50-sandbox-tenant-pilot1`
- `xr50-sandbox-tenant-pilot2`

You can verify these buckets exist:
```bash
# View init script logs
docker-compose logs localstack | grep "Creating bucket"

# List all buckets (if you have awslocal installed)
awslocal s3 ls

# Or using standard AWS CLI
aws --endpoint-url=http://localhost:4566 s3 ls
```

**Note**: In a real AWS environment, you would need to create these buckets manually or through infrastructure-as-code before creating tenants.

### Access the Services
- **Repository API (Swagger)**: http://localhost:5286/swagger
- **LocalStack Gateway**: http://localhost:4566
- **LocalStack Health**: http://localhost:4566/_localstack/health

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

If you need additional buckets, you can create them manually:
```bash
# Using awslocal
awslocal s3 mb s3://xr50-sandbox-tenant-newcompany

# Or using AWS CLI
aws --endpoint-url=http://localhost:4566 s3 mb s3://xr50-sandbox-tenant-newcompany
```

Or add them to the init script in `localstack-init/01-create-buckets.sh` and restart LocalStack.

### 2. Upload Test Assets

Use the Asset Management endpoints in Swagger to upload test files and experiment with the API.

### 3. Verify Data in LocalStack

Install AWS CLI and awslocal (optional but helpful):
```bash
pip install awscli-local
```

Then you can interact with LocalStack like real AWS:
```bash
# List all S3 buckets
awslocal s3 ls

# List contents of a specific bucket
awslocal s3 ls s3://xr50-sandbox-tenant-test-company/

# Copy a file to S3
awslocal s3 cp myfile.txt s3://xr50-sandbox-tenant-test-company/
```

## Important Notes

### Sandbox vs Production
- **Sandbox credentials**: Any values work (default: test/test)
- **Production credentials**: Must be valid AWS credentials
- **Sandbox endpoint**: http://localstack:4566
- **Production endpoint**: Uses real AWS endpoints

### Data Persistence
The sandbox configuration enables persistence, meaning your data will be saved even after stopping the containers. Data is stored in the `.localstack` directory.

To reset and start fresh:
```bash
# Stop containers
docker-compose --profile sandbox down

# Remove all data (optional)
rm -rf .localstack

# Start fresh
docker-compose --profile sandbox up -d
```

## Troubleshooting

### Bucket Does Not Exist Error

**Problem**: Tenant creation fails with "S3 bucket does NOT exist" error.

**Solution**:
The application requires buckets to be pre-provisioned. This is by design for security and control.

1. Check if the bucket exists:
```bash
awslocal s3 ls
```

2. If missing, create the bucket:
```bash
awslocal s3 mb s3://your-bucket-name
```

3. Verify the bucket name in your tenant creation request matches exactly.

### Services Won't Start
```bash
# Check logs
docker-compose logs -f

# Check specific service
docker-compose logs training-repo
docker-compose logs localstack
```

### Port Conflicts
If ports 4566, 5286, or 3306 are already in use:
```bash
# Find what's using the port (example for port 4566)
netstat -ano | findstr :4566    # Windows
lsof -i :4566                    # Linux/Mac
```

### Reset Everything
```bash
# Complete reset
docker-compose --profile sandbox down -v
rm -rf .localstack
docker-compose --profile sandbox up -d --build
```

## Limitations

LocalStack Community Edition (free) supports:
- S3 (object storage)
- DynamoDB (NoSQL database)
- SQS (message queuing)
- SNS (notifications)
- Lambda (serverless functions)
- And many more...

Some advanced AWS features require LocalStack Pro (paid), but all features needed for this repository work with the free Community Edition.

## Support

For issues related to:
- **Repository setup**: Contact Emmanouil Mavrogiorgis (emaurog@synelixis.com)
- **LocalStack**: Visit https://docs.localstack.cloud/

## Next Steps

1. Explore the API through Swagger UI
2. Create multiple tenants to test multi-tenancy
3. Upload various asset types (documents, 3D models, videos)
4. Test user management and permissions
5. Experiment with the storage operations

---

**Remember**: This is a sandbox environment. No real AWS resources are created, and you won't be charged anything!
