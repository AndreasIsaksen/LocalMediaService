FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish LocalMediaService.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV MEDIA_ROOT=/media
EXPOSE 8080
VOLUME ["/media"]
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LocalMediaService.Web.dll"]
