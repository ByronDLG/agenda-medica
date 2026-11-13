# Agenda Medica Online — especificacion del producto

- **Estado:** en implementacion incremental; ver el alcance actual mas abajo
- **Revision de opciones gratuitas:** 11 de noviembre de 2026
- **Idioma inicial:** espanol
- **Pais inicial:** Guatemala
- **Zona horaria predeterminada:** `America/Guatemala`
- **Nombre de trabajo:** Agenda Medica Online

## 1. Resumen ejecutivo

Construir una aplicacion web responsive para que profesionales de salud y clinicas pequenas publiquen disponibilidad, administren citas y coordinen la atencion con pacientes, con o sin secretaria. El producto debe ser facil de usar desde telefono y escritorio, prevenir reservas duplicadas y permitir incorporar modulos de forma gradual.

Esta especificacion es para un **producto nuevo e independiente**. La solucion heredada SISCAR es financiera, de Visual Studio, usa .NET Framework 4.8 y Oracle/Dapper, y no debe mezclarse con este producto. Agenda Medica Online vive en `agenda-medica/` como proyecto desacoplado: no agregar proyectos, referencias, tablas ni configuracion a `srvSiscar.sln` o a los ejecutables existentes, y no reutilizar datos ni credenciales de SISCAR.

### Estado de implementacion

La aplicacion independiente ya contiene Angular + Tailwind y una API ASP.NET
Core/EF Core con SQL Server. Estan implementados autenticacion con cookies y
CSRF, organizaciones, invitaciones de personal, horarios semanales, cierres,
preferencias de duracion, citas internas, reservas por enlace directo privado y
gestion de citas por Secretaria para agendas asignadas explicitamente. El
administrador puede conceder y revocar asignaciones, que quedan auditadas. La
Secretaria puede consultar, crear, confirmar, rechazar y cancelar citas; no
puede marcar atenciones ni inasistencias. El directorio publico, notificaciones,
MFA y el perfil reutilizable de paciente siguen pendientes. LocalDB y los datos
de pruebas son solo para desarrollo; no usar con pacientes reales.

## 2. Objetivo y propuesta de valor

- Reducir el tiempo dedicado a coordinar citas.
- Dar al medico control de su agenda, acepte o no solicitudes manualmente.
- Permitir que una secretaria coordine una o varias agendas con permisos limitados.
- Ofrecer a pacientes una forma clara de solicitar, cancelar o reprogramar citas.
- Automatizar confirmaciones y recordatorios sin revelar informacion clinica.
- Evolucionar a clinicas con multiples profesionales y, solo tras revisar privacidad, a expediente clinico.

### Indicadores de exito a validar

- No se producen dos citas activas que se traslapen en la misma agenda.
- Un paciente puede completar una solicitud desde telefono sin ayuda.
- El personal puede crear, confirmar, reprogramar y cancelar citas con pocos pasos.
- El medico ve una agenda del dia con estados y datos de contacto estrictamente necesarios.
- Las notificaciones reflejan el estado real de la cita y registran errores de entrega.

## 3. Usuarios, organizaciones y permisos

Cada cuenta profesional pertenece a una organizacion/consultorio. La aplicacion debe soportar un profesional independiente desde el inicio y permitir varios profesionales por organizacion sin rehacer el modelo de datos.

| Rol | Capacidades del MVP |
|---|---|
| Administrador de clinica | Configura la organizacion, invita personal, administra profesionales, permisos y parametros comunes. |
| Medico | Administra su disponibilidad y agenda; confirma/rechaza solicitudes si usa aprobacion; actualiza el estado de atencion. |
| Secretaria | Gestiona citas y datos de contacto para las agendas autorizadas; no accede a notas clinicas. |
| Paciente | Solicita, confirma, cancela o reprograma sus citas mediante un flujo web seguro. No necesita ver otros pacientes. |

Aplicar **minimo privilegio**: el alcance de cada solicitud debe estar limitado por organizacion, usuario y agenda autorizada. Un usuario no debe poder acceder a datos de otra organizacion cambiando identificadores en URL o API.

