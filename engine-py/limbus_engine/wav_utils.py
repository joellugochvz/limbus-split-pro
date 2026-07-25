"""
Utilidades minimas de lectura/escritura WAV usando solo la biblioteca estandar
(modulo 'wave'), para no agregar dependencias extra solo para sumar pistas.

Limitacion conocida y honesta: implementado y probado logicamente para PCM de
16 bits (el formato que escribe Spleeter por defecto). WAV float32/24-bit no
esta cubierto todavia; si Spleeter cambia su formato de salida, esto necesita
ampliarse y probarse en una maquina Windows real (no ha sido posible ejecutar
este codigo end-to-end desde este entorno de desarrollo).
"""
from __future__ import annotations

import array
import wave
from pathlib import Path
from typing import NamedTuple


class WavAudio(NamedTuple):
    samples: array.array  # enteros intercalados (interleaved) por canal
    channels: int
    sample_width: int  # bytes por muestra (2 = PCM16)
    frame_rate: int


def read_wav(path: Path) -> WavAudio:
    with wave.open(str(path), "rb") as wf:
        channels = wf.getnchannels()
        sample_width = wf.getsampwidth()
        frame_rate = wf.getframerate()
        n_frames = wf.getnframes()
        raw = wf.readframes(n_frames)

    if sample_width != 2:
        raise NotImplementedError(
            f"wav_utils solo soporta PCM16 por ahora (recibido: {sample_width * 8} bits). "
            "Esto debe ampliarse y probarse en Windows real antes de usarse en produccion."
        )

    samples = array.array("h")  # 'h' = signed short (16 bits)
    samples.frombytes(raw)
    return WavAudio(samples=samples, channels=channels, sample_width=sample_width, frame_rate=frame_rate)


def write_wav(path: Path, audio: WavAudio) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(audio.channels)
        wf.setsampwidth(audio.sample_width)
        wf.setframerate(audio.frame_rate)
        wf.writeframes(audio.samples.tobytes())


def sum_wavs(audios: list[WavAudio]) -> WavAudio:
    """Suma varias pistas WAV compatibles (sección 6: reconstruir 'Other' a partir
    de los stems no seleccionados). Usa numpy (ya es dependencia real de Spleeter)
    para que la suma sea viable en archivos largos; un loop puro de Python por
    muestra sería inutilizablemente lento en pistas de varios minutos.
    Aplica clipping duro a los límites de int16 en vez de desbordar silenciosamente."""
    import numpy as np

    if not audios:
        raise ValueError("sum_wavs requiere al menos una pista")

    first = audios[0]
    for a in audios[1:]:
        if a.channels != first.channels or a.frame_rate != first.frame_rate:
            raise ValueError("Todas las pistas deben tener mismo número de canales y sample rate para sumarse")

    length = min(len(a.samples) for a in audios)
    acc = np.zeros(length, dtype=np.int32)  # int32 para acumular sin desbordar antes del clip
    for a in audios:
        acc += np.frombuffer(a.samples, dtype=np.int16, count=length)

    clipped = np.clip(acc, -32768, 32767).astype(np.int16)
    result = array.array("h")
    result.frombytes(clipped.tobytes())

    return WavAudio(samples=result, channels=first.channels, sample_width=2, frame_rate=first.frame_rate)
