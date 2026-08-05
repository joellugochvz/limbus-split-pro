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

MANIFEST_PATH_ENV = "LIMBUS_MANIFEST_PATH"
MODELS_DIR_ENV = "LIMBUS_MODELS_DIR"
TORCH_HOME_ENV = "LIMBUS_TORCH_HOME"  # solo relevante si demucs esta habilitado


def _load_manifest() -> dict:
    manifest_path = os.environ.get(MANIFEST_PATH_ENV)
    if not manifest_path or not Path(manifest_path).exists():
        raise UnauthorizedModelError(
            f"No se encontro el manifiesto de modelos (variable de entorno {MANIFEST_PATH_ENV} "
            "no configurada o el archivo no existe). No se puede verificar ninguna licencia."
        )
    with open(manifest_path, encoding="utf-8") as f:
        return json.load(f)


def _find_entry(manifest: dict, model_id: str) -> dict | None:
    return next((m for m in manifest.get("models", []) if m.get("id") == model_id), None)


def resolve_backend(request: SeparationRequest) -> SeparationBackend:
    manifest = _load_manifest()
    models_dir = os.environ.get(MODELS_DIR_ENV)
    if not models_dir:
        raise UnauthorizedModelError(f"Variable de entorno {MODELS_DIR_ENV} no configurada.")

    is_public_build = manifest.get("buildType") == "public"

    # ---- Demucs (htdemucs_6s): SOLO en build de desarrollo, NUNCA en build publica. ----
    # No requiere redistributionAuthorized/commercialUseAuthorized porque, por diseno,
    # jamas puede usarse fuera de una build local no distribuible (ver docs/01-modelos-licencias.md).
    demucs_entry = _find_entry(manifest, "demucs-htdemucs_6s")
    if demucs_entry is not None and not is_public_build:
        covered = set(demucs_entry.get("capabilities", []))
        if any(stem in covered for stem in request.requested_stems):
            torch_home = os.environ.get(TORCH_HOME_ENV)
            if torch_home and Path(torch_home).exists():
                from limbus_engine.backends.demucs_backend import DemucsBackend
                return DemucsBackend(torch_home=torch_home)

    # ---- Spleeter 4stems: MIT confirmado, valido en build publica y de desarrollo. ----
    spleeter_entry = _find_entry(manifest, "spleeter-4stems")
    if spleeter_entry is None:
        raise UnauthorizedModelError("El modelo 'spleeter-4stems' no esta registrado en el manifiesto.")

    eligible = spleeter_entry.get("redistributionAuthorized") and spleeter_entry.get("commercialUseAuthorized")
    if is_public_build and not eligible:
        raise UnauthorizedModelError(
            "El modelo 'spleeter-4stems' no tiene autorizacion de redistribucion/uso comercial "
            "verificada: bloqueado en build publica."
        )

    covered = set(spleeter_entry.get("capabilities", []))
    if not any(stem in covered for stem in request.requested_stems):
        raise UnauthorizedModelError(
            "Ninguna de las categorias solicitadas esta cubierta por un modelo autorizado "
            "en esta build. Revisa docs/01-modelos-licencias.md para el detalle de bloqueos."
        )

    relative_parent = Path(spleeter_entry["relativePath"]).parent
    spleeter_model_dir = str(Path(models_dir) / relative_parent.name)

    return SpleeterBackend(model_dir=spleeter_model_dir)