## 4. Alcance por fases

### MVP — agenda y coordinacion

1. Inicio de sesion para medico, secretaria y administrador; invitacion controlada de personal.
2. Perfil profesional, ubicacion/consultorio, duracion predeterminada y zona horaria configurable.
3. Horarios semanales, excepciones, feriados, pausas, bloqueos y vacaciones.
4. Vista de calendario por dia/semana y lista de citas; filtros por profesional y estado.
5. Alta de pacientes con solo los datos de contacto necesarios para coordinar la cita.
6. Solicitud de cita publica; modo configurable de confirmacion automatica o aprobacion del profesional/secretaria.
7. Cancelacion y reprogramacion conforme a las politicas configuradas.
8. Prevencion de doble reserva y conflictos ante solicitudes simultaneas.
9. Recordatorios configurables (por ejemplo, 48 y 24 horas antes), con opcion de correo primero y estado de entrega.
10. Auditoria de cambios de agenda y permisos.
11. Interfaz en espanol, accesible, responsive y con estados vacios, validacion y errores comprensibles.

### Fase posterior

- Portal autenticado de paciente e historial personal.
- Sincronizacion de Google Calendar, como integracion opcional; la base de datos de la agenda sigue siendo la fuente de verdad.
- Integracion con WhatsApp Business Platform, sujeto a consentimiento, plantillas y costo por mensaje/mercado.
- Reportes de citas, ausencias, cancelaciones y utilizacion.
- Pagos/facturacion, teleconsulta y multiples sucursales.
- Expediente clinico, documentos y notas de consulta, solo luego de definir requisitos legales, retencion, control de acceso, auditoria, cifrado, alojamiento y respuesta a incidentes.

### Fuera del MVP

- Diagnostico, recomendaciones o decisiones clinicas automaticas.
- Chat medico-paciente, recetas, teleconsulta, cobro o facturacion.
- Importacion de datos del sistema SISCAR.
- Almacenamiento de historia clinica o documentos medicos en servicios gratuitos.
- Mensajes automaticos que contengan diagnosticos, procedimientos u otros datos sensibles.

## 5. Flujos principales

### Paciente y medico sin secretaria

1. El paciente selecciona profesional, tipo de cita y horario disponible.
2. El sistema valida disponibilidad y politica de confirmacion.
3. La solicitud queda confirmada o pendiente de aprobacion, segun la configuracion.
4. Se informa el resultado al paciente y al profesional.
5. Se envian recordatorios consentidos; el paciente confirma, cancela o solicita reprogramacion.
6. El profesional marca la cita atendida o como inasistencia.

### Paciente y medico con secretaria

1. La solicitud publica entra a la agenda del profesional como pendiente o confirmada.
2. El administrador asigna a la secretaria solo las agendas que puede gestionar.
3. La secretaria confirma o rechaza solicitudes de esas agendas.
4. Paciente y profesional reciben el cambio correspondiente.
5. La secretaria puede registrar una cita solicitada por telefono y cancelar citas;
   no puede marcar atenciones ni inasistencias.
6. El profesional conserva el control de su disponibilidad; la secretaria no ve
   contenido clinico.

### Regla de disponibilidad

Una cita ocupa un intervalo semiabierto `[inicio, fin)`, con duracion y posibles tiempos de preparacion configurables. La disponibilidad debe calcularse en el servidor a partir de horario, excepciones y citas activas. La verificacion final y la reserva deben ser atomicas (transaccion/constraint o mecanismo equivalente): una comprobacion solo en el navegador no evita carreras ni doble reserva. Persistir instantes en UTC y presentar en la zona horaria de la agenda.

## 6. Reglas de negocio

