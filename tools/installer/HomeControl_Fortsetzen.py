#!/usr/bin/env python3
"""Home Control: vorhandene PowerSwitch.css respektieren und Installer fortsetzen."""
import ast
import base64
import io
from pathlib import Path
import subprocess
import sys
import tempfile
from zipfile import ZipFile

quelle = Path('/tmp/HomeControl_Einbau.py')
if not quelle.is_file():
    sys.exit('Installer fehlt unter /tmp/HomeControl_Einbau.py. Zuerst erneut von GitHub holen.')

baum = ast.parse(quelle.read_text(encoding='utf-8'))
wert = None
for eintrag in baum.body:
    if isinstance(eintrag, ast.Assign) and any(
        isinstance(ziel, ast.Name) and ziel.id == 'DATA' for ziel in eintrag.targets
    ):
        wert = ast.literal_eval(eintrag.value)
        break
if not isinstance(wert, str):
    sys.exit('Eingebettetes Installationspaket nicht gefunden. Keine Aenderung.')

alt = """    if path.exists() and path.read_text(encoding='utf-8') != new:
        fail(f'{name} wurde bereits anders angepasst. Ich überschreibe keine fremden Änderungen.')
    changes[path] = new"""
neu = """    if path.exists() and path.read_text(encoding='utf-8') != new:
        if name == 'frontend/src/components/PowerSwitch.css':
            print('Vorhandene PowerSwitch.css bleibt unverändert.', flush=True)
            continue
        fail(f'{name} wurde bereits anders angepasst. Ich überschreibe keine fremden Änderungen.')
    changes[path] = new"""

with tempfile.TemporaryDirectory(prefix='homecontrol-fortsetzen-') as ordner:
    with ZipFile(io.BytesIO(base64.b64decode(wert))) as archiv:
        defekt = archiv.testzip()
        if defekt:
            sys.exit('Installationspaket beschaedigt: ' + defekt)
        archiv.extractall(ordner)
    installer = Path(ordner) / 'install_home_control_upgrade.py'
    code = installer.read_text(encoding='utf-8')
    if code.count(alt) != 1:
        sys.exit('Installerversion abweichend; keine Aenderung am Projekt vorgenommen.')
    installer.write_text(code.replace(alt, neu, 1), encoding='utf-8')
    print('Installer korrigiert. Vorhandene Schalter-CSS wird nicht ueberschrieben.', flush=True)
    raise SystemExit(subprocess.call([sys.executable, str(installer)], cwd=ordner))
