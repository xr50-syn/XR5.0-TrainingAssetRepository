#!/bin/bash

echo "=========================================="
echo "MinIO Sandbox: Creating Sample S3 Buckets"
echo "=========================================="
echo ""
echo "This script creates sample buckets in MinIO for sandbox testing."
echo ""

# Set MinIO credentials
export AWS_ACCESS_KEY_ID=minioadmin
export AWS_SECRET_ACCESS_KEY=minioadmin

# Wait for MinIO to be fully ready
echo "Waiting for MinIO to be ready..."
sleep 5

# Sample buckets to create
BUCKETS=(
  "xr50-sandbox-tenant-demo"
  "xr50-sandbox-tenant-pilot1"
  "xr50-sandbox-tenant-pilot2"
)

echo ""
echo "Creating buckets..."
echo ""

for BUCKET in "${BUCKETS[@]}"; do
  # Check if bucket already exists
  if aws --endpoint-url=http://localhost:9000 s3 ls "s3://$BUCKET" 2>/dev/null; then
    echo "✓ Bucket $BUCKET already exists"
  else
    echo "  Creating bucket: $BUCKET"
    if aws --endpoint-url=http://localhost:9000 \
       --region us-east-1 \
       s3 mb "s3://$BUCKET" 2>/dev/null; then
      echo "  ✓ Successfully created bucket: $BUCKET"
    else
      echo "  ✗ Failed to create bucket: $BUCKET"
      echo "    Make sure:"
      echo "    - MinIO is running (docker-compose --profile sandbox up -d)"
      echo "    - AWS CLI is configured with MinIO credentials"
      echo "    - You can access http://localhost:9000"
    fi
  fi
done

echo ""
echo "=========================================="
echo "Listing all S3 buckets in MinIO:"
echo "=========================================="
aws --endpoint-url=http://localhost:9000 s3 ls

echo ""
echo "=========================================="
echo "MinIO Sandbox Setup Complete!"
echo "=========================================="
echo ""
echo "Created buckets:"
for BUCKET in "${BUCKETS[@]}"; do
  echo "  - $BUCKET"
done
echo ""
echo "You can now:"
echo "  - Access MinIO Console: http://localhost:9001"
echo "  - Login with: minioadmin / minioadmin"
echo "  - Use Swagger API: http://localhost:5286/swagger"
echo ""
echo "To create additional buckets:"
echo "  aws --endpoint-url=http://localhost:9000 s3 mb s3://your-bucket-name"
echo ""
echo "=========================================="