- Estados sugeridos: `requested`, `confirmed`, `rejected`, `cancelled`, `completed`, `no_show`.
- Cada cambio de estado debe validar transiciones permitidas y guardar actor, fecha y motivo cuando corresponda.
- Cada organizacion puede usar confirmacion manual o automatica; el valor inicial es manual. La politica es configurable por consultorio, no una variable de entorno global.
- No borrar fisicamente una cita confirmada para corregir historial; cancelar y auditar.
- La politica de cancelacion/reprogramacion es configurable por organizacion y debe comunicarse antes de confirmar.
- Al cancelar o reprogramar, liberar el horario anterior y no enviar mensajes duplicados.
- Reintentos de recordatorio deben ser idempotentes. Una tarea tardia no debe notificar citas canceladas.
- Evitar revelar si una persona es paciente de una clinica en respuestas publicas.
- Datos minimos del paciente para coordinar: nombre, medio de contacto y preferencias/consentimiento de notificacion; solicitar identificadores o datos clinicos solo si existe necesidad documentada.

## 7. Arquitectura propuesta

### Stack inicial

- **Frontend:** Angular (version estable con soporte al iniciar el desarrollo), TypeScript, componentes standalone y Angular CLI.
- **Estilos:** Tailwind CSS (version estable; seguir la guia oficial vigente para Angular), tokens de diseno propios y componentes accesibles.
- **Backend:** ASP.NET Core Web API sobre .NET 10 LTS, API REST versionada y contrato OpenAPI.
- **Persistencia:** Microsoft SQL Server con Entity Framework Core y migraciones versionadas. No exponer credenciales de base de datos al navegador.
- **Autenticacion:** ASP.NET Core Identity con cookies HttpOnly y antifalsificacion para la API; primer administrador creado por comando local de un solo uso y cuentas posteriores por invitacion, sin registro publico. Preparar MFA antes de cualquier piloto con datos reales.
- **Tareas asincronas:** cola/cron idempotente para recordatorios, separado de las peticiones web.
- **Pruebas:** unitarias, integracion con SQL Server y E2E para flujos criticos.

### Limites de componentes

- Angular consume exclusivamente la API publica; no contiene reglas de autorizacion como unica barrera.
- ASP.NET Core valida entrada, identidad, permisos, organizacion y reglas de negocio.
- SQL Server aplica integridad e indices por organizacion; como no tiene una restriccion declarativa general para intervalos no solapados, las reservas deben validar y persistir dentro de transacciones serializables con una estrategia de bloqueo por agenda e indices adecuados, probada bajo concurrencia.
- Correo, WhatsApp y Calendar son adaptadores opcionales. El correo usa SMTP via MailKit con secretos externos a SQL Server; nunca guardar credenciales SMTP en la base de datos. Un fallo externo no revierte una cita ya persistida; se registra y reintenta de manera controlada.
- No registrar contrasenas, tokens, cuerpos clinicos ni datos personales completos en logs.

### Estructura inicial sugerida

```text
agenda-medica/
  apps/
    web/                  # Angular + Tailwind
    api/                  # ASP.NET Core + Entity Framework Core
  packages/
    contracts/            # DTOs/tipos compartidos, si aporta valor
  database/
    migrations/
    seeds/                # solo datos ficticios
  docs/
  .env.example
  README.md
```

Separar frontend y backend en repositorios diferentes tambien es valido si el equipo o despliegue lo requiere; no es necesario para el MVP.

## 8. Modelo de datos inicial (borrador)

Entidades principales:

