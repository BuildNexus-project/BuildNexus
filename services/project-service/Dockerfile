# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ProjectService.csproj ./
RUN dotnet restore ProjectService.csproj

COPY . ./
RUN dotnet publish ProjectService.csproj -c Release -o /app/publish --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "BuildNexus.ProjectService.dll"]
