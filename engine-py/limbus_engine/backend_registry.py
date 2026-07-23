"""Resuelve qué backend usar, validando contra el manifiesto de modelos verificado.

En la build pública, SOLO se registran aquí los backends cuyos modelos tengan
IsPublicBuildEligible == true en legal/model-manifest.json (ver docs/01-modelos-licencias.md).
Este archivo es intencionalmente un esqueleto: la carga real del manifiesto y de los
pesos requiere ejecutarse en una máquina con el runtime Python embebido instalado.
"""
from limbus_engine.backends.base import SeparationBackend, SeparationRequest, UnauthorizedModelError


def resolve_backend(request: SeparationRequest) -> SeparationBackend:
    # TODO (build real): cargar legal/model-manifest.json, filtrar por
    # IsPublicBuildEligible, y seleccionar el backend que cubra request.requested_stems
    # con la mejor calidad disponible y autorizada.
    raise UnauthorizedModelError(
        "Ningún backend registrado todavía: pendiente de instalar el manifiesto real "
        "de modelos verificados en esta build."
    )
