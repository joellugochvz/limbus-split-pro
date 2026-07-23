import json
import sys
import traceback

from limbus_engine.backends.base import SeparationRequest, UnauthorizedModelError
from limbus_engine.backend_registry import resolve_backend


def emit(event: dict) -> None:
    """Único punto de escritura a stdout: SIEMPRE una línea JSON, nada más."""
    sys.stdout.write(json.dumps(event, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def main() -> int:
    raw = sys.stdin.readline()
    if not raw.strip():
        emit({"event": "error", "errorCode": "EMPTY_REQUEST", "message": "Solicitud vacía."})
        return 1

    try:
        payload = json.loads(raw)
        request = SeparationRequest.from_dict(payload)
    except (json.JSONDecodeError, KeyError) as exc:
        emit({"event": "error", "errorCode": "MALFORMED_REQUEST", "message": str(exc)})
        return 1

    try:
        backend = resolve_backend(request)
    except UnauthorizedModelError as exc:
        # Fail-closed: nunca se genera una pista silenciosa como sustituto (sección 5/7).
        emit({"event": "error", "errorCode": "MODEL_NOT_AUTHORIZED", "message": str(exc)})
        return 1

    try:
        for stage_event in backend.run(request):
            emit(stage_event)
    except Exception as exc:  # noqa: BLE001 - frontera de proceso: se traduce a evento estructurado
        print(traceback.format_exc(), file=sys.stderr)  # detalle técnico -> stderr, nunca al usuario
        emit({"event": "error", "errorCode": "ENGINE_FAILURE", "message": str(exc)})
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
