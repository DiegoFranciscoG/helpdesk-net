# Front Blazor WebAssembly servido por nginx sin privilegios. Contexto de build: raíz del repositorio.
#   docker build -f src/Helpdesk.Web/Dockerfile -t helpdesk-net-web .
# La URL de la API se inyecta al arrancar (HELPDESK_API_URL): la misma imagen sirve para local y producción.

# 1) Publicar la app WebAssembly (archivos estáticos ya comprimidos en .br y .gz).
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Helpdesk.Domain/Helpdesk.Domain.csproj src/Helpdesk.Domain/
COPY src/Helpdesk.Contracts/Helpdesk.Contracts.csproj src/Helpdesk.Contracts/
COPY src/Helpdesk.Web/Helpdesk.Web.csproj src/Helpdesk.Web/
RUN dotnet restore src/Helpdesk.Web/Helpdesk.Web.csproj
COPY src/Helpdesk.Domain/ src/Helpdesk.Domain/
COPY src/Helpdesk.Contracts/ src/Helpdesk.Contracts/
COPY src/Helpdesk.Web/ src/Helpdesk.Web/
# El appsettings.json de desarrollo no se publica: nginx lo genera desde el entorno.
RUN dotnet publish src/Helpdesk.Web/Helpdesk.Web.csproj -c Release -o /out --no-restore \
    && rm -f /out/wwwroot/appsettings.json /out/wwwroot/appsettings.json.br /out/wwwroot/appsettings.json.gz

# 2) nginx 1.30 (rama estable) sin privilegios: usuario 101, puerto 8080.
FROM nginxinc/nginx-unprivileged:1.30.5-alpine3.24
COPY --chmod=0755 deploy/nginx/05-helpdesk-env.envsh /docker-entrypoint.d/05-helpdesk-env.envsh
COPY deploy/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /out/wwwroot /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 CMD ["wget", "-q", "--spider", "http://127.0.0.1:8080/healthz"]
