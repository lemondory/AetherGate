FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY AetherGate.sln .
COPY src/AetherGate.Domain/AetherGate.Domain.csproj             src/AetherGate.Domain/
COPY src/AetherGate.Application/AetherGate.Application.csproj   src/AetherGate.Application/
COPY src/AetherGate.Infrastructure/AetherGate.Infrastructure.csproj src/AetherGate.Infrastructure/
COPY src/AetherGate.Server/AetherGate.Server.csproj             src/AetherGate.Server/

RUN dotnet restore

COPY . .
RUN dotnet publish src/AetherGate.Server/AetherGate.Server.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 9000 8090

ENTRYPOINT ["dotnet", "AetherGate.Server.dll"]
