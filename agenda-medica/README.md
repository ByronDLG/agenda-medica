# Agenda Medica Online

Aplicacion nueva e independiente. No forma parte de SISCAR y no debe conectarse
a sus datos, credenciales ni servicios.

## Requisitos

- Node.js 22.12 o posterior y npm 11.21.2 para Angular 21.
- .NET 10 SDK para la API ASP.NET Core.
- SQL Server LocalDB para desarrollo en Windows, o una instancia SQL Server configurada.

## Instalacion

Desde esta carpeta:

```powershell
npm --prefix apps/web install
dotnet tool restore
dotnet restore apps/api/AgendaMedica.Api.csproj
dotnet restore apps/api.Tests/AgendaMedica.Api.Tests.csproj
```

Aplica las migraciones e inicia la API y el frontend en terminales separadas:

```powershell
npm run database:migrate
npm run dev:api
npm run dev:web
```

La interfaz queda en `http://localhost:4200`, la API en `http://localhost:5073`
y el contrato OpenAPI de desarrollo en `http://localhost:5073/openapi/v1.json`.
Angular reenvia `/api` a la API durante el desarrollo.

## Estado actual

El proyecto contiene el esqueleto Angular + Tailwind y ASP.NET Core 10, EF Core
con SQL Server, ASP.NET Core Identity y migraciones de organizaciones y
pertenencias. El modelo persiste la politica por organizacion
(`ManualApproval` o `Automatic`, manual por defecto) que el administrador puede
consultar y cambiar desde el espacio de trabajo. Incluye cookies de sesion,
CSRF, comprobacion de conectividad, una interfaz de inicio de sesion y un
formulario para que administradores inviten personal. Los profesionales pueden
guardar intervalos semanales recurrentes por consultorio. Tambien incluye
aprovisionamiento local del primer administrador y aceptacion de invitaciones
de un solo uso. Los profesionales pueden crear citas internas sobre sus horarios
guardados; el servidor vuelve a validar horarios, cierres y solapamientos dentro
de una transacción SQL serializable con bloqueo por agenda. La interfaz muestra
la lista de solicitudes y confirmaciones, permite cambios de estado y filtra
inicios ya ocupados. También permite generar, rotar y revocar un enlace directo
de reserva por profesional, sin directorio público. La duración inicial es de
30 minutos y la preparación de 5. La vista previa de horario sigue mostrando
candidatos base; la disponibilidad pública y el formulario interno filtran las
citas activas, y la API valida de nuevo al reservar. Las citas guardan nombre,
contacto mínimo, consentimiento, instantes UTC y el estado; no se admite motivo
de consulta ni texto clínico. Se registran eventos de creación y cambio de
estado. Los administradores pueden asignar agendas explícitamente a Secretaría;
su personal asignado puede consultar, crear, confirmar, rechazar y cancelar
citas, pero no marcarlas atendidas ni como inasistencia. Los cambios de
asignación quedan auditados y cada operación de cita registra a su actor real.
No ingreses datos reales: la base LocalDB es únicamente de desarrollo.

Todavía no existen perfiles de paciente reutilizables, reprogramación,
notificaciones, auditoría de accesos/horarios ni MFA. No se debe usar esta
versión con pacientes reales.

La configuracion local usa SQL Server LocalDB y autenticacion integrada de
Windows. Para otros entornos, proporciona el secreto
`ConnectionStrings:AgendaMedica` mediante un proveedor de secretos; no lo
guardes en el repositorio.

## Primer administrador e invitaciones

Tras aplicar las migraciones, ejecuta el aprovisionamiento una sola vez desde
una terminal local:

```powershell
npm run bootstrap:admin
```

La herramienta solicita consultorio, correo y contraseña; la contraseña no se
muestra ni se pasa por argumentos. Se niega a crear otra cuenta si ya existe un
usuario. No es un endpoint web.

