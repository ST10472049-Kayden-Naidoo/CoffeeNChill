# CoffeeNChill — Part 1 (simple)

This branch contains a minimal implementation for Part 1 required by the assignment:
- Azure Table "Menultems" (Menu items) via Azurite
- Blob-based staff-docs (upload/list/download)
- Azure Functions (isolated worker) simple endpoints
- Dockerfile for standalone function image
- Postman collection in /docs

Quick start
1. Start Azurite:
   docker run -d --name azurite -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite

2. Run functions locally:
   cd CafeDemo.Functions
   dotnet nuget locals all --clear
   dotnet restore
   dotnet build
   dotnet run

3. Import Postman collection in docs and run using baseUrl http://localhost:7071

Notes
- Uses Blob storage (Azurite) as a local substitute for Azure Files (documented for local testing).
- Keep commits granular for the rubric (split tasks into logical commits if needed).
