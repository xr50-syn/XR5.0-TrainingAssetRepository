# MinIO Public Bucket Access Guide

## For Partners: Making Buckets Temporarily Public

This guide shows how to make MinIO buckets publicly accessible so files can be downloaded directly from laptops without authentication.

⚠️ **Note**: This is temporary until S3 owner authentication is integrated. Only use for internal testing.

---

## Prerequisites

- MinIO is running on the VM at `http://192.168.190.33:9000`
- You have access to the VM or MinIO Web Console
- Files uploaded through the application will return URLs like: `http://192.168.190.33:9000/bucket-name/file-name`

---

## Method 1: Using MinIO Client (mc) - Recommended

### Windows

1. **Download MinIO Client**
   ```powershell
   curl -o mc.exe https://dl.min.io/client/mc/release/windows-amd64/mc.exe
   ```

2. **Configure Connection**
   ```powershell
   mc.exe alias set myminio http://192.168.190.33:9000 minioadmin minioadmin
   ```

3. **Make Bucket Public**
   ```powershell
   # Replace BUCKET_NAME with your actual bucket (e.g., xr50-sandbox-tenant-pilot5)
   mc.exe anonymous set download myminio/BUCKET_NAME
   ```

4. **Verify**
   ```powershell
   mc.exe anonymous get myminio/BUCKET_NAME
   ```

### Linux/Mac

1. **Download MinIO Client**
   ```bash
   curl -o mc https://dl.min.io/client/mc/release/linux-amd64/mc
   chmod +x mc
   ```

2. **Configure Connection**
   ```bash
   ./mc alias set myminio http://192.168.190.33:9000 minioadmin minioadmin
   ```

3. **Make Bucket Public**
   ```bash
   ./mc anonymous set download myminio/BUCKET_NAME
   ```

---

## Method 2: Using AWS CLI

### Setup (One-time)

```bash
aws configure set aws_access_key_id minioadmin
aws configure set aws_secret_access_key minioadmin
aws configure set region us-east-1
```

### Make Bucket Public

1. **Create policy file** (`public-policy.json`):
   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Principal": {"AWS": "*"},
         "Action": ["s3:GetObject"],
         "Resource": ["arn:aws:s3:::BUCKET_NAME/*"]
       }
     ]
   }
   ```
   Replace `BUCKET_NAME` with your actual bucket name.

2. **Apply policy**:
   ```bash
   aws --endpoint-url http://192.168.190.33:9000 s3api put-bucket-policy --bucket BUCKET_NAME --policy file://public-policy.json
   ```

---

## Method 3: Using MinIO Web Console

1. Open browser to `http://192.168.190.33:9001`
2. Login:
   - Username: `minioadmin`
   - Password: `minioadmin`
3. Navigate to **Buckets** → Click your bucket name
4. Look for **Access Policy** or **Anonymous** settings
5. Set to **Public** or **Download** (read-only)

*Note: The exact location of this setting varies by MinIO version*

---

## Common Bucket Names

Based on environment:

- **Sandbox**: `xr50-sandbox-tenant-{tenant-name}`
  - Example: `xr50-sandbox-tenant-pilot5`
- **Local Testing**: `xr50-test-tenant-{tenant-name}`
  - Example: `xr50-test-tenant-demo`

---

## Testing Access

After making a bucket public, test with curl:

```bash
curl http://192.168.190.33:9000/BUCKET_NAME/FILE_NAME
```

If successful, you'll see the file contents. If still getting `AccessDenied`, the policy hasn't been applied correctly.

---

## Troubleshooting

### "Access Denied" Error
- Verify bucket name is correct
- Check that policy was applied: `mc anonymous get myminio/BUCKET_NAME`
- Ensure MinIO is accessible at `http://192.168.190.33:9000`

### "Connection Refused"
- Verify MinIO is running: `docker ps | grep minio`
- Check firewall allows port 9000
- Ensure you're using the correct VM IP

### Files Still Not Accessible
- Check file actually exists in bucket
- Verify the full URL path matches: `http://192.168.190.33:9000/{bucket}/{filename}`
- Try accessing through MinIO Console first

---

## Reverting to Private

To make a bucket private again:

```bash
mc.exe anonymous set none myminio/BUCKET_NAME
```

Or using AWS CLI:

```bash
aws --endpoint-url http://192.168.190.33:9000 s3api delete-bucket-policy --bucket BUCKET_NAME
```

---

## Security Notes

- ⚠️ Public buckets mean **anyone** with the URL can download files
- This is intended for **internal testing only**
- Do not use for production or sensitive data
- Once S3 owner auth is integrated, this will be replaced with proper authentication
