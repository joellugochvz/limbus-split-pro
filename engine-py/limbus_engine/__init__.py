"""Motor de separación de Limbus Split Pro.

Se ejecuta como proceso hijo del host C#. Contrato IPC (ver docs/02-arquitectura-decision.md):
- Lee UNA línea JSON de stdin con la solicitud de separación.
- Escribe eventos JSON Lines a stdout: {"event": "progress"|"stage"|"error"|"result"|"cancelled"}
- Cualquier log técnico (PyTorch, TensorFlow, warnings) va a stderr, NUNCA a stdout.
"""
import os
import sys

# Desde Python 3.8, Windows YA NO busca DLLs automaticamente en la carpeta de
# python.exe para las dependencias de extensiones nativas (numpy, tensorflow...);
# solo busca en la carpeta del propio .pyd, en System32, o en carpetas registradas
# explicitamente con os.add_dll_directory(). Las DLLs del VC++ Redistributable
# (vcruntime140.dll, msvcp140.dll, etc.) se empaquetan junto a python.exe
# (ver runtime/python-embed/Assemble-PythonEmbed.ps1 y el workflow de CI), asi que
# hay que registrar esa carpeta explicitamente ANTES de que cualquier submodulo
# importe numpy/tensorflow. Error real encontrado en pruebas en Windows: "DLL load
# failed while importing _multiarray_umath" pese a que las DLLs ya estaban presentes
# junto al ejecutable.
if sys.platform == "win32" and hasattr(os, "add_dll_directory"):
    _python_exe_dir = os.path.dirname(sys.executable)
    try:
        os.add_dll_directory(_python_exe_dir)
    except (FileNotFoundError, OSError):
        pass  # se deja continuar; el import de numpy dara un error mas especifico si falla