- `Organization`: nombre, pais, zona horaria y politica de confirmacion (`manual` o `automatic`).
- `User`: identidad, estado y metadatos de acceso; no duplicar contrasenas de un proveedor externo.
- `OrganizationMembership`: relacion usuario-organizacion, rol y preferencias de agenda del profesional (duracion predeterminada de 30 minutos y preparacion de 5 minutos; duraciones permitidas: 15, 20, 30, 45 o 60 minutos; preparacion permitida: 0, 5, 10 o 15 minutos).
- `OrganizationInvitation`: correo, rol, hash del token, vencimiento, emisor y estado de aceptacion/revocacion; nunca almacenar el token de invitacion sin procesar.
- `Practitioner`: perfil, especialidad declarada y datos de contacto profesionales.
- `ScheduleRule` y `ScheduleException`: disponibilidad recurrente y cierres de dia completo por fecha. Las reglas semanales guardan horas locales asociadas a la zona horaria de la organizacion.
- `Appointment`: organizacion, profesional, datos minimos de contacto, consentimiento, intervalo UTC, duracion y preparacion guardadas como instantanea, estado y clave de idempotencia. El MVP actual no crea un perfil de paciente reutilizable.
- `AppointmentAuditEvent`: actor, estado anterior/nuevo y fecha UTC para creacion y transiciones de estado; no guarda motivo clinico.
- `PatientContact`: entidad futura para datos minimos de contacto y preferencias/consentimientos, cuando exista un flujo de paciente reutilizable.
- `Notification`: plantilla, destinatario, canal, cita, estado, reintentos y proveedor; nunca contenido clinico.
- `AuditEvent`: actor, accion, recurso, fecha y metadatos minimos.

Incluir `organization_id` en las entidades de negocio e indices por organizacion/profesional/fecha. En SQL Server, validar la estrategia transaccional y restricciones que eviten intervalos incompatibles con pruebas concurrentes sobre la version soportada.

Las notas clinicas deben modelarse en un modulo separado y con permisos propios; no agregar columnas libres de texto clinico al MVP.

## 9. API sugerida

Las rutas son una guia inicial y deben reflejarse en OpenAPI:

- (Futuro, no implementado) `GET /api/v1/public/practitioners` y
  `GET /api/v1/public/availability?practitionerId=...&from=...&to=...` para
  directorio y descubrimiento público.
- `GET/PUT/DELETE /api/v1/organizations/{organizationId}/public-booking-link`
  (profesional autenticado; estado, generación/rotación y revocación del enlace).
- `GET /api/v1/public/booking/availability?days=14` (requiere token Bearer del
  enlace; solo disponibilidad sin datos de citas/pacientes).
- `POST /api/v1/public/booking/requests` (token Bearer, CSRF, UUID
  `Idempotency-Key`, consentimiento y contacto mínimo).
- `GET /api/v1/auth/csrf`, `POST /api/v1/auth/login`, `GET /api/v1/auth/me`, `POST /api/v1/auth/logout`; no exponer registro publico de cuentas.
- `POST /api/v1/organizations/{organizationId}/invitations` y `DELETE /api/v1/organizations/{organizationId}/invitations/{invitationId}` (solo administrador de esa organizacion).
- `GET/PUT /api/v1/organizations/{organizationId}/settings` (solo administrador de esa organizacion; incluye la politica de confirmacion).
- `GET/PUT /api/v1/organizations/{organizationId}/schedule` (profesional autenticado, solo su horario semanal en esa organizacion; escritura protegida por antifalsificacion).
- `GET /api/v1/organizations/{organizationId}/schedule/preview` (hasta 31 dias; aplica horario semanal, duracion, preparacion y cierres; no incluye citas y no constituye disponibilidad reservable).
- `GET/POST /api/v1/organizations/{organizationId}/schedule/exceptions` y `DELETE /api/v1/organizations/{organizationId}/schedule/exceptions/{localDate}` (cierres de dia completo, solo el profesional de esa organizacion).
- `POST /api/v1/invitations/accept` (token de un solo uso, antifalsificacion y limitacion de intentos).
- `GET /api/v1/organizations/{organizationId}/appointments` (profesional autenticado; citas activas de los ultimos y proximos 31 dias, filtradas por organizacion y profesional).
- `POST /api/v1/organizations/{organizationId}/appointments` (profesional autenticado, antifalsificacion y `Idempotency-Key` UUID; valida horario, cierres y solapamientos en SQL Server).
- `PATCH /api/v1/organizations/{organizationId}/appointments/{appointmentId}/status` (profesional autenticado, antifalsificacion y transiciones permitidas).
- `POST /api/v1/appointments/{id}/reschedule`
- `GET/POST /api/v1/patients` (solo campos minimos de contacto)
- `GET /api/v1/audit-events` (solo rol autorizado)

