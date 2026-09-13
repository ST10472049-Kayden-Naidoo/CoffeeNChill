# Multi-stage build for optimization
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /src

# Copy project files
COPY ["FunctionApp1.csproj", "./"]
RUN dotnet restore "FunctionApp1.csproj"

# Copy remaining source code
COPY . .

# Build the project
RUN dotnet build "FunctionApp1.csproj" -c Release -o /app/build

# Publish stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY --from=builder /app/build .
RUN dotnet publish "FunctionApp1.csproj" -c Release -o /app/publish

# Runtime stage - use Azure Functions base image
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4.0-dotnet-isolated10.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

WORKDIR /home/site/wwwroot
COPY --from=publish /app/publish .

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost/health || exit 1
