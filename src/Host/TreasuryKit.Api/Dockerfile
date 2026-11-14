# syntax=docker/dockerfile:1
# Build from the repository root:  docker build -f src/Host/TreasuryKit.Api/Dockerfile -t treasurykit-api .

# 1. The web UI. `npm run build` writes to ../src/Host/TreasuryKit.Api/wwwroot relative to web/.
FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# 2. The Api, with the UI in wwwroot.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
# The demo seed files embedded in the Api (the same synthetic data the tests use).
COPY tests/TestData/recon/expected.csv tests/TestData/recon/statement.csv tests/TestData/recon/
COPY --from=web /src/src/Host/TreasuryKit.Api/wwwroot src/Host/TreasuryKit.Api/wwwroot
RUN dotnet publish src/Host/TreasuryKit.Api -c Release -o /app

# 3. Runtime: non-root, port 8080 (the image default).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir -p /data/files && chown app:app /data/files
COPY --from=build /app ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "TreasuryKit.Api.dll"]