Usar claves de idempotencia para operaciones publicas de reserva y cambios que puedan repetirse por reintento de red. No usar identificadores secuenciales publicos como mecanismo de autorizacion.

La implementacion actual permite solicitudes públicas únicamente a través de
un enlace secreto que cada profesional genera, rota o revoca; no existe
perfil/directorio público. El token se muestra una sola vez, se conserva como
hash en la base de datos, viaja en el fragmento de la URL y se transmite a la
API en `Authorization: Bearer`. La página elimina el fragmento de la barra de
direcciones al cargarlo. La disponibilidad revela solo nombre del consultorio,
zona horaria, duración e inicios libres, nunca citas ni datos de otras personas.
El envío requiere antifalsificación, clave de idempotencia, consentimiento y
un medio de contacto; no admite motivos de consulta. La API vuelve a validar
el enlace y el intervalo bajo bloqueo transaccional antes de persistir. Las
respuestas públicas no se almacenan en caché y los endpoints aplican límites de
solicitud por IP.

Los inicios de `schedule/preview` siguen siendo candidatos base; no constituyen
disponibilidad reservable. La disponibilidad pública y la interfaz interna
filtran citas activas, y la API vuelve a validar bajo bloqueo transaccional
antes de persistir.

## 10. Experiencia visual y diseno

- Direccion: profesional, tranquila y contemporanea, con jerarquia clara y poco ruido visual.
- Paleta inicial sugerida: azul tinta para navegacion/estructura, teal o verde azulado para acciones positivas, fondos calidos/neutros y color de alerta con contraste suficiente. Validar contraste; no comunicar estado solo con color.
- Tipografia de sistema o familia open source optimizada para legibilidad.
- Componentes: navegacion lateral plegable en escritorio, navegacion movil clara, calendario con alternativa de lista, tarjetas de resumen, formularios cortos y confirmaciones antes de acciones destructivas.
- Estados de cita diferenciados por etiqueta, icono/texto y color accesible.
- Usar HTML semantico, foco visible, teclado completo, etiquetas asociadas, mensajes de error accesibles y objetivos tactiles suficientes.
- Objetivo: WCAG 2.2 AA como referencia de diseno y validacion, sin afirmar conformidad hasta medirla.
- Evitar fotos de pacientes o estetica de hospital generica; usar iconos coherentes y datos ficticios para prototipos.
- Definir tokens reutilizables para color, espaciado, tipografia, radios, elevacion y estados; evitar CSS global ad hoc que compita con Tailwind.

## 11. Privacidad, seguridad y confiabilidad

Esta aplicacion puede tratar datos de salud y datos de identificacion. Antes de cargar datos reales, definir jurisdiccion, base legal/consentimiento, responsable del tratamiento, contratos con proveedores, residencia de datos, retencion, exportacion/eliminacion, gestion de incidentes y requisitos regulatorios con asesoria competente.

Requisitos minimos:

- No usar datos de pacientes reales en desarrollo, demos, pruebas, prompts, telemetria ni cuentas gratuitas.
- TLS en transito, secretos fuera del repositorio y cifrado/controles apropiados en almacenamiento y respaldos.
- Autorizacion en servidor y aislamiento multi-organizacion probado; MFA para personal cuando sea posible.
- Proteccion contra abuso: limites de tasa, validacion, cookies seguras y antifalsificacion CSRF; MFA para personal antes de produccion.
- Auditoria de accesos y cambios de citas; acceso a auditoria limitado y retencion definida.
- Backups probados y restauracion ensayada antes de produccion; no asumir que el nivel gratuito incluye backups adecuados.
- Monitoreo que excluya datos sensibles y alerta de errores de tareas/notificaciones.
- No enviar diagnosticos, motivo de consulta ni detalle de expediente por SMS, correo o WhatsApp. Mensaje neutral: aviso de cita, fecha/hora y enlace seguro, segun consentimiento.
- Integraciones de terceros con alcance minimo; OAuth de Google con consentimiento, revocacion y manejo de tokens en servidor.
- Revisión de privacidad/seguridad y pruebas de aislamiento como criterio de salida, no como mejora posterior.

