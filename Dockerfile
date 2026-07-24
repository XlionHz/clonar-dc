FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ClonarDC.Server/ClonarDC.Server.csproj src/ClonarDC.Server/
RUN dotnet restore src/ClonarDC.Server/ClonarDC.Server.csproj

COPY src/ClonarDC.Server/ src/ClonarDC.Server/
RUN dotnet publish src/ClonarDC.Server/ClonarDC.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    /p:DebugType=None \
    /p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

USER app
ENTRYPOINT ["dotnet", "ClonarDC.Server.dll"]