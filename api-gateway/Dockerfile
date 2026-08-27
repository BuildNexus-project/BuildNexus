# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The project file alone first, so the restore layer is cached and only redone
# when a package reference actually changes — not on every source edit.
COPY BuildNexus.ApiGateway.csproj ./
RUN dotnet restore BuildNexus.ApiGateway.csproj

COPY . ./
RUN dotnet publish BuildNexus.ApiGateway.csproj -c Release -o /app/publish --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "BuildNexus.ApiGateway.dll"]
