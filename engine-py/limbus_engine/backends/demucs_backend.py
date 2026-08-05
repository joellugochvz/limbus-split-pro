from __future__ import annotations

import os
from pathlib import Path
from typing import Iterator

from limbus_engine.backends.base import SeparationBackend, SeparationRequest
from limbus_engine.wav_utils import WavAudio, sum_wavs, write_wav

# htdemucs_6s separa 6 fuentes: vocals, drums, bass, guitar, piano, other.
# NOTA DE LICENCIA (ver docs/01-modelos-licencias.md): los pesos de Demucs no
# tienen licencia explícita publicada por Meta/el autor (issue #327 sin resolver
# en facebookresearch/demucs). Este backend SOLO puede activarse en una build de
# desarrollo marcada explícitamente "buildType": "development" en el manifiesto,
# NUNCA en una build pública/distribuible (ver backend_registry.py).
RAW_STEM_BY_CATEGORY = {
    "voces": "vocals",
    "voz_principal": "vocals",
    "bateria": "drums",
    "bajo": "bass",
    "guitarra": "guitar",
    "piano": "piano",
}

RAW_STEM_NAMES = ["vocals", "drums", "bass", "guitar", "piano", "other"]


class DemucsBackend(SeparationBackend):
    """Backend basado en htdemucs_6s (Meta/facebookresearch). SOLO para uso personal,
    no distribuible (licencia de pesos no confirmada). Requiere que el modelo ya esté
    descargado localmente por el propio usuario (no se descarga silenciosamente en
    tiempo de ejecución, sección 7 del encargo)."""

    id = "demucs-htdemucs_6s"
    capabilities = ["voces", "voz_principal", "bateria", "bajo", "guitarra", "piano", "other"]

    def __init__(self, torch_home: str):
        # TORCH_HOME controla dónde busca/cachea PyTorch los pesos descargados
        # (a través de huggingface-hub / torch.hub). Se fija a una carpeta local
        # ya poblada por el propio usuario, nunca se descarga aquí en caliente.
        self._torch_home = torch_home

    def run(self, request: SeparationRequest) -> Iterator[dict]:
        os.environ["TORCH_HOME"] = self._torch_home

        yield {"event": "stage", "stage": "loading_model"}
        from demucs.api import Separator  # import diferido: solo si este backend se usa

        separator = Separator(model="htdemucs_6s", progress=False)

        yield {"event": "stage", "stage": "separating"}
        _, separated = separator.separate_audio_file(request.input_file_path)
        # 'separated' es un dict {nombre_stem: tensor torch [canales, samples]} a la
        # frecuencia de muestreo del modelo (44100 Hz, estéreo).

        sample_rate = separator.samplerate

        def tensor_to_wav(tensor) -> WavAudio:
            import numpy as np
            # tensor: [channels, samples] float32 en rango [-1, 1] aprox.
            arr = tensor.detach().cpu().numpy()
            channels = arr.shape[0]
            interleaved = (np.clip(arr.T, -1.0, 1.0) * 32767.0).astype(np.int16).flatten()
            import array
            samples = array.array("h")
            samples.frombytes(interleaved.tobytes())
            return WavAudio(samples=samples, channels=channels, sample_width=2, frame_rate=sample_rate)

        yield {"event": "stage", "stage": "writing_output"}
        raw_audio = {name: tensor_to_wav(separated[name]) for name in RAW_STEM_NAMES if name in separated}

        output_files: list[str] = []
        selected_raw_names: set[str] = set()

        for category in request.requested_stems:
            raw_name = RAW_STEM_BY_CATEGORY.get(category)
            if raw_name is None or raw_name not in raw_audio:
                continue
            selected_raw_names.add(raw_name)
            out_path = Path(request.output_folder_path) / f"{category}.wav"
            write_wav(out_path, raw_audio[raw_name])
            output_files.append(str(out_path))

        # "Other" = complemento real (sección 6), igual que en SpleeterBackend.
        other_names = [n for n in raw_audio if n not in selected_raw_names]
        if other_names:
            other_audio = raw_audio[other_names[0]] if len(other_names) == 1 else sum_wavs(
                [raw_audio[n] for n in other_names]
            )
            other_path = Path(request.output_folder_path) / "Other.wav"
            write_wav(other_path, other_audio)
            output_files.append(str(other_path))

        yield {"event": "result", "outputFiles": output_files}
