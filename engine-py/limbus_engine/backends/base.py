from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Iterator


class UnauthorizedModelError(Exception):
    """Se lanza cuando el manifiesto marca un modelo como no apto para la build actual."""


@dataclass
class SeparationRequest:
    input_file_path: str
    output_folder_path: str
    requested_stems: list[str]
    device: str  # "auto" | "cpu" | "gpu"

    @staticmethod
    def from_dict(d: dict) -> "SeparationRequest":
        return SeparationRequest(
            input_file_path=d["inputFilePath"],
            output_folder_path=d["outputFolderPath"],
            requested_stems=d["requestedStems"],
            device=d.get("device", "auto"),
        )


class SeparationBackend(ABC):
    """Interfaz común para todo motor de separación (Spleeter, Open-Unmix, Demucs...).

    Espejo Python de ISeparationBackend en C# (docs/02-arquitectura-decision.md, sección 4):
    activar un nuevo backend es una entrada en el manifiesto, no un cambio de arquitectura.
    """

    id: str
    capabilities: list[str]

    @abstractmethod
    def run(self, request: SeparationRequest) -> Iterator[dict]:
        """Debe emitir eventos {'event': 'progress'|'stage'|'result', ...} según avanza."""
        raise NotImplementedError
