Write-Host "=========================================="
Write-Host "MinIO Sandbox: Creating Sample S3 Buckets"
Write-Host "=========================================="
Write-Host ""
Write-Host "This script creates sample buckets in MinIO for sandbox testing."
Write-Host ""

# Set MinIO credentials for this session.
$env:AWS_ACCESS_KEY_ID = "minioadmin"
$env:AWS_SECRET_ACCESS_KEY = "minioadmin"

# Wait for MinIO to be fully ready.
Write-Host "Waiting for MinIO to be ready..."
Start-Sleep -Seconds 5

# Sample buckets to create.
$buckets = @(
  "xr50-sandbox-tenant-demo",
  "xr50-sandbox-tenant-pilot4",
  "xr50-sandbox-tenant-pilot5"
)

Write-Host ""
Write-Host "Creating buckets..."
Write-Host ""

foreach ($bucket in $buckets) {
  $bucketExists = $false

  # Check if bucket already exists.
  & aws --endpoint-url=http://localhost:9000 s3 ls "s3://$bucket" 2>$null
  if ($LASTEXITCODE -eq 0) {
    Write-Host "  Bucket $bucket already exists"
    $bucketExists = $true
  } else {
    Write-Host "  Creating bucket: $bucket"
    & aws --endpoint-url=http://localhost:9000 --region us-east-1 s3 mb "s3://$bucket" 2>$null
    if ($LASTEXITCODE -eq 0) {
      Write-Host "  Successfully created bucket: $bucket"
      $bucketExists = $true
    } else {
      Write-Host "  Failed to create bucket: $bucket"
      Write-Host "    Make sure:"
      Write-Host "    - MinIO is running (docker-compose --profile sandbox up -d)"
      Write-Host "    - AWS CLI is configured with MinIO credentials"
      Write-Host "    - You can access http://localhost:9000"
      $bucketExists = $false
    }
  }

  # Make bucket publicly accessible for testing.
  if ($bucketExists) {
    Write-Host "  Setting public read access for testing..."

    $policyPath = Join-Path $env:TEMP ("public-policy-{0}.json" -f $bucket)
    @"
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {"AWS": "*"},
      "Action": ["s3:GetObject"],
      "Resource": ["arn:aws:s3:::$bucket/*"]
    }
  ]
}
"@ | Set-Content -Path $policyPath -Encoding ASCII

    $policyUri = (Get-Item $policyPath).FullName.Replace('\', '/')
    $policyParam = "file://$policyUri"
    $policyCommand = "aws --endpoint-url=http://localhost:9000 s3api put-bucket-policy --bucket `"$bucket`" --policy `"$policyParam`""
    Write-Host "  Command: $policyCommand"
    & aws --endpoint-url=http://localhost:9000 s3api put-bucket-policy --bucket "$bucket" --policy "$policyParam" 2>$null
    if ($LASTEXITCODE -eq 0) {
      Write-Host "  Bucket $bucket is now publicly readable"
    } else {
      Write-Host "  Warning: Could not set public policy for $bucket"
    }

    Remove-Item -Path $policyPath -Force -ErrorAction SilentlyContinue
  }
}

Write-Host ""
Write-Host "=========================================="
Write-Host "Listing all S3 buckets in MinIO:"
Write-Host "=========================================="
& aws --endpoint-url=http://localhost:9000 s3 ls

Write-Host ""
Write-Host "=========================================="
Write-Host "MinIO Sandbox Setup Complete!"
Write-Host "=========================================="
Write-Host ""
Write-Host "Created buckets:"
foreach ($bucket in $buckets) {
  Write-Host "  - $bucket"
}
Write-Host ""
Write-Host "You can now:"
Write-Host "  - Access MinIO Console: http://localhost:9001"
Write-Host "  - Login with: minioadmin / minioadmin"
Write-Host "  - Use Swagger API: http://localhost:5286/swagger"
Write-Host ""
Write-Host "To create additional buckets:"
Write-Host "  aws --endpoint-url=http://localhost:9000 s3 mb s3://your-bucket-name"
Write-Host ""
Write-Host "=========================================="
