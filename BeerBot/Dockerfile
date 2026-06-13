FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY BeerBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BeerBot.dll"]
