Analicé el spec. El plan queda así.

## Capa: dominio

### T-01 — Modelar la tarea con su título, estado y fecha límite opcional
- Implementa: RN-01
- Verifica: CA-01, CA-03, CA-05, CA-07
- Depende de: —

### T-02 — Modelar la relación de dependencia entre tareas y rechazar los ciclos
- Implementa: RN-02
- Verifica: CA-04, CA-09
- Depende de: T-01

### T-03 — Impedir la eliminación de una tarea con dependientes
- Implementa: RN-03
- Verifica: CA-10
- Depende de: T-02

### T-04 — Informar cuáles son los prerrequisitos que bloquean a una tarea
- Implementa: RN-01
- Verifica: CA-08
- Depende de: T-02

## Capa: api

### T-05 — Exponer el alta, la consulta y la edición de tareas
- Implementa: —
- Verifica: CA-01, CA-02, CA-03
- Depende de: T-01

### T-06 — Exponer el alta y la baja de dependencias
- Implementa: RN-02
- Verifica: CA-04, CA-09
- Depende de: T-02

### T-07 — Exponer la operación de completar, traduciendo el rechazo del dominio a un error de negocio
- Implementa: RN-01
- Verifica: CA-06, CA-08
- Depende de: T-04

### T-08 — Exponer la eliminación de una tarea
- Implementa: RN-03
- Verifica: CA-10
- Depende de: T-03

## Capa: frontend

### T-09 — Listar las tareas con un control para completarlas
- Implementa: —
- Verifica: CA-11
- Depende de: T-05

### T-10 — Deshabilitar el control de una tarea bloqueada y mostrar el motivo
- Implementa: RN-01
- Verifica: CA-12
- Depende de: T-07

### T-11 — Mostrar el error de regla de negocio sin dejar la lista inconsistente
- Implementa: RN-01
- Verifica: CA-13
- Depende de: T-07

### T-12 — Formulario para crear una tarea y control para declarar una dependencia
- Implementa: —
- Verifica: CA-14, CA-15
- Depende de: T-05, T-06

Todos los criterios del spec quedan cubiertos.
