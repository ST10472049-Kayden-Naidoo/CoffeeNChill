# CoffeeNChill Canteen Management System - Part 1

## Overview
This is a cloud-enabled microservices system for the CoffeeNChill campus canteen, replacing paper-based menus and filing cabinets with Azure Storage and serverless Azure Functions.

## Local Setup & Execution

### Prerequisites
- Docker Desktop installed
- .NET 10.0 SDK
- Visual Studio or VS Code
- Postman (for API testing)

### Running Locally with Docker Compose

1. **Clone the repository and navigate to project root:**
   ```bash
   git clone <repository-url>
   cd CoffeeNChill
   ```

2. **Build and run all services with Docker Compose:**
   ```bash
   docker-compose up --build
   ```

   This will:
   - Start Azurite storage emulator on ports 10000-10002
   - Build and run the Azure Functions on port 7071
   - Establish networking between containers

3. **Verify services are running:**
   - Azurite: http://localhost:10000
   - Azure Functions: http://localhost:7071/api

### Standalone Docker Execution (Without Compose)

**Step 1: Start Azurite**
```bash
docker run -d --name azurite \
  -p 10000:10000 \
  -p 10001:10001 \
  -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite:latest
```

**Step 2: Build Functions image**
```bash
docker build -t coffeenchill-functions:v1.0 .
```

**Step 3: Run Functions container**
```bash
docker run -p 7071:80 \
  -e AzureWebJobsStorage="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://host.docker.internal:10001/devstoreaccount1;TableEndpoint=http://host.docker.internal:10000/devstoreaccount1;QueueEndpoint=http://host.docker.internal:10002/devstoreaccount1;" \
  coffeenchill-functions:v1.0
```

## API Endpoints

### Menu Management
- **POST** `/api/menu` - Create menu item (AuthorizationLevel.Function)
- **GET** `/api/menu` - Get all menu items (AuthorizationLevel.Anonymous)
- **GET** `/api/menu/category/{category}` - Get items by category (AuthorizationLevel.Anonymous)
- **PUT** `/api/menu/{category}/{id}` - Update menu item (AuthorizationLevel.Function)
- **DELETE** `/api/menu/{category}/{id}` - Delete menu item (AuthorizationLevel.Function)

### Document Management
- **POST** `/api/documents/upload` - Upload staff document (AuthorizationLevel.Function)
- **GET** `/api/documents` - List all documents (AuthorizationLevel.Function)
- **GET** `/api/documents/download/{fileName}` - Download document (AuthorizationLevel.Function)

## Testing with Postman

Import the Postman collection from `/docs/CoffeeNChill-Postman-Collection.json` to test all endpoints with automated tests.

## Architecture Notes

### Storage Implementation
The assignment specified Azure File Share (`staff-docs`) for document storage. However, Azurite (the local storage emulator) does not support Azure Files. Therefore:
- **Menu items** are stored in Azure Table Storage (Menultems table) ✓
- **Documents** are stored in Azure Blob Storage (documents container) as a functional substitute for local development
- Document metadata is tracked in Azure Table Storage (Document table)

In production, switching to Azure Files would be a simple configuration change without affecting the API contract.

### Security Considerations
- Write operations (Create, Update, Delete) require `AuthorizationLevel.Function`
- Read operations allow `Anonymous` access (can be restricted based on business requirements)
- File uploads are restricted to 20MB maximum
- Allowed file types: .jpg, .png, .pdf, .docx, .txt, .rtf

## Team Contributions

| Team Member | Contributions | Commits |
|-------------|---------------|----------|
| ST10472049-Kayden-Naidoo | Code fixes, Docker setup, README, Postman collection | [See Git history] |

## Docker Hub Repository

Images are tagged and published to Docker Hub:
- Function App: `ST10472049/coffeenchill-functions:v1.0`
- Azurite: Official `mcr.microsoft.com/azure-storage/azurite:latest`

## Video Demonstration

YouTube video demonstrating:
- Docker containers running with `docker run` and `docker-compose` commands
- Postman collection executing all endpoints
- Document upload, list, and download functionality
- Menu CRUD operations with validation

[YouTube Link - Unlisted]

## References

- Oladipo - IIE(2026) cldv6212-http-triggered-functions. Available at: https://github.com/Oladipo-IIE/cldv6212-http-triggered-functions (Accessed: 12 September 2026)
- Microsoft Azure Functions Worker SDK Documentation
- Azure Storage Blob Client Library for .NET
- Azure Storage Table Client Library for .NET