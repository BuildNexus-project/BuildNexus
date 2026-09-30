# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PaymentService.csproj ./
RUN dotnet restore PaymentService.csproj

COPY . ./
RUN dotnet publish PaymentService.csproj -c Release -o /app/publish --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "BuildNexus.PaymentService.dll"]
