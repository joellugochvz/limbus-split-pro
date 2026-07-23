# Limbus Split Pro — Investigación de modelos y licencias
Fecha de investigación: 2026-07-23. Fuentes primarias: repositorios oficiales, LICENSE files, model cards, Zenodo.

> Regla aplicada (sección 7): licencia de **código** ≠ licencia de **pesos**. Cada fila indica ambas por separado, con evidencia y decisión de uso.

## Resumen ejecutivo

| Familia | Código | Pesos oficiales | ¿Redistribuible en build comercial? | Calidad relativa |
|---|---|---|---|---|
| Demucs v4 / htdemucs (Meta) | MIT | **No hay licencia explícita para los pesos** — issue #327 abierto sin resolver desde 2022 en el propio repo, y entrenado con MUSDB18-HQ (dataset con restricciones parciales) | **BLOQUEADO** hasta obtener autorización explícita del autor o un modelo re-entrenado con licencia clara | La más alta (SOTA en MUSDB18, único con 6 stems incl. guitarra/piano) |
| Spleeter (Deezer) | MIT | **MIT, explícito y confirmado en el propio repo** ("source code and pre-trained models are... distributed under a MIT license") | **SÍ** | Media (TensorFlow, arquitectura de 2019, más artefactos que Demucs) |
| Open-Unmix `umx` / `umxhq` (Sigsep/Inria) | MIT | MIT según registro Zenodo oficial de los pesos | **SÍ, pendiente de verificar el registro Zenodo del hash exacto antes de fijar la build** | Media-baja |
| Open-Unmix `umxl` (Sigsep, modelo mejorado) | MIT | **CC BY-NC-SA 4.0 — explícitamente no comercial** | **BLOQUEADO** | Alta, pero no usable en producto comercial |
| MDX-Net / UVR (Ultimate Vocal Remover, comunidad) | Variado por checkpoint | **No verificado aún** — la mayoría de checkpoints de la comunidad UVR no publican cadena de procedencia clara ni licencia explícita de pesos | **PENDIENTE** — requiere auditoría checkpoint por checkpoint antes de considerarlo | Alta en algunos checkpoints vocales |
| Separación detallada de batería (bombo/caja/toms/platos) | — | **No existe modelo público con licencia comercial verificada** que yo haya podido confirmar en esta pasada de investigación | **BLOQUEADO — función deshabilitada en la interfaz** | — |
| Guitarra acústica/eléctrica separadas individualmente | — | Solo existe como estimación experimental dentro de `htdemucs_6s` (Meta advierte explícitamente de "mucho bleeding y artefactos" en piano, calidad limitada en guitarra) | **BLOQUEADO** en tanto los pesos de Demucs sigan sin licencia clara | — |

## Detalle por modelo

### 1. Demucs v4 / htdemucs (facebookresearch)
- Código: MIT (`facebookresearch/demucs/LICENSE`).
- Pesos: sin licencia separada publicada. El issue #327 del propio repositorio pregunta exactamente esto y sigue sin respuesta oficial de Meta.
- Dataset de entrenamiento: MUSDB18-HQ, cuyo uso comercial de derivados no está garantizado para todas las pistas incluidas.
- Consecuencia: **no se puede incluir en una compilación pública/comercial** sin autorización explícita adicional. Se documenta como bloqueo legal, no técnico — la arquitectura del producto queda preparada para activarlo en cuanto exista esa autorización o aparezca un modelo re-entrenado equivalente con pesos libres.
- Uso permitido mientras tanto: build de desarrollo marcada `"COMPILACIÓN LOCAL DE DESARROLLO - NO DISTRIBUIR"`, tal como especifica la sección 7.

### 2. Spleeter (Deezer)
- Código y pesos: MIT, confirmado en el LICENSE del repo y reiterado por Deezer Research en su blog oficial.
- Stems disponibles: 2 (voz/acompañamiento), 4 (voz/batería/bajo/otros), 5 (+ piano).
- Limitación real: no separa coros, efectos vocales, ruido, ni instrumentos de batería individuales, ni guitarra. Arquitectura TensorFlow de 2019, más artefactos audibles que Demucs.
- Rol propuesto: motor base **legalmente seguro** para voz principal, batería completa, bajo, piano y Other en la compilación pública, mientras Demucs permanezca bloqueado.

### 3. Open-Unmix
- `umx` y `umxhq`: MIT según el registro Zenodo de los propios pesos (verificar hash y DOI exactos antes de congelar la versión).
- `umxl` (el modelo más nuevo y de mejor calidad): **CC BY-NC-SA 4.0**, explícitamente no comercial. No puede usarse en la build pública.
- Rol propuesto: alternativa de respaldo para vocals/drums/bass/other si Spleeter no alcanza la calidad mínima aceptable en pruebas A/B.

### 4. MDX-Net / checkpoints de la comunidad UVR
- Estado: pendiente. La comunidad de Ultimate Vocal Remover publica decenas de checkpoints (Kim Vocal, UVR-MDX-NET, etc.) con calidad a menudo superior a Spleeter, pero la procedencia y licencia de cada uno varía y muchos no declaran licencia de pesos en absoluto.
- Decisión: no se incluirá ningún checkpoint de esta familia en la build pública sin que yo pueda verificar, por checkpoint, autor, licencia explícita y autorización de redistribución/uso comercial. Cada uno que se investigue se añadirá a esta tabla con su propia fila y evidencia.

### 5. Batería detallada y guitarra/piano individuales
- No he podido confirmar la existencia de un modelo público con pesos de licencia comercial clara que separe bombo, caja, toms y platos como pistas independientes, ni guitarra acústica/eléctrica de forma fiable fuera de `htdemucs_6s` (que hereda el bloqueo de licencia de Demucs).
- Consecuencia directa en la interfaz (sección 5 del encargo): estas opciones deben mostrarse **deshabilitadas** en la build pública, con un texto explicativo ("Modelo sin licencia comercial verificada"), nunca generar un archivo silencioso ni ocultar la limitación.

## Próximos pasos de investigación (antes de fijar el manifiesto de modelos)
1. Verificar directamente con Meta/facebookresearch si existe una ruta de licenciamiento de los pesos de Demucs para uso comercial (contacto o respuesta al issue #327).
2. Auditar 3–5 checkpoints MDX-Net de mayor reputación en la comunidad UVR, uno por uno, con evidencia de licencia.
3. Confirmar el hash y DOI Zenodo exactos de `umx`/`umxhq` antes de registrar el manifiesto.
4. Con base en (1)-(3), decidir el motor por defecto de la build pública: **Spleeter (seguro pero de menor calidad) vs. Demucs (mejor calidad, bloqueado hasta resolver licencia)**.

## Decisión provisional para avanzar con la arquitectura
Mientras se resuelve el punto 4, el motor Python se diseña con una **interfaz de backend intercambiable** (Spleeter y Demucs implementan la misma interfaz `ISeparationBackend`), de modo que activar Demucs en el futuro sea un cambio de configuración y verificación de manifiesto, no un cambio de arquitectura. La build de desarrollo puede usar Demucs marcada como no distribuible; la build pública usa Spleeter/Open-Unmix hasta nueva verificación.
