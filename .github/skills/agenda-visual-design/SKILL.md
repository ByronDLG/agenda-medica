---
name: agenda-visual-design
description: Define el sistema visual y la experiencia accesible, moderna y confiable de Agenda Medica Online para medicos, secretarias y pacientes.
---

# Habilidad de diseno visual — Agenda Medica Online

## Objetivo

Crear una experiencia sanitaria profesional, tranquila, clara y utilitaria; reducir carga cognitiva y errores de coordinacion. La referencia de requisitos esta en `docs/agenda-medica/especificacion.md`.

## Direccion de arte

- Interfaz contemporanea y limpia, sin estetica fria de hospital ni decoracion excesiva.
- Jerarquia fuerte: fecha/horario y estado de cita legibles; acciones principales visibles; informacion secundaria discreta.
- Base neutra calida con azul tinta para estructura y teal/verde azulado para acciones positivas. Estados de cita requieren etiqueta y texto/icono, nunca solo color.
- Tipografia legible, espaciado consistente, bordes y sombras sutiles; preferir iconos coherentes y de licencia permisiva.
- En escritorio, navegacion persistente y vistas densas pero escaneables; en movil, acciones primarias alcanzables y agenda en lista cuando el calendario no se adapte.
- Datos y avatares de demostracion siempre ficticios; no usar fotografias de pacientes reales.

## Proceso

1. Identifica usuarios, tareas principales, contexto (recepcion, consulta, movil) y errores de alto impacto.
2. Define arquitectura de informacion y recorridos antes de elegir colores o detalles esteticos.
3. Establece tokens de color, tipografia, escala de espaciado, radios, elevacion, foco y estados; comprueba contraste.
4. Produce wireframes para agenda diaria/semanal, formulario de cita, gestion de horarios y solicitud del paciente.
5. Incluye estados vacios, carga, error, exito, conflicto de horario, confirmacion y cancelacion.
6. Verifica teclado, foco, ampliacion, contraste, etiquetas y orden de lectura; usa WCAG 2.2 AA como objetivo de diseno, no como afirmacion automatica de conformidad.
7. Entrega especificaciones que puedan traducirse a componentes y tokens de Angular/Tailwind.

## Restricciones

- La interfaz no debe mostrar datos o acciones fuera del rol, pero la seguridad siempre se valida en servidor.
- No incluir diagnósticos, notas clinicas o informacion identificable en mockups publicos o herramientas de diseno compartidas.
- No usar color como unica indicacion de estado ni iconos sin etiqueta accesible.
- No sacrificar legibilidad, foco o tamano tactil por densidad visual.
- Evitar copiar marcas, plantillas comerciales o interfaces de terceros; crea un lenguaje visual propio.
- Si se usa Figma Starter u otra herramienta gratuita, revisar limites actuales de colaboracion, privacidad y retencion antes de compartir cualquier material.

## Entrega de diseno

Incluye objetivo por pantalla, jerarquia de informacion, tokens usados, comportamiento responsive, estados, interacciones, accesibilidad y datos ficticios requeridos. Explica cualquier patron que pueda afectar privacidad o inducir una reserva/cancelacion equivocada.