Los requisitos legales dependen del pais donde opere la clinica y de donde se alojen/procesen los datos; esta especificacion no sustituye una evaluacion legal ni certifica cumplimiento.

## 12. Opciones gratuitas y costos

Los limites y precios cambian. Lo siguiente es una fotografia de paginas oficiales consultadas el **11 de noviembre de 2026**, en USD cuando aplica. Confirmar condiciones en el momento de crear las cuentas. "Gratis" es apto para prototipo con datos ficticios, no una recomendacion de produccion clinica.

| Necesidad | Opcion de $0 / libre | Limites relevantes y costos que pueden aparecer |
|---|---|---|
| Framework y estilos | Angular, TypeScript, Tailwind CSS: software open source. | Sin tarifa de licencia; alojamiento, dominio, equipo y mantenimiento no quedan incluidos. La instalacion de Tailwind se debe seguir segun la guia vigente para Angular. |
| Diseno UI | Figma Starter: sin costo; drafts ilimitados y acceso limitado a productos; pagina indica 150 creditos de IA/dia, hasta 500/mes. Penpot es una alternativa open source. | Figma restringe funciones/colaboracion de planes pagos; revisar limites actuales. El plan gratis no equivale a almacenamiento privado empresarial ni controles regulatorios. |
| Hosting frontend estatico | Cloudflare Pages Free. | Hasta 500 builds/mes, un build concurrente, 20.000 archivos por sitio y 25 MiB por archivo. Las Pages Functions cuentan bajo limites de Workers; Worker Free documenta 100.000 solicitudes/dia y 10 ms de CPU por invocacion. No alojar aqui secretos ni suponer que una funcion gratuita satisface requisitos de datos de salud. |
| Base de datos de desarrollo | SQL Server Express LocalDB o SQL Server Express. | Uso local de desarrollo con datos ficticios; no es una recomendacion de alojamiento ni configuracion de produccion. Revisar limites/licencia vigentes para cualquier distribucion o despliegue. |
| API ASP.NET Core de prototipo | Servicio de alojamiento compatible con .NET. | Precios, soporte de SQL Server, limites y politicas de datos dependen del proveedor y del plan; verificar antes de publicar. No usar niveles gratuitos con datos medicos reales. |
| Correo transaccional | Resend Free. | 3.000 correos/mes, maximo 100/dia, hasta 3 dominios segun plan consultado. No incluir contenido medico; revisar verificacion de dominio y politicas de uso. Plan pagado desde $20/mes segun pagina consultada. |
| WhatsApp | WhatsApp Business Platform / Cloud API de Meta. | No asumir envio empresarial gratuito: Meta cobra mensajes entregados por categoria y mercado; hay excepciones/ventanas gratuitas especificas, pero no cubren todos los recordatorios. A esto pueden sumarse tarifas de un proveedor intermediario. Presupuestar por volumen y pais antes de activarlo. |
| Google Calendar | API oficial con cuota gratuita documentada. | La guia consultada muestra 10.000 solicitudes/minuto por proyecto y 600/minuto por usuario/proyecto, con umbral de facturacion de 1.000.000 solicitudes/dia para proyectos nuevos desde mayo de 2026; pueden aplicar requisitos de proyecto, OAuth y limites operativos. La cuota y los terminos pueden cambiar. |
| Desarrollo local | Node.js, Angular CLI, .NET 10 SDK y SQL Server LocalDB/Express. | Sin cuota de proveedor para ejecutar localmente; requiere equipo, energia, respaldo y administracion. No es alojamiento de produccion. |

### Presupuesto practico

