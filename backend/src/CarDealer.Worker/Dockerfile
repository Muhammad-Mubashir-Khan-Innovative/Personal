# Build context is the backend/ directory (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/CarDealer.Domain/CarDealer.Domain.csproj src/CarDealer.Domain/
COPY src/CarDealer.Application/CarDealer.Application.csproj src/CarDealer.Application/
COPY src/CarDealer.Infrastructure/CarDealer.Infrastructure.csproj src/CarDealer.Infrastructure/
COPY src/CarDealer.Integrations/CarDealer.Integrations.csproj src/CarDealer.Integrations/
COPY src/CarDealer.Worker/CarDealer.Worker.csproj src/CarDealer.Worker/

RUN dotnet restore src/CarDealer.Worker/CarDealer.Worker.csproj

COPY src/ src/
RUN dotnet publish src/CarDealer.Worker/CarDealer.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

USER $APP_UID

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CarDealer.Worker.dll"]
