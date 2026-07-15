FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/InternalResourceStore.Domain/InternalResourceStore.Domain.csproj src/InternalResourceStore.Domain/
COPY src/InternalResourceStore.Application/InternalResourceStore.Application.csproj src/InternalResourceStore.Application/
COPY src/InternalResourceStore.Configuration/InternalResourceStore.Configuration.csproj src/InternalResourceStore.Configuration/
COPY src/InternalResourceStore.Infrastructure/InternalResourceStore.Infrastructure.csproj src/InternalResourceStore.Infrastructure/
COPY src/InternalResourceStore.Api/InternalResourceStore.Api.csproj src/InternalResourceStore.Api/
RUN dotnet restore src/InternalResourceStore.Api/InternalResourceStore.Api.csproj

COPY . .
RUN dotnet publish src/InternalResourceStore.Api/InternalResourceStore.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /data/resources
EXPOSE 8080
ENTRYPOINT ["dotnet", "InternalResourceStore.Api.dll"]
