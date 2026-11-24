FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY IrrigacaoConfig.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV DataProtection__KeysPath=/app/keys
EXPOSE 5080
ENTRYPOINT ["dotnet", "IrrigacaoConfig.dll"]
