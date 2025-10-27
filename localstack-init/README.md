# LocalStack Initialization Scripts

This directory contains scripts that run automatically when LocalStack starts up.

## How It Works

- Scripts in this directory are mounted to `/etc/localstack/init/ready.d/` in the LocalStack container
- LocalStack executes all `.sh` scripts in this directory once the services are ready
- Scripts are executed in alphabetical order (hence the `01-` prefix)

## Current Scripts

### 01-create-buckets.sh
Creates sample S3 buckets for testing:
- `xr50-sandbox-tenant-demo`
- `xr50-sandbox-tenant-pilot1`
- `xr50-sandbox-tenant-pilot2`

## Customizing Buckets

To create different buckets, edit `01-create-buckets.sh` and modify the `BUCKETS` array:

```bash
BUCKETS=(
  "xr50-sandbox-tenant-demo"
  "xr50-sandbox-tenant-pilot1"
  "xr50-sandbox-tenant-pilot2"
  "your-custom-bucket-name"
)
```

After making changes:
1. Restart the LocalStack container: `docker-compose --profile sandbox restart localstack`
2. Or rebuild completely: `docker-compose --profile sandbox down && docker-compose --profile sandbox up -d`

## Adding More Init Scripts

You can add additional initialization scripts for other AWS services:

### Example: Create SQS Queue
Create `02-create-queues.sh`:
```bash
#!/bin/bash
echo "Creating SQS queues..."
awslocal sqs create-queue --queue-name xr50-notifications
awslocal sqs create-queue --queue-name xr50-processing
```

### Example: Create DynamoDB Table
Create `03-create-tables.sh`:
```bash
#!/bin/bash
echo "Creating DynamoDB tables..."
awslocal dynamodb create-table \
  --table-name xr50-sessions \
  --attribute-definitions AttributeName=id,AttributeType=S \
  --key-schema AttributeName=id,KeyType=HASH \
  --billing-mode PAY_PER_REQUEST
```

## Troubleshooting

### Scripts Not Running
Check the LocalStack logs:
```bash
docker-compose logs localstack
```

Look for initialization messages like "Creating bucket: ..."

### Script Errors
- Ensure scripts are executable: Files should have proper permissions
- Check line endings: Scripts must use LF (Unix) line endings, not CRLF (Windows)
- Verify syntax: Test scripts locally with `bash -n script.sh`

### Buckets Not Appearing
```bash
# Check if buckets were created
awslocal s3 ls

# Or using standard AWS CLI
aws --endpoint-url=http://localhost:4566 s3 ls
```

## Resources

- [LocalStack Init Hooks Documentation](https://docs.localstack.cloud/references/init-hooks/)
- [AWS CLI LocalStack](https://github.com/localstack/awscli-local)