- **Prototipo local con datos ficticios:** potencialmente $0/mes, mas equipo/tiempo.
- **Demo publica de bajo trafico:** puede mantenerse en niveles gratuitos, con cold starts, pausas, cuotas y sin garantia de disponibilidad; usar solo datos inventados.
- **Produccion con pacientes:** presupuestar alojamiento/backend/DB con backups y soporte apropiados, dominio, correo, monitoreo, seguridad/privacidad, soporte y mensajeria. No existe una cifra responsable unica sin pais, usuarios, volumen, retencion y requisitos contractuales.
- **WhatsApp/SMS:** tratar como costo variable; correo y notificaciones dentro del portal son la primera opcion de bajo costo.
- No presentar los precios de esta tabla como cotizacion ni recomendar un plan por cumplir automaticamente una norma sanitaria.

## 13. Plan de implementacion recomendado

1. Confirmar pais de operacion, perfil de clinica, politica de aprobacion, zona horaria, canales de notificacion y usuarios iniciales.
2. Crear el esqueleto separado `agenda-medica/`; fijar versiones soportadas y comandos de desarrollo reproducibles.
3. Construir modelo multi-organizacion, autenticacion/autorizacion, migraciones y tests de aislamiento antes de la UI protegida.
4. Crear design tokens, navegacion responsive y estados principales con datos ficticios.
5. Implementar disponibilidad y reservas atomicas; probar solicitudes concurrentes y cambios de zona horaria.
6. Implementar gestion de citas, agenda y accesos de secretaria.
7. Agregar notificaciones neutrales con consentimiento, idempotencia, registro de entrega y opt-out.
8. Ejecutar pruebas de accesibilidad, seguridad, respaldo/restauracion y flujos E2E.
9. Hacer piloto controlado sin expediente clinico; revisar metricas y costos.
10. Evaluar Calendar, WhatsApp, pagos y expediente en decisiones separadas con analisis de costo/riesgo.

## 14. Criterios de aceptacion del MVP

- El profesional configura horarios recurrentes y excepciones.
- Un paciente puede solicitar cita solo en horarios vigentes.
- Dos solicitudes simultaneas para un mismo espacio no generan citas confirmadas que se solapen.
- Secretaria puede administrar solo las agendas asignadas y nunca ve notas clinicas.
- Secretaria puede crear, confirmar, rechazar y cancelar citas en esas agendas;
  no puede marcarlas atendidas ni como inasistencia. El registro identifica al
  actor y cada concesion/revocacion de acceso queda auditada.
- Medico puede confirmar, rechazar, cancelar, reprogramar y cerrar una cita; el sistema guarda actor y cambios.
- La confirmacion y cancelacion comunican el estado correcto; un reintento no duplica la cita ni el mensaje.
- La zona horaria se muestra de forma coherente alrededor de cambios de fecha y conversion UTC/local.
- Pruebas demuestran que un usuario no puede leer ni modificar datos de otra organizacion.
- El flujo principal funciona con teclado y en viewport movil; errores, carga, vacio y exito tienen estados visibles.
- En las pruebas y despliegues gratuitos solo se usan datos ficticios.

## 15. Referencias oficiales consultadas

- [.NET releases and support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)
- [EF Core SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/)
- [Angular: versionado y releases](https://angular.dev/reference/releases)
- [Tailwind CSS: instalacion con Angular](https://tailwindcss.com/docs/installation/framework-guides/angular)
- [Cloudflare Pages: limites](https://developers.cloudflare.com/pages/platform/limits/)
- [Cloudflare Workers: precios y limites](https://developers.cloudflare.com/workers/platform/pricing/)
- [Supabase: precios y cuotas](https://supabase.com/pricing)
- [Render: despliegue gratis y limitaciones](https://render.com/docs/free)
- [Resend: precios y cuotas](https://resend.com/pricing)
- [Meta: precios de WhatsApp Business Platform](https://developers.facebook.com/documentation/business-messaging/whatsapp/pricing)
- [Google Calendar API: cuotas](https://developers.google.com/calendar/api/guides/quota)
- [Figma: planes](https://www.figma.com/pricing/)
