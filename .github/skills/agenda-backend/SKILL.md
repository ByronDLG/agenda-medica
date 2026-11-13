---
name: agenda-backend
description: Disena e implementa el backend ASP.NET Core y SQL Server de Agenda Medica Online, priorizando aislamiento multi-organizacion, agenda sin doble reserva y proteccion de datos sensibles.
---

# Habilidad de backend — Agenda Medica Online

## Contexto tecnico

La arquitectura objetivo esta documentada en `docs/agenda-medica/especificacion.md`. Backend: ASP.NET Core Web API en .NET 10 LTS, Entity Framework Core, SQL Server, migraciones y contrato OpenAPI. Verifica primero el codigo y las versiones reales antes de aplicar esta propuesta.

## Metodo de trabajo

1. Lee la especificacion y localiza controladores, DTOs, entidades, migraciones, filtros/autorizacion, pruebas y patrones existentes.
2. Define limites de dominio: identidad/organizacion, usuarios, disponibilidad, pacientes de contacto, citas, auditoria y notificaciones.
3. Valida entrada con DTOs; usa ASP.NET Core Identity y valida identidad, organizacion y permiso en cada operacion protegida en servidor.
4. Mantiene `OrganizationId` en consultas de negocio y limita cualquier acceso por el contexto autenticado; no confia en identificadores recibidos del cliente.
5. Implementa reglas de disponibilidad y reserva dentro de transacciones SQL Server. Maneja solicitudes concurrentes y reintentos con idempotencia e integridad en base de datos; prueba expresamente la estrategia contra solapamientos.
6. Persiste instantes en UTC; mantiene la zona horaria de la agenda para generar y presentar slots, incluidas fechas limite y cambios de horario.
7. Registra auditoria minima para cambios sensibles y no incluye datos clinicos ni secretos en logs.
8. Implementa tareas de notificacion reintentables e idempotentes; un fallo de proveedor no debe fingir entrega ni invalidar una cita persistida.
9. Agrega pruebas unitarias, de integracion y de autorizacion/aislamiento; valida concurrencia de reservas y transiciones de estado.

## Reglas obligatorias

- No guardar contenido clinico libre en el MVP. El registro de paciente se limita a contacto necesario y preferencias/consentimiento.
- No enviar diagnosticos, motivos de consulta ni documentos clinicos por correo, SMS o WhatsApp.
- No crear criptografia, autenticacion, tokens, permisos ni validacion de estado a mano si existe una biblioteca/patron mantenido para ASP.NET Core.
- No habilitar registro publico de personal; usar invitaciones autorizadas. Protege autenticacion por cookie con antifalsificacion CSRF y no presentes la base local como apta para datos reales.
- No escribir secretos, cadenas de conexion, tokens OAuth ni PII en codigo, logs, fixtures o documentacion.
- Nunca confiar en que Angular oculta datos correctamente; el backend debe aplicar autorizacion en cada lectura y escritura.
- No utilizar servicios gratuitos con pacientes reales ni prometer disponibilidad, backups o cumplimiento legal no verificados.
- No integrar Oracle ni modificar la solucion SISCAR salvo solicitud expresa y evaluacion de impacto aprobada.

## Validacion de entrega

- Migraciones aplican y revierten en el entorno de prueba soportado.
- Migraciones EF Core aplican y revierten en SQL Server de prueba.
- Pruebas negativas confirman que usuarios de otra organizacion no leen ni modifican citas.
- Reservas simultaneas no crean solapamientos confirmados; una repeticion idempotente no duplica citas.
- Errores de proveedor y validacion son visibles, accionables y no se convierten en respuesta de exito.
- OpenAPI y documentacion describen campos, estados, permisos y errores actuales.
