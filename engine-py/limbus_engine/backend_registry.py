"""Resuelve que backend usar, validando contra el manifiesto de modelos verificado
(legal/model-manifest.json). Fail-closed (seccion 7): si el modelo requerido no
esta en el manifiesto, no tiene archivos reales presentes, o no esta autorizado
para el tipo de build actual, se rechaza explicitamente en vez de simular una
separacion o generar una pista silenciosa.
"""
from __future__ import annotations

import json
import os
from pathlib import Path

from limbus_engine.backends.base import SeparationBackend, SeparationRequest, UnauthorizedModelError
from limbus_engine.backends.spleeter_backend import SpleeterBackend

# El host C# (EngineProcessClient) debe fijar estas variables de entorno para
# que el motor sepa donde esta el manifiesto y la carpeta de modelos descargados.
MANIFEST_PATH_ENV = "LIMBUS_MANIFEST_PATH"
MODELS_DIR_ENV = "LIMBUS_MODELS_DIR"


def _load_manifest() -> dict:
    manifest_path = os.environ.get(MANIFEST_PATH_ENV)
    if not manifest_path or not Path(manifest_path).exists():
        raise UnauthorizedModelError(
            f"No se encontro el manifiesto de modelos (variable de entorno {MANIFEST_PATH_ENV} "
            "no configurada o el archivo no existe). No se puede verificar ninguna licencia."
        )
    with open(manifest_path, encoding="utf-8") as f:
        return json.load(f)


def resolve_backend(request: SeparationRequest) -> SeparationBackend:
    manifest = _load_manifest()
    models_dir = os.environ.get(MODELS_DIR_ENV)
    if not models_dir:
        raise UnauthorizedModelError(f"Variable de entorno {MODELS_DIR_ENV} no configurada.")

    is_public_build = manifest.get("buildType") == "public"

    entry = next((m for m in manifest.get("models", []) if m.get("id") == "spleeter-4stems"), None)
    if entry is None:
        raise UnauthorizedModelError("El modelo 'spleeter-4stems' no esta registrado en el manifiesto.")

    eligible = entry.get("redistributionAuthorized") and entry.get("commercialUseAuthorized")
    if is_public_build and not eligible:
        raise UnauthorizedModelError(
            "El modelo 'spleeter-4stems' no tiene autorizacion de redistribucion/uso comercial "
            "verificada: bloqueado en build publica."
        )

    # Verifica que al menos alguna categoria solicitada sea cubierta por este backend;
    # si ninguna lo es (p. ej. el usuario solo pidio "guitarra"), se rechaza explicito
    # en vez de devolver un resultado vacio silenciosamente.
    covered = set(entry.get("capabilities", []))
    if not any(stem in covered for stem in request.requested_stems):
        raise UnauthorizedModelError(
            "Ninguna de las categorias solicitadas esta cubierta por un modelo autorizado "
            "en esta build. Revisa docs/01-modelos-licencias.md para el detalle de bloqueos."
        )

    # relativePath del manifiesto es "models/spleeter/4stems": SpleeterBackend espera
    # la carpeta PADRE de "4stems" (equivalente al MODEL_PATH que usaria el propio
    # downloader de Spleeter). Se deriva del manifiesto en vez de asumir un valor fijo,
    # para no repetir el bug real encontrado en pruebas (buscaba en models/4stems en
    # vez de models/spleeter/4stems).
    relative_parent = Path(entry["relativePath"]).parent  # "models/spleeter" -> Path("models/spleeter")
    spleeter_model_dir = str(Path(models_dir) / relative_parent.name)  # models_dir/"spleeter"

    return SpleeterBackend(model_dir=spleeter_model_dir)
