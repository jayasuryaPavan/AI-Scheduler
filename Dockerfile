# ═══════════════════════════════════════════════════════════════
# AI Manufacturing Scheduler — Multi-Stage Dockerfile
# Produces a single container: Vue 3 SPA served by .NET 9 API
#
# Stages:
#   1. frontend-build  — npm ci + vite build → /frontend/dist
#   2. backend-build   — dotnet restore + publish (copies SPA into wwwroot)
#   3. runtime         — minimal ASP.NET runtime image
# ═══════════════════════════════════════════════════════════════

# ── Stage 1: Build Vue 3 frontend ──────────────────────────────
FROM node:20-alpine AS frontend-build
WORKDIR /frontend

# Install deps first (layer cache: only re-runs if package*.json changes)
COPY frontend/package*.json ./
RUN npm ci --silent

# Copy source and build
COPY frontend/ ./
RUN npm run build
# Output: /frontend/dist  (standard Vite output dir)


# ── Stage 2: Build .NET 9 backend ──────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS backend-build
WORKDIR /src

# Restore NuGet packages (layer cache)
COPY backend/AiScheduler.Api.csproj ./
RUN dotnet restore AiScheduler.Api.csproj

# Copy all backend source
COPY backend/ ./

# Copy agent source (compiled via Compile Include in .csproj)
COPY agent/ ../agent/

# Embed the compiled Vue SPA into wwwroot/
# The .NET app will serve these as static files via UseStaticFiles()
COPY --from=frontend-build /backend/wwwroot ./wwwroot/

# Publish release build
RUN dotnet publish AiScheduler.Api.csproj \
    --configuration Release \
    --output /publish \
    --no-restore


# ── Stage 3: Runtime image ──────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Cloud Run injects PORT env var (default 8080)
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# Copy published artifacts
COPY --from=backend-build /publish .

ENTRYPOINT ["dotnet", "AiScheduler.Api.dll"]
