FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src

COPY ["src/Neftyanik.Portal.Web/Neftyanik.Portal.Web.csproj", "src/Neftyanik.Portal.Web/"]
COPY ["src/Neftyanik.Portal.Application/Neftyanik.Portal.Application.csproj", "src/Neftyanik.Portal.Application/"]
COPY ["src/Neftyanik.Portal.Infrastructure/Neftyanik.Portal.Infrastructure.csproj", "src/Neftyanik.Portal.Infrastructure/"]
COPY ["src/Neftyanik.Portal.Domain/Neftyanik.Portal.Domain.csproj", "src/Neftyanik.Portal.Domain/"]
RUN dotnet restore "src/Neftyanik.Portal.Web/Neftyanik.Portal.Web.csproj"

COPY src ./src

FROM restore AS build
WORKDIR /src/src/Neftyanik.Portal.Web
RUN dotnet build "Neftyanik.Portal.Web.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "Neftyanik.Portal.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-build

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=publish /app/publish .

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/aspnet-data-protection/keys \
    && chown -R $APP_UID:0 /app /var/aspnet-data-protection \
    && chmod -R g=u /app /var/aspnet-data-protection

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Neftyanik.Portal.Web.dll"]
