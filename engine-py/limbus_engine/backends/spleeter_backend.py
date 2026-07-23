from __future__ import annotations

from typing import Iterator

from limbus_engine.backends.base import SeparationBackend, SeparationRequest


class SpleeterBackend(SeparationBackend):
    """Backend basado en Spleeter (Deezer): código y pesos MIT confirmados
    (docs/01-modelos-licencias.md). Candidato por defecto para la build pública."""

    id = "spleeter"
    capabilities = ["voz_principal", "bateria_completa", "bajo", "piano_teclados", "other"]

    def run(self, request: SeparationRequest) -> Iterator[dict]:
        # TODO (build real en Windows con Python embebido + pesos verificados):
        # 1) emit stage "loading_model"
        # 2) cargar audio de entrada (validar sample rate/canales/duración)
        # 3) ejecutar inferencia por segmentos con reporte de progreso real
        # 4) escribir WAV PCM de cada stem solicitado + Other complementario
        # 5) emit "result" con la lista de archivos generados
        yield {"event": "stage", "stage": "not_implemented_in_scaffold"}
        yield {
            "event": "error",
            "errorCode": "SCAFFOLD_ONLY",
            "message": "Backend de andamiaje: la inferencia real se implementa y prueba en Windows.",
        }
