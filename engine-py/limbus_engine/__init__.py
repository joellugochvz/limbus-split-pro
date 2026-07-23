"""Motor de separación de Limbus Split Pro.

Se ejecuta como proceso hijo del host C#. Contrato IPC (ver docs/02-arquitectura-decision.md):
- Lee UNA línea JSON de stdin con la solicitud de separación.
- Escribe eventos JSON Lines a stdout: {"event": "progress"|"stage"|"error"|"result"|"cancelled", ...}
- Cualquier log técnico (PyTorch, TensorFlow, warnings) va a stderr, NUNCA a stdout.
"""
