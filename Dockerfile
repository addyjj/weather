# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/Weather.WebApi/Weather.WebApi.csproj -c Release -o /out/webapi \
    && dotnet publish src/Weather.Mcp/Weather.Mcp.csproj -c Release -o /out/mcp \
    && dotnet publish src/Weather.Import/Weather.Import.csproj -c Release -o /out/import

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS migrations-build
WORKDIR /source
COPY .config/dotnet-tools.json ./.config/dotnet-tools.json
RUN dotnet tool restore
COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet ef migrations bundle --project src/Weather.Infrastructure --configuration Release \
    --self-contained --target-runtime linux-x64 --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
EXPOSE 8080

FROM runtime AS webapi
COPY --from=build /out/webapi/ ./
ENTRYPOINT ["dotnet", "Weather.WebApi.dll"]

FROM runtime AS mcp
COPY --from=build /out/mcp/ ./
ENTRYPOINT ["dotnet", "Weather.Mcp.dll"]

FROM runtime AS import
COPY --from=build /out/import/ ./
ENTRYPOINT ["dotnet", "Weather.Import.dll"]

FROM runtime AS migrate
COPY --from=migrations-build /out/efbundle ./efbundle
ENTRYPOINT ["./efbundle"]
