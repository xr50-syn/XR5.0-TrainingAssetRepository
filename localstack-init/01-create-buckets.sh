#!/bin/bash

echo "=========================================="
echo "LocalStack Init: Creating Sample S3 Buckets"
echo "=========================================="

# Wait a moment to ensure S3 service is fully ready
sleep 2

# Create sample buckets for testing
BUCKETS=(
  "xr50-sandbox-tenant-demo"
  "xr50-sandbox-tenant-pilot1"
  "xr50-sandbox-tenant-pilot2"
)

for BUCKET in "${BUCKETS[@]}"; do
  # Check if bucket already exists (to preserve data from previous runs)
  if awslocal s3 ls "s3://$BUCKET" 2>/dev/null; then
    echo "Bucket $BUCKET already exists (preserving existing data)"
  else
    echo "Creating bucket: $BUCKET"
    if awslocal s3 mb "s3://$BUCKET" 2>/dev/null; then
      echo "  ✓ Successfully created bucket: $BUCKET"

      # Set bucket to allow public ACLs (optional, useful for testing)
      awslocal s3api put-public-access-block \
        --bucket "$BUCKET" \
        --public-access-block-configuration \
        "BlockPublicAcls=false,IgnorePublicAcls=false,BlockPublicPolicy=false,RestrictPublicBuckets=false" \
        2>/dev/null && echo "  ✓ Configured bucket access settings"
    else
      echo "  ✗ Failed to create bucket: $BUCKET"
    fi
  fi
done

echo ""
echo "=========================================="
echo "Listing all S3 buckets:"
echo "=========================================="
awslocal s3 ls

echo ""
echo "=========================================="
echo "LocalStack S3 Initialization Complete!"
echo "=========================================="
echo "Created buckets:"
for BUCKET in "${BUCKETS[@]}"; do
  echo "  - $BUCKET"
done
echo ""
echo "You can now create tenants using these bucket names."
echo "IMPORTANT: The application does NOT create buckets automatically."
echo "If you need more buckets, add them to this script or create them manually with 'awslocal s3 mb'."
echo "=========================================="
