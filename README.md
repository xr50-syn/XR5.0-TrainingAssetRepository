# XR5.0 Training Asset Repository

Multi-tenant, cloud-agnostic storage platform for Extended Reality (XR) training materials.

Developed as part of the **Horizon Europe XR5.0 project** (Grant Agreement No. 101135209).

## Quick Start

```bash
# Clone repository
git clone https://github.com/xr50-syn/XR5.0-TrainingAssetRepository.git
cd XR5.0-TrainingAssetRepository

# Start sandbox environment (MinIO + MariaDB + Keycloak)
cp .env.sandbox .env
docker-compose --profile sandbox up -d

# Create sample buckets
chmod +x sandbox-init-buckets.sh && ./sandbox-init-buckets.sh

# Access
# - API Swagger: http://localhost:5286/swagger
# - MinIO Console: http://localhost:9001 (minioadmin/minioadmin)
# - Keycloak: http://localhost:8180 (admin/admin)
```

## Documentation

See [docs/README.md](docs/README.md) for complete documentation.

| Section | Description |
|---------|-------------|
| [API Reference](docs/README.md#api-reference) | Materials, User Progress, Assets |
| [Authentication](docs/guides/authentication.md) | Keycloak JWT setup |
| [Setup Guides](docs/README.md#setup) | Sandbox, MinIO, Docker |
| [Architecture](docs/architecture.md) | System design, multi-tenancy |

## Deployment Profiles

| Profile | Storage | Use Case |
|---------|---------|----------|
| `sandbox` | MinIO | Local testing (no AWS costs) |
| `lab` | OwnCloud | Development with WebDAV storage |
| `prod` | AWS S3 | Production with cloud storage |

```bash
docker-compose --profile sandbox up -d   # MinIO
docker-compose --profile lab up -d       # OwnCloud
docker-compose --profile prod up -d      # AWS S3
```

## Technology Stack

- **Backend**: ASP.NET Core 8.0
- **Database**: MySQL/MariaDB with Entity Framework Core
- **Storage**: AWS S3, OwnCloud, MinIO
- **Auth**: Keycloak (JWT Bearer)
- **API Docs**: OpenAPI/Swagger

## License

MIT License - See [LICENSE.md](LICENSE.md)

## Support

Contact: Emmanouil Mavrogiorgis (emaurog@synelixis.com)

---

*This project has received funding from the European Union's Horizon Europe Research and Innovation Programme under grant agreement no 101135209.*
