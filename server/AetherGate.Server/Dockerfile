FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY AetherGate.sln .
COPY AetherGate.Domain/AetherGate.Domain.csproj             AetherGate.Domain/
COPY AetherGate.Application/AetherGate.Application.csproj   AetherGate.Application/
COPY AetherGate.Infrastructure/AetherGate.Infrastructure.csproj AetherGate.Infrastructure/
COPY AetherGate.Server/AetherGate.Server.csproj             AetherGate.Server/

RUN dotnet restore

COPY . .
RUN dotnet publish AetherGate.Server/AetherGate.Server.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 9000 8090

ENTRYPOINT ["dotnet", "AetherGate.Server.dll"]
