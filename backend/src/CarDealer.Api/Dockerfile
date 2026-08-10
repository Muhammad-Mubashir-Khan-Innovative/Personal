# Build context is the backend/ directory (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so restore is cached independently of source changes.
COPY Directory.Build.props ./
COPY src/CarDealer.Domain/CarDealer.Domain.csproj src/CarDealer.Domain/
COPY src/CarDealer.Application/CarDealer.Application.csproj src/CarDealer.Application/
COPY src/CarDealer.Infrastructure/CarDealer.Infrastructure.csproj src/CarDealer.Infrastructure/
COPY src/CarDealer.Integrations/CarDealer.Integrations.csproj src/CarDealer.Integrations/
COPY src/CarDealer.Api/CarDealer.Api.csproj src/CarDealer.Api/

RUN dotnet restore src/CarDealer.Api/CarDealer.Api.csproj

COPY src/ src/
RUN dotnet publish src/CarDealer.Api/CarDealer.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as the non-root user the base image provides.
USER $APP_UID

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "CarDealer.Api.dll"]