Un administrador de consultorio puede invitar personal con rol `Practitioner`
o `Secretary`. El enlace de aceptación vence a las 48 horas, es de un solo uso
y el token sin procesar no se guarda en la base de datos. La API envia el enlace
por el proveedor SMTP configurado al crear la invitacion; si falla, devuelve el
ID de la invitacion pendiente para revocarla antes de volver a intentarlo. La
interfaz permite revocar esa invitacion fallida. Las rutas protegidas son:

- `POST /api/v1/organizations/{organizationId}/invitations`
- `DELETE /api/v1/organizations/{organizationId}/invitations/{invitationId}`
- `POST /api/v1/invitations/accept` (requiere antifalsificación, token válido y contraseña)

La URL de aceptación local se define en
`apps/api/appsettings.Development.json`. En producción debe ser HTTPS y apuntar
a la página Angular publicada.

## Horario semanal

Cada profesional puede consultar y reemplazar su horario recurrente en el
consultorio al que pertenece:

- `GET /api/v1/organizations/{organizationId}/schedule`
- `PUT /api/v1/organizations/{organizationId}/schedule`

Los intervalos usan la zona horaria del consultorio, permiten varios bloques
por día y el servidor rechaza rangos inválidos o solapados. Enviar una lista
vacía elimina el horario semanal. La duración puede configurarse por profesional
(15, 20, 30, 45 o 60 minutos); la preparación permite 0, 5, 10 o 15 minutos.
Los valores iniciales son 30 y 5 minutos. Se pueden marcar cierres de día
completo para los próximos 365 días; la vista previa muestra inicios candidatos
durante 14 días en la zona del consultorio. Esa vista previa no considera citas
existentes; el formulario de reserva filtra las citas activas y la API realiza
la validación definitiva al guardar.

- `GET /api/v1/organizations/{organizationId}/schedule/exceptions`
- `POST /api/v1/organizations/{organizationId}/schedule/exceptions`
- `DELETE /api/v1/organizations/{organizationId}/schedule/exceptions/{localDate}`
- `GET /api/v1/organizations/{organizationId}/schedule/preview`

## Asignación de agendas a Secretaría

Un administrador de consultorio asigna o revoca agendas por persona de
Secretaría. La selección se limita a profesionales de esa misma organización;
una secretaria sin agendas asignadas no puede consultar citas. Cada cambio
conserva actor, agenda y fecha en la auditoría.

- `GET /api/v1/organizations/{organizationId}/secretary-agenda-assignments` (solo administrador)
- `PUT /api/v1/organizations/{organizationId}/secretary-agenda-assignments/secretaries/{secretaryUserId}` (solo administrador, requiere CSRF; reemplaza la lista completa de `practitionerUserIds`)

## Citas internas

Un profesional autenticado puede consultar y gestionar su propia agenda. Una
persona de Secretaría solo puede consultar y coordinar las agendas asignadas
explícitamente en el mismo consultorio. Todas las escrituras requieren
antifalsificación CSRF. La creación recibe fecha y hora locales, nombre, al
menos un medio de contacto y consentimiento; no acepta motivo de consulta. El
encabezado `Idempotency-Key` debe contener un UUID y se reutiliza sin cambios
cuando se reintenta la misma solicitud. La Secretaría puede confirmar,
rechazar o cancelar; solo el profesional puede marcar atendida o inasistencia.
El historial de auditoría identifica a la persona de Secretaría que realizó la
acción.

- `GET /api/v1/organizations/{organizationId}/appointments/practitioners` (profesional: su propia agenda; Secretaría: agendas autorizadas)
- `GET /api/v1/organizations/{organizationId}/appointments` (citas activas de los últimos y próximos 31 días)
- `GET /api/v1/organizations/{organizationId}/appointments?practitionerUserId={userId}` (obligatorio para Secretaría; debe ser una agenda asignada)
- `POST /api/v1/organizations/{organizationId}/appointments` (cita desde horario guardado; Secretaría envía `practitionerUserId`; respuesta 409 si el inicio ya no está disponible)
- `PATCH /api/v1/organizations/{organizationId}/appointments/{appointmentId}/status` (transiciones según rol; respuesta 403 si Secretaría intenta marcar atendida o inasistencia)

