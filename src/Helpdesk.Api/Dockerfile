# API de helpdesk-net. Contexto de build: raíz del repositorio.
#   docker build -f src/Helpdesk.Api/Dockerfile -t helpdesk-net-api .
# Todas las imágenes con versión exacta (nunca "latest").

# 1) Compilar y publicar con el SDK de .NET 10. Primero solo los .csproj: la restauración queda en caché.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Helpdesk.Domain/Helpdesk.Domain.csproj src/Helpdesk.Domain/
COPY src/Helpdesk.Contracts/Helpdesk.Contracts.csproj src/Helpdesk.Contracts/
COPY src/Helpdesk.Application/Helpdesk.Application.csproj src/Helpdesk.Application/
COPY src/Helpdesk.Infrastructure/Helpdesk.Infrastructure.csproj src/Helpdesk.Infrastructure/
COPY src/Helpdesk.Api/Helpdesk.Api.csproj src/Helpdesk.Api/
RUN dotnet restore src/Helpdesk.Api/Helpdesk.Api.csproj
COPY src/Helpdesk.Domain/ src/Helpdesk.Domain/
COPY src/Helpdesk.Contracts/ src/Helpdesk.Contracts/
COPY src/Helpdesk.Application/ src/Helpdesk.Application/
COPY src/Helpdesk.Infrastructure/ src/Helpdesk.Infrastructure/
COPY src/Helpdesk.Api/ src/Helpdesk.Api/
RUN dotnet publish src/Helpdesk.Api/Helpdesk.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false \
    && mkdir -p /storage/attachments

# 2) Runtime: ASP.NET Core 10.0.12 sobre Ubuntu Noble "chiseled" (sin shell ni gestor de paquetes, usuario
#    no root "app"). La variante "-extra" conserva ICU y tzdata: la API calcula el SLA en America/Guayaquil.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
# Carpeta de adjuntos fuera de la base de datos, propiedad del usuario sin privilegios (montar un volumen aquí).
COPY --from=build --chown=$APP_UID:$APP_UID /storage /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__AttachmentsPath=/data/attachments \
    DOTNET_gcServer=0
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 CMD ["dotnet", "Helpdesk.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Helpdesk.Api.dll"]
