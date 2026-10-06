# Política de seguridad

## Reportar una vulnerabilidad

No abras un issue público. Escríbeme por GitHub ([DiegoFranciscoG](https://github.com/DiegoFranciscoG)) o usa *Security › Report a vulnerability* del repositorio (reporte privado). Incluye los pasos para reproducir el problema. Respondo en un máximo de 7 días.

## Versiones soportadas

Solo la rama `main` recibe correcciones.

## Prácticas aplicadas

Las referencias entre paréntesis son de OWASP Top 10:2025 (A), OWASP API Security Top 10:2023 (API) y OWASP Top 10 for LLM Applications 2025 (LLM). Las fuentes están en [`docs/investigacion.md`](docs/investigacion.md).

### Secretos y configuración (A02)
- Secretos solo por variables de entorno. `.env` no se versiona; [`.env.example`](.env.example) trae las claves vacías.
- Sin valores por defecto para secretos:
  - La API no arranca sin `ConnectionStrings__Helpdesk` ni sin una `Jwt__SigningKey` de al menos 256 bits.
  - `compose.yaml` exige los obligatorios con `${VAR:?}`.
  - El front no arranca sin una `HELPDESK_API_URL` válida (se valida con una expresión regular antes de usarla en la CSP).
- El navegador no recibe ninguna clave: la API key del clasificador (P27) vive solo en el backend.
- gitleaks escanea todo el historial en CI en cada push y pull request; también corre como hook pre-commit local.

### Autenticación (A07 · API2)
- ASP.NET Core Identity con PBKDF2-HMAC-SHA512 y 220 000 iteraciones (OWASP Password Storage Cheat Sheet).
- Política NIST SP 800-63B-4 para las contraseñas:
  - 15 a 128 caracteres, sin reglas de composición.
  - Se rechazan las comunes, las repetitivas o secuenciales y las que contienen el correo o el nombre.
- Bloqueo de 15 minutos tras 5 intentos fallidos.
- Errores genéricos: el login no revela si el correo existe y compara contra un hash ficticio para igualar el tiempo de respuesta.
- JWT HS256 de 15 minutos, con emisor y audiencia validados.
- Refresh token opaco, guardado como SHA-256 (RFC 9700):
  - Se rota en cada uso y la familia expira a las 8 horas.
  - Reutilizar un token ya rotado revoca toda la familia.
  - Hay 30 segundos de gracia para la recarga de una pestaña con la rotación en vuelo. Solo aplica si la familia sigue viva: tras un logout o una revocación no se acepta.
- En el navegador (RFC 10017):
  - El access token vive solo en memoria.
  - El refresh token se guarda en `sessionStorage` y desaparece al cerrar la pestaña.
- El cliente de integración (`client_credentials`) recibe un JWT con rol `Integration` y alcances `tickets:read` y `suggestions:write`. Su secreto se guarda con hash.

### Autorización (A01 · API1, API3, API5)
- Deny-by-default: la política de respaldo exige un usuario autenticado. Solo son públicos la salud, la documentación OpenAPI y los endpoints de autenticación (login, refresh, logout y token de integración).
- Roles `Admin`, `Supervisor`, `Agent` y `Customer`, más políticas por recurso:
  - Un cliente solo ve sus tickets.
  - Un agente solo ve los tickets de sus colas.
  - Sin acceso, la API responde **404** para no confirmar que el recurso existe (BOLA).
- Los comentarios y adjuntos internos nunca llegan al cliente, tampoco por SignalR: los mensajes en tiempo real solo llevan identificadores.
- Concurrencia optimista con `If-Match`/`ETag` (412 si el ticket cambió).

### Entradas, salidas y errores (A05, A10 · API4, API8)
- FluentValidation en todos los DTO. Las mismas reglas se aplican en el front porque comparte el proyecto de contratos.
- Los enums solo aceptan sus nombres en `snake_case`.
- SQL solo parametrizado (EF Core), con escape de comodines en las búsquedas `LIKE`.
- Errores como `ProblemDetails` con `code` y `traceId`, sin stack traces. Las excepciones se manejan en un solo lugar.
- Exportación CSV protegida contra inyección de fórmulas.
- Rate limiting (429 con `Retry-After`):
  - Login: 5/min por IP. El token de integración tiene una partición aparte.
  - Refresh: 30/min.
  - Subidas: 10/min.
  - Global: 300/min.
- Kestrel limita el cuerpo de las peticiones a 1 MB, salvo en la subida de adjuntos.
- Cabeceras de la API: CSP `default-src 'none'`, `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, COOP/CORP, HSTS y `Cache-Control: no-store`.
- Cabeceras del front (nginx): CSP con `script-src 'self' 'wasm-unsafe-eval'`, `connect-src` limitado a la API y `frame-ancestors 'none'`.
- CORS solo con orígenes explícitos desde el entorno; un comodín `*` se rechaza al arrancar.
- La documentación interactiva (Scalar) se publica sin su asistente de IA, su servidor MCP ni la barra de desarrollo: no envía la definición de la API a servicios de terceros.
- Redirección tras el login solo a rutas internas relativas: se bloquean esquemas, `//host` y `\`.

### Adjuntos (OWASP File Upload Cheat Sheet)
- Lista blanca de extensiones, con firma del archivo (magic bytes) verificada. No se confía en `Content-Type`.
- Máximo 5 MB.
- Se guardan fuera de la base de datos y del webroot, con un nombre aleatorio generado por el servidor.
- La descarga exige autorización sobre el ticket y responde con `Content-Disposition: attachment`, `nosniff` y CSP `sandbox`.

### Registro y monitoreo (A09)
- Serilog en JSON compacto. Los logs de peticiones no incluyen el query string, así que el token de SignalR no queda registrado.
- Nunca se registran contraseñas, tokens ni textos de tickets. Los intentos fallidos se registran con su motivo, sin la contraseña.

### Integración con el clasificador de IA (API10 · LLM02, LLM05, LLM06, LLM09)
- Cliente HTTP tipado con tiempo límite y circuit breaker. Los reintentos solo se hacen en métodos seguros.
- La respuesta se trata como no confiable:
  - Tamaño máximo de 256 KB.
  - Los códigos deben cumplir `^[a-z0-9_]{1,40}$` y los puntajes estar entre 0 y 1.
- La sugerencia **nunca se aplica sola**: un agente la acepta o la corrige (LOPDP art. 20). Si está configurada la clave de revisor, la corrección vuelve a P27 como retroalimentación.
- P27 actúa como encargado del tratamiento (LOPDP art. 34) y anonimiza cédulas, RUC, teléfonos y tarjetas antes de persistir el texto.

### Datos personales (LOPDP de Ecuador)
- Minimización: solo nombre y contacto; nunca cédula ni RUC. Los reportes identifican al cliente por un código.
- Antes de registrar solicitudes, el cliente debe aceptar un aviso de privacidad versionado.
- Derecho de eliminación (art. 15): la anonimización de un cliente borra sus adjuntos y seudonimiza sus usuarios.
- Datos demo 100 % ficticios en dominios reservados `.example`.
- Ante una vulneración de seguridad:
  - Se notifica a la Autoridad de Protección de Datos en un máximo de 5 días (art. 43).
  - Se notifica a los titulares en 3 días si hay riesgo para sus derechos (art. 46).

### Cadena de suministro y contenedores (A03, A08)
- Versiones centralizadas en `Directory.Packages.props`.
- Dependabot para NuGet, GitHub Actions, Dockerfiles y Compose.
- Acciones de GitHub fijadas por SHA, con `permissions: contents: read`.
- gitleaks y hadolint se descargan con el SHA-256 verificado.
- Imágenes con versión exacta:
  - La API corre sobre ASP.NET Core *chiseled* (sin shell ni gestor de paquetes), con el usuario 1654.
  - El front corre sobre `nginx-unprivileged`, con el usuario 101.
  - Ambas tienen healthcheck.
- En Compose, la API corre con sistema de archivos de solo lectura, `cap_drop: ALL` y `no-new-privileges`.
- Trivy escanea las dos imágenes en CI y falla ante vulnerabilidades HIGH/CRITICAL corregibles.
