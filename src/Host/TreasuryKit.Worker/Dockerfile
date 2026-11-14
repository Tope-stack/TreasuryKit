# syntax=docker/dockerfile:1
# Build from the repository root:  docker build -f src/Host/TreasuryKit.Worker/Dockerfile -t treasurykit-worker .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Host/TreasuryKit.Worker -c Release -o /app

# The Worker shares building blocks with the Api that use the ASP.NET Core framework, hence the aspnet runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir -p /data/files && chown app:app /data/files
COPY --from=build /app ./
USER app
ENTRYPOINT ["dotnet", "TreasuryKit.Worker.dll"]
