# syntax=docker/dockerfile:1

# The Draft Room ships as a single container: the React SPA is built and served from the same
# origin as the .NET API, so the whole app lives behind one URL with no CORS. Deploy this image to
# any container host that injects a PORT (Render, Railway, Fly, ...) — the API binds to it.

# ---- Stage 1: build the React SPA ----
FROM node:22-alpine AS web
WORKDIR /web
# Restore node_modules first so this layer is cached until the lockfile changes.
COPY fc-draft-web/package.json fc-draft-web/package-lock.json ./
RUN npm ci
COPY fc-draft-web/ ./
RUN npm run build
# → produces /web/dist

# ---- Stage 2: restore, build & publish the .NET API ----
# Pinned to the SDK version in global.json so the pinned rollForward=latestPatch policy resolves.
FROM mcr.microsoft.com/dotnet/sdk:8.0.422 AS build
WORKDIR /src
# Restore against project files first so the NuGet layer is cached until a .csproj changes.
COPY global.json Directory.Build.props ./
COPY src/FcDraft.API/FcDraft.API.csproj src/FcDraft.API/
COPY src/FcDraft.Application/FcDraft.Application.csproj src/FcDraft.Application/
COPY src/FcDraft.Domain/FcDraft.Domain.csproj src/FcDraft.Domain/
COPY src/FcDraft.Infrastructure/FcDraft.Infrastructure.csproj src/FcDraft.Infrastructure/
RUN dotnet restore src/FcDraft.API/FcDraft.API.csproj
# Publish the API. The SPA is built in the web stage above, so the project's own frontend build
# target is skipped here to avoid needing Node in this image and building the SPA twice.
COPY src/ src/
RUN dotnet publish src/FcDraft.API/FcDraft.API.csproj \
    -c Release -o /app/publish \
    /p:SkipFrontendBuild=true /p:UseAppHost=false
# Serve the built SPA from the API's wwwroot (same origin as /api).
COPY --from=web /web/dist /app/publish/wwwroot

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_ENVIRONMENT=Production
# The app binds to $PORT when the platform sets it (Render/Railway); 8080 is the local default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "FcDraft.API.dll"]
