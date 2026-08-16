FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LocalMediaService.Web.csproj .
RUN dotnet restore LocalMediaService.Web.csproj
COPY . .
RUN dotnet publish LocalMediaService.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    MediaLibrary__RootPath=/media \
    Storage__DataPath=/data
EXPOSE 8080
RUN mkdir -p /data && chown app:app /data
COPY --from=build --chown=app:app /app/publish .
USER app
ENTRYPOINT ["dotnet", "LocalMediaService.Web.dll"]
