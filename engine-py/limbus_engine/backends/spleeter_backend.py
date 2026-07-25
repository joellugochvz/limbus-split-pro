from __future__ import annotations

import os
import tempfile
from pathlib import Path
from typing import Iterator

from limbus_engine.backends.base import SeparationBackend, SeparationRequest
from limbus_engine.wav_utils import read_wav, sum_wavs, write_wav

# Mapea nuestras categorias de UI a los nombres de stem crudos que produce el
# modelo "spleeter:4stems" (vocals, drums, bass, other). Varias categorias de UI
# pueden apuntar al mismo stem crudo (p. ej. "voces" y "voz_principal" -> vocals),
# ya que 4stems no distingue voz principal de coros.
RAW_STEM_BY_CATEGORY = {
    "voces": "vocals",
    "voz_principal": "vocals",
    "bateria": "drums",
    "bajo": "bass",
}

RAW_STEM_NAMES = ["vocals", "drums", "bass", "other"]


class SpleeterBackend(SeparationBackend):
    """Backend basado en Spleeter 4stems (Deezer): código y pesos MIT confirmados
    y verificados (ver docs/01-modelos-licencias.md y legal/model-manifest.json,
    hash SHA-256 real obtenido en un runner Windows de GitHub Actions)."""

    id = "spleeter"
    capabilities = ["voces", "voz_principal", "bateria", "bajo", "other"]

    def __init__(self, model_dir: str):
        """
        Args:
            model_dir: carpeta que CONTIENE la subcarpeta "4stems" ya descargada
                y verificada (legal/models/spleeter en este repo). Se expone como
                MODEL_PATH para que Spleeter use el modelo local y NUNCA intente
                descargarlo en tiempo de ejecución (sección 7 del encargo: "sin
                descargar modelos silenciosamente mientras se procesa una pista").
        """
        self._model_dir = model_dir

    def run(self, request: SeparationRequest) -> Iterator[dict]:
        expected_model_path = Path(self._model_dir) / "4stems"
        if not expected_model_path.exists():
            # Fail-closed: nunca se simula una separación ni se genera una pista
            # silenciosa como sustituto si el modelo no está realmente presente.
            yield {
                "event": "error",
                "errorCode": "MODEL_FILES_MISSING",
                "message": f"No se encontraron los archivos del modelo en {expected_model_path}.",
            }
            return

        os.environ["MODEL_PATH"] = str(self._model_dir)

        yield {"event": "stage", "stage": "loading_model"}
        from spleeter.separator import Separator  # import diferido: solo si este backend se usa

        separator = Separator("spleeter:4stems", multiprocess=False)

        yield {"event": "stage", "stage": "separating"}
        with tempfile.TemporaryDirectory() as tmp:
            separator.separate_to_file(
                request.input_file_path,
                tmp,
                filename_format="{instrument}.wav",
            )

            base_name = Path(request.input_file_path).stem
            raw_dir = Path(tmp) / base_name

            yield {"event": "stage", "stage": "reading_stems"}
            raw_audio = {name: read_wav(raw_dir / f"{name}.wav") for name in RAW_STEM_NAMES}

            yield {"event": "stage", "stage": "writing_output"}
            output_files: list[str] = []
            selected_raw_names: set[str] = set()

            for category in request.requested_stems:
                raw_name = RAW_STEM_BY_CATEGORY.get(category)
                if raw_name is None:
                    # Categoria no cubierta por este backend (p. ej. piano, guitarra):
                    # se ignora en vez de fingir un resultado; el manifiesto/UI ya
                    # deja esas categorias deshabilitadas cuando no hay modelo real.
                    continue
                selected_raw_names.add(raw_name)
                out_path = Path(request.output_folder_path) / f"{category}.wav"
                write_wav(out_path, raw_audio[raw_name])
                output_files.append(str(out_path))

            # "Other" = complemento real: el residual propio de Spleeter mas
            # cualquier stem principal que el usuario NO haya seleccionado
            # (sección 6: "Other debe contener todo lo que no sea [seleccionado]").
            other_names = [n for n in RAW_STEM_NAMES if n not in selected_raw_names]
            if other_names:
                other_audio = (
                    raw_audio[other_names[0]]
                    if len(other_names) == 1
                    else sum_wavs([raw_audio[n] for n in other_names])
                )
                other_path = Path(request.output_folder_path) / "Other.wav"
                write_wav(other_path, other_audio)
                output_files.append(str(other_path))

        yield {"event": "result", "outputFiles": output_files}