## Reserva pública por enlace directo

Cada profesional puede generar un enlace secreto, rotarlo o revocarlo. El
servidor conserva únicamente el hash SHA-256 del token; el token original se
muestra una sola vez. La URL lo incluye en el fragmento (`/book#<token>`), que
no se envía en solicitudes HTTP; la página lo retira de la barra de direcciones
al cargarlo y lo envía a la API únicamente en `Authorization: Bearer`.

- `GET /api/v1/organizations/{organizationId}/public-booking-link` (estado; no devuelve el token)
- `PUT /api/v1/organizations/{organizationId}/public-booking-link` (genera o rota el token; requiere CSRF)
- `DELETE /api/v1/organizations/{organizationId}/public-booking-link` (revoca el enlace; requiere CSRF)
- `GET /api/v1/public/booking/availability?days=14` (requiere Bearer; solo expone nombre del consultorio, zona horaria, duración e inicios disponibles)
- `POST /api/v1/public/booking/requests` (requiere Bearer, CSRF, UUID `Idempotency-Key`, consentimiento y un medio de contacto)

Las respuestas públicas no incluyen citas existentes ni datos de pacientes y
usan `Cache-Control: no-store`. La API limita por IP las lecturas a 60 y las
solicitudes de cita a 5 por minuto. No hay un directorio público de
profesionales. Esta función también es solo para pruebas con datos ficticios.

Las nuevas citas quedan `Requested` con aprobación manual o `Confirmed` con
confirmación automática. Los estados que siguen ocupando el horario son
`Requested` y `Confirmed`; cancelar/rechazar libera el espacio. La migración
`AppointmentBooking` crea las tablas de citas y eventos de auditoría, claves
únicas de idempotencia, restricciones de dominio e índices.
La migración `SecretaryAgendaAssignments` añade las asignaciones explícitas y
la auditoría de sus concesiones y revocaciones.

## Correo saliente

La API usa MailKit (licencia MIT) para SMTP; la biblioteca es gratuita, pero el
proveedor de correo puede cobrar o limitar el envío. El envío está deshabilitado
por defecto y no se almacena ninguna credencial SMTP en SQL Server. En
desarrollo, guarda los valores localmente en .NET User Secrets:

```powershell
dotnet user-secrets set "Email:Smtp:Enabled" "true" --project apps/api/AgendaMedica.Api.csproj
dotnet user-secrets set "Email:Smtp:Host" "smtp.example.net" --project apps/api/AgendaMedica.Api.csproj
dotnet user-secrets set "Email:Smtp:Port" "587" --project apps/api/AgendaMedica.Api.csproj
dotnet user-secrets set "Email:Smtp:FromAddress" "agenda@example.net" --project apps/api/AgendaMedica.Api.csproj
$smtpUser = Read-Host "SMTP user"
dotnet user-secrets set "Email:Smtp:UserName" $smtpUser --project apps/api/AgendaMedica.Api.csproj
Remove-Variable smtpUser
$smtpPassword = Read-Host "SMTP password" -AsSecureString
$plainTextPassword = [System.Net.NetworkCredential]::new("", $smtpPassword).Password
dotnet user-secrets set "Email:Smtp:Password" $plainTextPassword --project apps/api/AgendaMedica.Api.csproj
Remove-Variable plainTextPassword
$smtpPassword.Dispose()
```

Configura el usuario y la contrasena SMTP localmente; no los incluyas en
`appsettings*.json`, SQL Server, el repositorio ni el chat. User Secrets no cifra
los valores y solo es apropiado para desarrollo local. En produccion, inyecta
`Email:Smtp:*` desde el gestor de secretos del alojamiento (por ejemplo, Azure
Key Vault); el servicio falla visiblemente si se intenta enviar con SMTP
deshabilitado o mal configurado. No se requiere ni se guarda aqui ninguna
credencial real.

## Validacion

```powershell
npm run build:web
npm run build:api
npm run test:web
npm run test:api
```
