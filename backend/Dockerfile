# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /source

# Copy solution and project definitions for fast caching
COPY SchoolAdmission.sln ./
COPY backend/src/SchoolAdmission.Domain/SchoolAdmission.Domain.csproj backend/src/SchoolAdmission.Domain/
COPY backend/src/SchoolAdmission.Application/SchoolAdmission.Application.csproj backend/src/SchoolAdmission.Application/
COPY backend/src/SchoolAdmission.Infrastructure/SchoolAdmission.Infrastructure.csproj backend/src/SchoolAdmission.Infrastructure/
COPY backend/src/SchoolAdmission.Api/SchoolAdmission.Api.csproj backend/src/SchoolAdmission.Api/
COPY backend/tests/SchoolAdmission.Tests/SchoolAdmission.Tests.csproj backend/tests/SchoolAdmission.Tests/

# Restore dependencies
RUN dotnet restore backend/src/SchoolAdmission.Api/SchoolAdmission.Api.csproj

# Copy all source files
COPY backend/src/ backend/src/

# Publish Release build
WORKDIR /source/backend/src/SchoolAdmission.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Run as non-root user for enterprise container security
USER $APP_UID

COPY --from=build /app/publish ./

# Expose port 8080 as mandated by system requirements
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]
