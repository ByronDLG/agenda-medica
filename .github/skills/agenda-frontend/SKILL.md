---
name: agenda-frontend
description: Construye la interfaz Angular responsive y accesible de Agenda Medica Online con Tailwind, respetando roles, estados reales de API y privacidad.
---

# Habilidad de frontend — Angular y Tailwind

## Contexto tecnico

La especificacion esta en `docs/agenda-medica/especificacion.md`. La propuesta utiliza Angular estable con componentes standalone, TypeScript y Tailwind CSS segun la guia vigente de Angular. Inspecciona primero la version y las convenciones del proyecto antes de crear componentes o instalar paquetes.

## Metodo de trabajo

1. Identifica rutas, layout, servicios HTTP, modelos, validacion, manejo de sesion, tokens visuales y pruebas ya existentes.
2. Implementa componentes pequenos y tipados, separa vistas de llamadas API y mantiene las reglas de autorizacion en backend.
3. Prioriza tareas habituales: revisar agenda, crear cita, cambiar estado, configurar horarios y encontrar pacientes por datos permitidos.
4. Construye estados de carga, error, vacio, exito y conflicto; permite reintentar sin duplicar operaciones.
5. Usa formularios etiquetados, validacion comprensible, foco visible, teclado, contraste y targets tactiles; no dependas solo del color.
6. Disena agenda para telefono y escritorio; ofrece vista de lista alternativa al calendario y comunica zona horaria.
7. Prueba rutas y controles por rol sin tratar el frontend como frontera de seguridad; confirma las restricciones del API.
8. Usa contenido inventado y evita volcar respuestas sensibles a consola, analytics o telemetria.

## Reglas obligatorias

- No incluir secretos, claves privadas, credenciales de base de datos ni tokens de servidor en bundles o variables `environment` publicas.
- No cargar datos clinicos ni historial medico en almacenamiento local, URL, analytics, crash reports o logs.
- No asumir que una solicitud de cita fue exitosa hasta recibir confirmacion de API.
- No exponer campos que el rol actual no necesite; los permisos del servidor siguen siendo obligatorios.
- No introducir una libreria visual grande si HTML accesible y Tailwind resuelven el caso con menor complejidad.
- Mantener espanol claro y consistente; textos de notificacion no revelan detalles de salud.
- No modificar el proyecto heredado ni integrar UI de la agenda en `srvSiscar.sln`.

## Validacion de entrega

- Build y pruebas del proyecto pasan o los fallos preexistentes quedan identificados.
- Flujos de cita contemplan carga, errores de red, conflicto de horario y doble clic/reintento.
- Formularios principales funcionan por teclado y tienen etiquetas/errores accesibles.
- Vista movil no requiere desplazamiento horizontal para completar flujos primarios.
- La UI muestra permisos/estados reales; ninguna accion protegida se simula localmente como si estuviera guardada.
