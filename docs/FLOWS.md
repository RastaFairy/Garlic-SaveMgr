# Flujos esenciales

## Backup

```text
PS5 → savedata → IMG + JSON → SHA/registro → Snapshot → backup activo
```

GUARDAR COPIA no crea ni refresca ninguna Papelera.

## Papelera

```text
PS5 ACTIVE ──eliminar de consola──> PS5 TRASH
PC ACTIVE  ──eliminar localmente──> PC TRASH
```

Los dos almacenes son independientes.

## Restore

```text
backup activo → validación → confirmación → OperationRunner → PS5
```

Restore no mueve automáticamente un backup a Trash.

## Carátulas

```text
CACHE → resolución remota → publicación
```

Estados: `QUEUED → RUNNING → COMPLETED | NOT_FOUND | FAILED | STALLED`.

## Salud

```text
HOST / MODULES / SERVICES / OPERATIONS / DATA
                    ↓
                  SALUD
             ┌──────┼──────┐
           HEALTH DIAGNOSTICS HISTORY
```

El histórico no es automáticamente un fallo operativo.
