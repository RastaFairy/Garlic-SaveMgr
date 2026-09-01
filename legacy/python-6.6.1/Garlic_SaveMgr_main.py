#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Garlic SaveMgr — cliente PC  v6.6
Copia de seguridad y restauracion de saves PS5 (solo PS5).

Sin funcion de refirmado: una copia solo se restaura si su perfil de
origen (uid/id/account_id/aid, segun lo que reporte la API) coincide
con un perfil existente en la consola destino. Si alguna copia no
coincide, se aborta toda la operacion antes de tocar la consola y se
muestra un error de coincidencia de perfil.

v6.1: fix de estabilidad (ver log_v6_0.txt) — la barra de progreso y el
resultado del escaneo se actualizaban desde un hilo secundario tocando
widgets de Qt directamente, lo que corrompia el backing store de la
GUI y terminaba matando el proceso sin traceback durante descargas
grandes. Ahora ambos pasan por una señal Qt conectada a un metodo real
de la ventana, que PySide6 encola de forma segura en el hilo de la
GUI. No hay cambios de funcionalidad ni de la logica de verificacion
de perfil.

v6.2: se quita el modelo de configuracion "Fuente"/"Destino" (resto de
cuando la app clonaba entre dos consolas). Ahora hay una sola consola
configurable en Ajustes; se usa tanto para escanear/copiar como para
restaurar. IMPORTANTE: al ser una clave de configuracion nueva, la IP
guardada con v6.0/v6.1 no se migra sola — hay que volver a escribirla
una vez en Ajustes (la app la pide sola al arrancar si esta vacia).

v6.3: fix de diagnostico en la verificacion de perfil de restauracion.
Sintoma reportado: la comprobacion abortaba TODAS las restauraciones,
incluida una copia restaurada en la MISMA consola de la que se
origino segundos antes (ver 20260827_224333.log, 22:51:06) — eso
descarta que el perfil fuera realmente ilegitimo y apunta a un
problema en como se comparan los campos, no en la decision de
seguridad en si. Cambios:
  1. _perfil_coincide ahora normaliza el VALOR de forma segura (mayus/
     minus, espacios, prefijo "0x", ceros a la izquierda dentro de la
     misma cadena) antes de comparar. Nunca convierte entre bases
     (decimal/hex): con una cadena ambigua se compara tal cual, para
     no arriesgarse a hacer coincidir por error dos perfiles distintos.
  2. Si /account_ids no esta disponible (404 u otro fallo) y se recurre
     a /users, ahora se avisa explicitamente en el log en vez de
     cambiar de fuente en silencio — /users puede no exponer el mismo
     campo identificativo que las copias.
  3. Cada fallo de coincidencia imprime los campos identificativos
     (uid/id/account_id/aid) de la copia Y de los perfiles de destino,
     para ver a simple vista si el problema es de nombre de campo o de
     valor.
  4. Al asignar el uid a reenviar en el import ya no se asume "uid" o
     "id" a secas (_valor_perfil_para_import) — se usa el campo real
     que produjo el match, aunque haya sido account_id o aid.
La logica de "sin datos suficientes para comparar → NO coincide"
(fallo cerrado) no cambia, y sigue sin existir ninguna via para forzar
una restauracion que no coincida — ver directriz al principio de este
docstring.

v6.4: revision de account_ids() durante la verificacion de perfil de
restauracion. Sintoma reportado: sigue fallando incluso restaurando
en la MISMA consola/usuario del que salio la copia. Se encuentra un
bug concreto: account_ids() leia la clave "users" de la respuesta de
/account_ids, la MISMA clave que usa users() para un endpoint
distinto — si /account_ids responde 200 con otra forma de JSON, la
lista salia vacia en silencio y se caia a /users sin que saltara el
aviso de v6.3 (ese aviso solo dispara si perfiles queda vacio, no si
la clave leida es la incorrecta). Ahora se prueban varias claves
plausibles ("account_ids", "accounts", "ids", "perfiles", "users") y
se acepta la respuesta si ya es una lista de nivel superior. Esto NO
toca _perfil_coincide ni el fallo cerrado, solo asegura que
"perfiles" tenga los datos reales del servidor antes de comparar.
Pendiente: confirmar con un intento real si esto basta. Si la
restauracion sigue fallando tras esto, lo mas probable es que /saves
y /account_ids (o /users) usen nombres de campo distintos para el
mismo identificador — hace falta la salida de "Perfiles de destino"
/ "copia: {...}" de un intento real para verlo y decidir el arreglo
sin arriesgarse a hacer coincidir por error dos perfiles distintos.

v6.6.1: fix de eliminacion en consola.
  Bug: API.delete() enviaba HTTP DELETE a /api/delete?idx=N. El payload
  no tiene ningun endpoint DELETE ni ninguna ruta /api/delete; el endpoint
  correcto es GET /api/delete_save?idx=N. Resultado en v6.6: cada intento
  de borrado devuelvia 404 y no eliminaba nada. Fix: se elimina el metodo
  _del() y delete() pasa a usar _get() + la ruta correcta.

v6.6: dos mejoras independientes:
  1. El aviso "/account_ids no disponible" ya no aparece en el panel de
     registro de la interfaz. El fallback a /users sigue funcionando en
     segundo plano y el aviso sigue escribiendose en el fichero de log.
     Era ruido innecesario una vez que el fix de v6.5 hace que el
     fallback funcione correctamente.
  2. Nueva funcionalidad "Eliminar":
     - Pestaña Copia de seguridad: boton "Eliminar de consola" que borra
       los saves seleccionados directamente de la PS5 (todos los slots del
       titulo). Requiere confirmacion explicita con listado de titulos.
       Usa DeleteConsolePipeline (mismo patron que BackupPipeline).
       Los indices se eliminan de mayor a menor para evitar desplazamientos.
     - Pestaña Restaurar: boton "Eliminar copia local" que borra del PC
       los ficheros .img + .json de las copias seleccionadas. Tambien
       requiere confirmacion y recarga la lista al terminar.

v6.5: fix de coincidencia de campo en la verificacion de perfil de
restauracion. Sintoma: restaurar en la MISMA consola de origen fallaba
incluso con v6.4. Causa raiz: /api/saves identifica al propietario con
la clave "uid", pero /api/users devuelve el mismo valor bajo la clave
"id". _perfil_coincide buscaba campos con el MISMO nombre en propietario
y perfil — al no coincidir las claves (uid vs id), salia 0 campos
comunes y la funcion devolviaFalse para todos los perfiles. La fix
introduce _CAMPO_ALIAS (id→uid, aid→account_id) y _normaliza_campos(),
que reescriben ambos dicts a claves canonicas antes de buscar campos
comunes. La logica de comparacion de valores (_normaliza_valor_id) y el
fallo cerrado (sin datos suficientes → NO coincide) no cambian.

USO:  python garlic.py
      Primera ejecucion instala PySide6 y requests automaticamente.
      Python 3.10 o superior requerido.
"""

# ──────────────────────────────────────────────────────────────────────────────
# FASE 0 — Bootstrap (stdlib unicamente)
# ──────────────────────────────────────────────────────────────────────────────
import sys, os, subprocess, platform, importlib.util

_DEPS = [("PySide6", "PySide6"), ("requests", "requests")]

def _bootstrap():
    missing = [pip for mod, pip in _DEPS if importlib.util.find_spec(mod) is None]
    if not missing:
        return
    print("Garlic SaveMgr — instalando dependencias...")
    kw = {}
    if platform.system() == "Windows":
        kw["creationflags"] = 0x08000000  # CREATE_NO_WINDOW
    r = subprocess.run(
        [sys.executable, "-m", "pip", "install", "--quiet", "--upgrade"] + missing,
        **kw)
    if r.returncode != 0:
        print(f"\nError. Ejecuta manualmente:  pip install {' '.join(missing)}")
        input("Pulsa Enter para salir.")
        sys.exit(1)
    os.execv(sys.executable, [sys.executable] + sys.argv)

_bootstrap()

# ──────────────────────────────────────────────────────────────────────────────
import json, logging, threading
from pathlib import Path
from dataclasses import dataclass, field
from typing import List
from datetime import datetime

import requests
from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QSplitter, QLabel, QPushButton, QLineEdit, QTableWidget,
    QTableWidgetItem, QHeaderView, QProgressBar, QTextEdit,
    QDialog, QFormLayout, QDialogButtonBox, QMessageBox,
    QAbstractItemView, QFrame, QSpinBox, QTabWidget
)
from PySide6.QtCore  import Qt, QThread, Signal, QObject, QSettings
from PySide6.QtGui   import QFont, QColor, QPalette

# ──────────────────────────────────────────────────────────────────────────────
# Constantes
# ──────────────────────────────────────────────────────────────────────────────
APP      = "Garlic SaveMgr"
VER      = "6.6.1"
ORG      = "GarlicSave"
PUERTO   = 8082
DIR_BASE = Path.home() / "garlic_saves"
DIR_ENC  = DIR_BASE / "enc"
DIR_LOGS = DIR_BASE / "logs"
for _d in (DIR_ENC, DIR_LOGS): _d.mkdir(parents=True, exist_ok=True)

logging.basicConfig(
    level=logging.DEBUG,
    format="%(asctime)s %(levelname)s %(message)s",
    handlers=[
        logging.FileHandler(
            DIR_LOGS / f"{datetime.now():%Y%m%d_%H%M%S}.log",
            encoding="utf-8"),
        logging.StreamHandler()
    ])
L = logging.getLogger("garlic")

# ──────────────────────────────────────────────────────────────────────────────
# Utilidades
# ──────────────────────────────────────────────────────────────────────────────
def fmt_bytes(n: int) -> str:
    for u in ("B","KB","MB","GB"):
        if n < 1024: return f"{n:.0f} {u}"
        n /= 1024
    return f"{n:.1f} TB"

# Campos que identifican al propietario original de una partida. Se comparan
# tal cual los reporte la API — no se asume cual de ellos va a existir.
CAMPOS_ID = ("uid", "id", "account_id", "aid")

# /api/saves identifica al propietario como 'uid'; /api/users lo devuelve
# como 'id'. Son el mismo valor conceptual, pero con nombre de clave distinto
# segun el endpoint. Se normalizan antes de comparar para no rechazar copias
# legitimas solo por esta diferencia de nombre.
_CAMPO_ALIAS: dict = {"id": "uid", "aid": "account_id"}
_CAMPOS_CANON = ("uid", "account_id")   # claves resultantes tras normalizar

def _normaliza_campos(d: dict) -> dict:
    """Normaliza los nombres de los campos identificativos (id→uid,
    aid→account_id) para poder comparar entradas de /saves con las de
    /users o /account_ids aunque usen claves distintas para el mismo valor."""
    out: dict = {}
    for k, v in d.items():
        if k in CAMPOS_ID:
            out[_CAMPO_ALIAS.get(k, k)] = v
    return out

def _es_ps5(entry: dict) -> bool:
    """True si la entrada NO es de PS4 (este administrador solo maneja PS5)."""
    return str(entry.get("type", "ps5")).lower() != "ps4"

def _extraer_propietario(entry: dict) -> dict:
    """Campos identificativos presentes en una entrada de /saves."""
    return {k: entry[k] for k in CAMPOS_ID if entry.get(k) not in (None, "")}

def _normaliza_valor_id(v) -> str:
    """
    Normaliza variaciones de formato SEGURAS de un valor identificativo:
    mayus/minusculas, espacios, y un posible prefijo "0x"/ceros a la
    izquierda dentro de la MISMA cadena. Nunca convierte entre bases
    (decimal <-> hex): eso exigiria adivinar la base de una cadena
    ambigua, y adivinar mal podria hacer coincidir por error dos
    perfiles distintos — justo lo que esta comprobacion existe para
    evitar. Ante la duda, se compara tal cual (sigue fallando cerrado).
    """
    s = str(v).strip().lower()
    if s.startswith("0x"):
        s = s[2:]
    if s and set(s) <= set("0123456789abcdef"):
        s = s.lstrip("0") or "0"
    return s

def _perfil_coincide(propietario: dict, perfil: dict) -> bool:
    """
    True solo si hay al menos un campo identificativo en comun entre el
    propietario original de la partida y un perfil de la consola destino,
    y todos los campos en comun coinciden.

    Los campos se normalizan antes de comparar (id→uid, aid→account_id) para
    no rechazar copias legitimas cuando /saves y /users usan nombres de clave
    distintos para el mismo identificador (el caso mas frecuente: /saves
    devuelve 'uid' y /users devuelve 'id').

    Si no hay datos suficientes para comparar, se considera que NO coincide
    (falla cerrado).
    """
    p = _normaliza_campos(propietario)
    f = _normaliza_campos(perfil)
    comunes = [c for c in _CAMPOS_CANON
               if p.get(c) not in (None, "") and f.get(c) not in (None, "")]
    if not comunes:
        return False
    return all(_normaliza_valor_id(p[c]) == _normaliza_valor_id(f[c])
               for c in comunes)

def _campos_presentes(d: dict) -> str:
    """Representacion legible de los campos de CAMPOS_ID presentes en d,
    solo para diagnostico en el log — no participa en la comparacion."""
    campos = [f"{c}={d[c]!r}" for c in CAMPOS_ID if d.get(c) not in (None, "")]
    return "{" + ", ".join(campos) + "}" if campos else "(sin uid/id/account_id/aid)"

def _valor_perfil_para_import(perfil: dict) -> str:
    """Identificador a reenviar en import_encrypted/import_finish: el
    primer campo de CAMPOS_ID presente en el perfil que dio match (antes
    se asumia siempre 'uid' o 'id' a secas, perdiendo el valor si el
    match se habia dado por 'account_id' o 'aid')."""
    for c in CAMPOS_ID:
        v = perfil.get(c)
        if v not in (None, ""):
            return str(v)
    return ""

# ──────────────────────────────────────────────────────────────────────────────
# API
# ──────────────────────────────────────────────────────────────────────────────
class GarlicError(Exception): pass

# Las señales Qt de progreso (prog = Signal(int, int)) viajan como "int" de
# C, es decir un entero de 32 bits con signo (máximo 2_147_483_647). Algunos
# contenedores de save de PS5 ocupan exactamente 2 GiB (2_147_483_648 bytes),
# 1 byte por encima de ese límite. Si se emite ese "total" (o un "done" que
# lo alcance) tal cual, Qt lanza OverflowError en cada actualización de
# progreso —cientos o miles de veces mientras dura la descarga/subida—.
# Por eso todo callback de progreso pasa por aquí, que recorta ambos
# valores al rango valido antes de invocar cb().
_I32_MAX = 2_147_483_647

def _cb_seguro(cb, done, total):
    if cb:
        cb(min(done, _I32_MAX), min(total, _I32_MAX))

class API:
    def __init__(self, ip: str, port: int = PUERTO):
        self.base = f"http://{ip}:{port}/api"
        self.ip   = ip
        self.port = port

    def _get(self, path, timeout=15) -> requests.Response:
        try:
            r = requests.get(self.base + path, timeout=timeout)
            r.raise_for_status(); return r
        except requests.RequestException as e:
            raise GarlicError(str(e)) from e

    def _post_raw(self, path, data: bytes, cb=None, timeout=300) -> dict:
        try:
            total = len(data); sent = [0]
            mv = memoryview(data)
            def gen():
                for off in range(0, total, 65536):
                    blk = mv[off:off+65536].tobytes()
                    sent[0] += len(blk)
                    if cb: _cb_seguro(cb, sent[0], total)
                    yield blk
            r = requests.post(self.base + path, data=gen(),
                              headers={"Content-Type":"application/octet-stream",
                                       "Content-Length": str(total)},
                              timeout=timeout)
            r.raise_for_status(); return r.json()
        except requests.RequestException as e:
            raise GarlicError(str(e)) from e

    def _dl(self, path, dest: Path, cb=None, timeout=600) -> int:
        try:
            r = requests.get(self.base + path, stream=True, timeout=timeout)
            r.raise_for_status()
            total = int(r.headers.get("Content-Length", 0))
            done  = 0
            dest.parent.mkdir(parents=True, exist_ok=True)
            with open(dest, "wb") as f:
                for chunk in r.iter_content(65536):
                    f.write(chunk); done += len(chunk)
                    if cb and total: _cb_seguro(cb, done, total)
            return done
        except requests.RequestException as e:
            raise GarlicError(str(e)) from e

    # ── estado ────────────────────────────────────────────────────────────────
    def ping(self) -> bool:
        try: self._get("/status", 5); return True
        except GarlicError: return False

    # ── usuarios ──────────────────────────────────────────────────────────────
    def account_ids(self) -> list:
        try:
            data = self._get("/account_ids").json()
        except GarlicError:
            return []
        if isinstance(data, list):
            return data
        # No se asume una unica clave: /account_ids es un endpoint distinto
        # de /users y no hay certeza de que anide su lista bajo el mismo
        # nombre ("users") que este ultimo. Se prueban varias formas
        # plausibles, en vez de devolver [] en silencio si la real no es
        # la que se esperaba.
        for clave in ("account_ids", "accounts", "ids", "perfiles", "users"):
            v = data.get(clave)
            if isinstance(v, list):
                return v
        return []

    def users(self) -> list:
        return self._get("/users").json().get("users", [])

    # ── inventario ────────────────────────────────────────────────────────────
    def saves(self) -> list:
        return self._get("/saves").json().get("saves", [])

    def scan_titles(self, uid="") -> list:
        """Usa /api/scan_titles si el servidor lo tiene; si no, agrupa /api/saves."""
        try:
            q = f"?uid={uid}" if uid else ""
            return self._get(f"/scan_titles{q}").json().get("titles", [])
        except GarlicError:
            return self._group_saves(uid)

    def _group_saves(self, uid="") -> list:
        saves = self.saves()
        groups: dict = {}
        for s in saves:
            if not _es_ps5(s): continue
            if uid and s.get("uid","").lower() != uid.lower(): continue
            k = f"{s.get('title_id')}|{s.get('uid')}"
            if k not in groups:
                groups[k] = {"title_id": s.get("title_id",""),
                             "uid": s.get("uid",""),
                             "title_name": s.get("title_name",""),
                             "slot_count": 0, "backup_count": 0, "slots": []}
            g = groups[k]
            g["slot_count"] += 1
            if s.get("backup"): g["backup_count"] += 1
            g["slots"].append({"name": s.get("save_name",""),
                                "backup": bool(s.get("backup"))})
        return list(groups.values())

    # ── copia a PC ────────────────────────────────────────────────────────────
    def download_raw(self, dest: Path, idx: int, cb=None) -> int:
        """Descarga la imagen ENC bruta (.img) de un save PS5."""
        return self._dl(f"/download_raw?idx={idx}", dest, cb)

    # ── restaurar desde PC ───────────────────────────────────────────────────
    def import_encrypted(self, img_data: bytes, uid: str, cb=None) -> dict:
        return self._post_raw(f"/import_encrypted?uid={uid}", img_data, cb)

    def import_finish(self, uid: str) -> dict:
        return self._get(f"/import_finish?uid={uid}").json()

    # ── eliminar save de la consola ──────────────────────────────────────────
    def delete(self, idx: int) -> dict:
        """Elimina el save en la posicion idx de /api/saves.
        El payload expone GET /api/delete_save?idx=N — no existe ningun
        endpoint DELETE ni ninguna ruta /api/delete."""
        r = self._get(f"/delete_save?idx={idx}")
        try: return r.json()
        except Exception: return {}

# ──────────────────────────────────────────────────────────────────────────────
# Modelo de datos
# ──────────────────────────────────────────────────────────────────────────────
@dataclass
class ConsolaCfg:
    nombre: str = ""
    ip:     str = ""
    puerto: int = PUERTO

@dataclass
class Cfg:
    consola: ConsolaCfg = field(default_factory=ConsolaCfg)

    @classmethod
    def load(cls) -> "Cfg":
        s = QSettings(ORG, APP); c = cls()
        c.consola.nombre = s.value("c_nom", "PS5")
        c.consola.ip     = s.value("c_ip", "")
        c.consola.puerto = int(s.value("c_prt", PUERTO))
        return c

    def save(self):
        s = QSettings(ORG, APP)
        s.setValue("c_nom", self.consola.nombre)
        s.setValue("c_ip",  self.consola.ip)
        s.setValue("c_prt", self.consola.puerto)

@dataclass
class Titulo:
    title_id:     str
    uid:          str
    title_name:   str
    slot_count:   int
    backup_count: int
    slots:        list
    seleccionado: bool = True

@dataclass
class BackupEntry:
    """Una copia local en DIR_ENC (imagen .img + metadatos .json)."""
    img_path:    Path
    title_id:    str
    save_name:   str
    title_name:  str
    propietario: dict
    origen:      dict
    fecha:       str
    tamano:      int

# ──────────────────────────────────────────────────────────────────────────────
# Backups locales (guardar / listar)
# ──────────────────────────────────────────────────────────────────────────────
def _guardar_sidecar(img_path: Path, meta: dict):
    img_path.with_suffix(".json").write_text(
        json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")

def leer_backups_locales() -> List[BackupEntry]:
    out = []
    for jf in sorted(DIR_ENC.glob("*.json"), reverse=True):
        img = jf.with_suffix(".img")
        if not img.exists(): continue
        try:
            meta = json.loads(jf.read_text(encoding="utf-8"))
        except (OSError, ValueError) as e:
            L.warning(f"Backup ilegible {jf}: {e}"); continue
        out.append(BackupEntry(
            img_path    = img,
            title_id    = meta.get("title_id",""),
            save_name   = meta.get("save_name",""),
            title_name  = meta.get("title_name",""),
            propietario = meta.get("propietario", {}),
            origen      = meta.get("origen", {}),
            fecha       = meta.get("fecha",""),
            tamano      = meta.get("tamano", img.stat().st_size)))
    return out

# ──────────────────────────────────────────────────────────────────────────────
# Pipeline de copia de seguridad (consola → PC)
# ──────────────────────────────────────────────────────────────────────────────
class BackupPipeline(QObject):
    log    = Signal(str, str)        # mensaje, nivel
    prog   = Signal(int, int)        # done, total
    estado = Signal(str, str, str)   # title_id, uid, estado
    done   = Signal()

    def __init__(self, titulos: List[Titulo], consola: ConsolaCfg):
        super().__init__()
        self.titulos = titulos
        self.consola = consola
        self._stop   = False

    def stop(self): self._stop = True

    def run(self):
        src = API(self.consola.ip, self.consola.puerto)
        total = len(self.titulos)
        ok_n = err_n = 0

        for n, tit in enumerate(self.titulos):
            if self._stop:
                self.log.emit("Cancelado.", "warn"); break

            self.log.emit(f"\n{'─'*58}", "sep")
            self.log.emit(
                f"[{n+1}/{total}]  {tit.title_id}"
                f"  {tit.title_name or '—'}  ({tit.slot_count} slots)", "info")
            self.estado.emit(tit.title_id, tit.uid, "proc")
            self.prog.emit(n, total)

            slots_main = [s for s in tit.slots if not s.get("backup")]
            if not slots_main: slots_main = tit.slots

            titulo_ok = True
            for slot in slots_main:
                if self._stop: break
                if not self._slot(tit, slot, src): titulo_ok = False

            if titulo_ok:
                ok_n += 1; self.estado.emit(tit.title_id, tit.uid, "ok")
            else:
                err_n += 1; self.estado.emit(tit.title_id, tit.uid, "err")

        self.prog.emit(total, total)
        self.log.emit(f"\n{'═'*58}", "sep")
        self.log.emit(f"Fin:  {ok_n} OK  /  {err_n} errores  de {total} titulos.", "info")
        self.done.emit()

    def _slot(self, tit: Titulo, slot: dict, src: API) -> bool:
        sname = slot.get("name","")
        ts    = datetime.now().strftime("%Y%m%d_%H%M%S")
        self.log.emit(f"  slot: {sname}", "info")

        self.log.emit("  buscando save en la consola...", "info")
        try:
            saves = src.saves()
            idx = next((i for i,s in enumerate(saves)
                        if s.get("title_id") == tit.title_id
                        and s.get("save_name") == sname
                        and s.get("uid","").lower() == tit.uid.lower()), None)
            if idx is None:
                self.log.emit("  ERR: save no encontrado en la consola", "error")
                return False
            entrada = saves[idx]
        except GarlicError as e:
            self.log.emit(f"  ERR: {e}", "error"); return False

        self.log.emit("  descargando copia de seguridad...", "info")
        raw_path = DIR_ENC / f"{tit.title_id}_{sname}_{ts}.img"
        try:
            sz = src.download_raw(raw_path, idx, lambda d,t: self.prog.emit(d,t))
        except GarlicError as e:
            self.log.emit(f"  ERR: {e}", "error"); return False

        propietario = _extraer_propietario(entrada)
        _guardar_sidecar(raw_path, {
            "title_id":    tit.title_id,
            "save_name":   sname,
            "title_name":  tit.title_name,
            "propietario": propietario,
            "origen":      {"nombre": self.consola.nombre, "ip": self.consola.ip},
            "fecha":       datetime.now().isoformat(timespec="seconds"),
            "tamano":      sz,
        })
        self.log.emit(f"  OK  {fmt_bytes(sz)}  guardado como {raw_path.name}", "ok")
        return True

# ──────────────────────────────────────────────────────────────────────────────
# Pipeline de restauracion (PC → consola)
#
# Verifica el perfil de TODAS las copias antes de tocar la consola destino.
# Si alguna copia no pertenece a un perfil existente en el destino, se
# aborta la operacion completa (no se restaura nada) y se reporta un
# error de coincidencia de perfil. No hay ninguna forma de forzar la
# restauracion saltandose esta verificacion.
# ──────────────────────────────────────────────────────────────────────────────
class RestorePipeline(QObject):
    log    = Signal(str, str)   # mensaje, nivel
    prog   = Signal(int, int)   # done, total
    estado = Signal(int, str)   # indice en la lista, estado
    done   = Signal()

    def __init__(self, backups: List[BackupEntry], consola: ConsolaCfg):
        super().__init__()
        self.backups = backups
        self.consola = consola
        self._stop   = False

    def stop(self): self._stop = True

    def run(self):
        dst = API(self.consola.ip, self.consola.puerto)
        if not dst.ping():
            self.log.emit("Sin conexion con la consola destino.", "error")
            self.done.emit(); return

        try:
            perfiles = dst.account_ids()
            fuente_perfiles = "account_ids"
            if not perfiles:
                perfiles = dst.users()
                fuente_perfiles = "users"
                # Solo al fichero de log, no al panel de la interfaz:
                # el fallback ya funciona correctamente desde v6.5 y el
                # aviso era ruido innecesario para el usuario.
                L.debug("/account_ids no disponible en la consola destino; "
                        "usando /users como alternativa.")
        except GarlicError as e:
            self.log.emit(f"No se pudieron leer los perfiles de destino: {e}", "error")
            self.done.emit(); return
        if not perfiles:
            self.log.emit("La consola destino no reporto ningun perfil.", "error")
            self.done.emit(); return

        # ── verificacion de perfil de TODAS las copias, antes de empezar ──────
        self.log.emit("Verificando perfil de origen de cada copia...", "info")
        asignaciones: dict = {}
        fallos = []
        for n, b in enumerate(self.backups):
            perfil = next((p for p in perfiles if _perfil_coincide(b.propietario, p)), None)
            if perfil is None:
                fallos.append(n)
            else:
                asignaciones[n] = _valor_perfil_para_import(perfil)

        if fallos:
            resumen_perfiles = " | ".join(_campos_presentes(p) for p in perfiles)
            self.log.emit(
                f"  Perfiles de destino (via /{fuente_perfiles}): "
                f"{resumen_perfiles}", "error")
            for n in fallos:
                b = self.backups[n]
                self.log.emit(
                    f"  ERROR de coincidencia de perfil: {b.title_id} "
                    f"({b.save_name}) no pertenece a ningun perfil de la "
                    f"consola destino.  copia: {_campos_presentes(b.propietario)}",
                    "error")
                self.estado.emit(n, "err")
            self.log.emit(
                f"\nAbortado: {len(fallos)} de {len(self.backups)} copias no "
                "coinciden con un perfil de la consola destino. No se ha "
                "restaurado ninguna partida.", "error")
            self.done.emit(); return

        self.log.emit(f"Perfil verificado en las {len(self.backups)} copias "
                       f"(via /{fuente_perfiles}).", "ok")

        # ── restauracion ───────────────────────────────────────────────────────
        total = len(self.backups); ok_n = err_n = 0
        for n, b in enumerate(self.backups):
            if self._stop:
                self.log.emit("Cancelado.", "warn"); break

            self.log.emit(f"\n{'─'*58}", "sep")
            self.log.emit(
                f"[{n+1}/{total}]  {b.title_id}  {b.title_name or '—'}"
                f"  ({b.save_name})", "info")
            self.estado.emit(n, "proc"); self.prog.emit(n, total)

            uid = asignaciones[n]
            try:
                img  = b.img_path.read_bytes()
                res  = dst.import_encrypted(img, uid, lambda d,t: self.prog.emit(d,t))
                fres = dst.import_finish(uid)
                if not fres.get("ok"):
                    self.log.emit(f"  ERR al finalizar: {fres.get('error','?')}", "error")
                    err_n += 1; self.estado.emit(n, "err"); continue
                self.log.emit(
                    f"  OK  coincidencia={'si' if res.get('match', True) else 'NO'}"
                    f"  existia={res.get('exists', False)}", "ok")
                ok_n += 1; self.estado.emit(n, "ok")
            except GarlicError as e:
                self.log.emit(f"  ERR: {e}", "error")
                err_n += 1; self.estado.emit(n, "err")

        self.prog.emit(total, total)
        self.log.emit(f"\n{'═'*58}", "sep")
        self.log.emit(f"Fin restauracion:  {ok_n} OK  /  {err_n} errores  de {total}.", "info")
        self.done.emit()

# ──────────────────────────────────────────────────────────────────────────────
# Pipeline de eliminacion en consola (consola → borrar)
#
# Obtiene la lista de saves actual, localiza todos los slots del titulo
# seleccionado y los elimina de mayor a menor indice para evitar
# desplazamientos tras cada borrado.
# ──────────────────────────────────────────────────────────────────────────────
class DeleteConsolePipeline(QObject):
    log    = Signal(str, str)        # mensaje, nivel
    prog   = Signal(int, int)        # done, total
    estado = Signal(str, str, str)   # title_id, uid, estado
    done   = Signal()

    def __init__(self, titulos: List[Titulo], consola: ConsolaCfg):
        super().__init__()
        self.titulos = titulos
        self.consola = consola
        self._stop   = False

    def stop(self): self._stop = True

    def run(self):
        api   = API(self.consola.ip, self.consola.puerto)
        total = len(self.titulos)
        ok_n  = err_n = 0

        for n, tit in enumerate(self.titulos):
            if self._stop:
                self.log.emit("Cancelado.", "warn"); break

            self.log.emit(f"\n{'─'*58}", "sep")
            self.log.emit(
                f"[{n+1}/{total}]  {tit.title_id}"
                f"  {tit.title_name or '—'}  ({tit.slot_count} slots)", "info")
            self.estado.emit(tit.title_id, tit.uid, "proc")
            self.prog.emit(n, total)

            try:
                saves = api.saves()
                # Indices de TODOS los slots del titulo para el uid dado
                idxs = [i for i, s in enumerate(saves)
                        if s.get("title_id") == tit.title_id
                        and s.get("uid", "").lower() == tit.uid.lower()]
                if not idxs:
                    self.log.emit(
                        f"  ERR: ningun save encontrado en la consola para "
                        f"{tit.title_id} (uid={tit.uid})", "error")
                    err_n += 1; self.estado.emit(tit.title_id, tit.uid, "err"); continue

                # Eliminar de mayor a menor para no desplazar los indices restantes
                for idx in sorted(idxs, reverse=True):
                    if self._stop:
                        self.log.emit("Cancelado.", "warn"); break
                    api.delete(idx)
                    self.log.emit(f"  eliminado slot idx={idx}", "ok")

                if self._stop:
                    err_n += 1; self.estado.emit(tit.title_id, tit.uid, "err"); break

                ok_n += 1; self.estado.emit(tit.title_id, tit.uid, "ok")

            except GarlicError as e:
                self.log.emit(f"  ERR: {e}", "error")
                err_n += 1; self.estado.emit(tit.title_id, tit.uid, "err")

        self.prog.emit(total, total)
        self.log.emit(f"\n{'═'*58}", "sep")
        self.log.emit(
            f"Fin eliminacion:  {ok_n} OK  /  {err_n} errores  de {total} titulos.", "info")
        self.done.emit()

# ──────────────────────────────────────────────────────────────────────────────
# Dialogo de Ajustes
# ──────────────────────────────────────────────────────────────────────────────
class DlgAjustes(QDialog):
    def __init__(self, cfg: Cfg, parent=None):
        super().__init__(parent)
        self.cfg = cfg
        self.setWindowTitle("Ajustes — Garlic SaveMgr")
        self.setMinimumWidth(420)
        lay = QVBoxLayout(self)

        f = QFormLayout()
        self._nom = QLineEdit(cfg.consola.nombre); self._nom.setPlaceholderText("PS5")
        self._ip  = QLineEdit(cfg.consola.ip);     self._ip.setPlaceholderText("192.168.1.x")
        self._prt = QSpinBox(); self._prt.setRange(1,65535); self._prt.setValue(cfg.consola.puerto)
        for lbl,w2 in [("Nombre:",self._nom),("IP:",self._ip),("Puerto:",self._prt)]:
            f.addRow(lbl, w2)
        b = QPushButton("Verificar conexion")
        b.clicked.connect(self._ping)
        f.addRow("", b)
        lay.addLayout(f)

        info = QLabel(
            "Las copias de seguridad se guardan en:\n"
            f"  {DIR_ENC}\n\n"
            "Logs en:\n"
            f"  {DIR_LOGS}")
        info.setStyleSheet("color:#555; font-size:10px;")
        lay.addWidget(info)

        bb = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok |
                              QDialogButtonBox.StandardButton.Cancel)
        bb.accepted.connect(self._guardar)
        bb.rejected.connect(self.reject)
        lay.addWidget(bb)

    def _ping(self):
        nom = self._nom.text().strip() or "PS5"
        ip  = self._ip.text().strip()
        if not ip:
            QMessageBox.information(self,"Verificar conexion", f"{nom}: IP no configurada.")
            return
        ok = API(ip, self._prt.value()).ping()
        QMessageBox.information(self,"Verificar conexion",
            f"{nom} ({ip}:{self._prt.value()}):  {'OK' if ok else 'sin respuesta'}")

    def _guardar(self):
        self.cfg.consola.nombre = self._nom.text().strip()
        self.cfg.consola.ip     = self._ip.text().strip()
        self.cfg.consola.puerto = self._prt.value()
        self.cfg.save(); self.accept()

# ──────────────────────────────────────────────────────────────────────────────
# Ventana principal
# ──────────────────────────────────────────────────────────────────────────────
class MainWindow(QMainWindow):
    # (titulos_crudos_o_None, error_o_None, uid_filtro) — ver _escanear_bk()
    _escaneo_bk_listo = Signal(object)

    def __init__(self, cfg: Cfg):
        super().__init__()
        self.cfg       = cfg
        self._titulos: List[Titulo]      = []
        self._backups: List[BackupEntry] = []
        self._thread   = None
        self._pipe     = None
        self.setWindowTitle(f"{APP}  v{VER}")
        self.setMinimumSize(1080, 680)
        self._escaneo_bk_listo.connect(self._on_escaneo_bk)
        self._ui(); self._actualizar_barra(); self._cargar_rst()

    # ── construccion ─────────────────────────────────────────────────────────
    def _ui(self):
        root = QWidget(); self.setCentralWidget(root)
        lay  = QVBoxLayout(root); lay.setContentsMargins(8,8,8,8); lay.setSpacing(6)

        top = QHBoxLayout()
        self._lbl_consola = QLabel("—"); self._lbl_consola.setStyleSheet("color:#666")
        btn_cfg = QPushButton("Ajustes"); btn_cfg.clicked.connect(self._ajustes)
        btn_cfg.setFixedWidth(80)
        top.addWidget(QLabel("Consola:")); top.addWidget(self._lbl_consola)
        top.addStretch(); top.addWidget(btn_cfg)
        lay.addLayout(top)

        sep = QFrame(); sep.setFrameShape(QFrame.Shape.HLine)
        sep.setStyleSheet("color:#ddd"); lay.addWidget(sep)

        sp = QSplitter(Qt.Orientation.Horizontal); sp.setChildrenCollapsible(False)
        lay.addWidget(sp, stretch=1)

        tabs = QTabWidget()
        tabs.addTab(self._tab_backup(),  "Copia de seguridad")
        tabs.addTab(self._tab_restore(), "Restaurar")
        sp.addWidget(tabs)

        right = QWidget(); rl = QVBoxLayout(right); rl.setContentsMargins(6,0,0,0)
        rl.addWidget(QLabel("Registro:"))
        self._log = QTextEdit(); self._log.setReadOnly(True)
        self._log.setFont(_font_mono())
        self._log.document().setMaximumBlockCount(6000)
        rl.addWidget(self._log, stretch=1)
        brw = QHBoxLayout()
        for lbl, path in [("Abrir carpeta de copias", DIR_ENC),
                           ("Abrir carpeta logs",      DIR_LOGS)]:
            b = QPushButton(lbl)
            b.clicked.connect(lambda _=None, p=path: self._abrir(p))
            brw.addWidget(b)
        rl.addLayout(brw)
        sp.addWidget(right)
        sp.setSizes([680, 400])

        self.statusBar().showMessage("Listo.")

    # ═══ Pestaña: copia de seguridad (consola → PC) ═══
    def _tab_backup(self) -> QWidget:
        w = QWidget(); ll = QVBoxLayout(w); ll.setContentsMargins(0,6,6,0)

        ctrl = QHBoxLayout()
        self._ed_uid  = QLineEdit(); self._ed_uid.setPlaceholderText("UID (vacio = todos)")
        self._ed_uid.setMaximumWidth(220)
        btn_scan = QPushButton("Escanear"); btn_scan.clicked.connect(self._escanear_bk)
        ctrl.addWidget(QLabel("UID:")); ctrl.addWidget(self._ed_uid)
        ctrl.addStretch(); ctrl.addWidget(btn_scan)
        ll.addLayout(ctrl)

        sel = QHBoxLayout()
        for txt, fn in [("Todos",self._sel_bk_all),("Ninguno",self._sel_bk_none)]:
            b = QPushButton(txt); b.clicked.connect(fn); b.setFixedWidth(76)
            sel.addWidget(b)
        sel.addStretch()
        self._lbl_count_bk = QLabel("0 titulos")
        sel.addWidget(self._lbl_count_bk)
        ll.addLayout(sel)

        self._tbl_bk = QTableWidget(0, 5)
        self._tbl_bk.setHorizontalHeaderLabels(["","Title ID","Slots","Nombre","UID"])
        self._config_tabla(self._tbl_bk,
            [(QHeaderView.ResizeMode.Fixed, 28),
             (QHeaderView.ResizeMode.ResizeToContents, 0),
             (QHeaderView.ResizeMode.Fixed, 46),
             (QHeaderView.ResizeMode.Stretch, 0),
             (QHeaderView.ResizeMode.ResizeToContents, 0)])
        ll.addWidget(self._tbl_bk, stretch=1)

        self._pgbar_bk = QProgressBar(); self._pgbar_bk.setTextVisible(False)
        self._pgbar_bk.setFixedHeight(6)
        ll.addWidget(self._pgbar_bk)

        btn_row = QHBoxLayout()
        self._btn_bk = QPushButton("Guardar copia en PC")
        self._btn_bk.setFixedHeight(34)
        self._btn_bk.setStyleSheet(
            "QPushButton{background:#2766c4;color:white;font-weight:bold;border-radius:4px}"
            "QPushButton:hover{background:#1d56a8}"
            "QPushButton:disabled{background:#aac;color:#dde}")
        self._btn_bk.clicked.connect(self._iniciar_bk)
        self._btn_bk_stop = QPushButton("Cancelar")
        self._btn_bk_stop.setEnabled(False)
        self._btn_bk_stop.clicked.connect(lambda: self._pipe and self._pipe.stop())
        btn_row.addWidget(self._btn_bk, stretch=1); btn_row.addWidget(self._btn_bk_stop)
        ll.addLayout(btn_row)

        del_row = QHBoxLayout()
        self._btn_del = QPushButton("Eliminar de consola")
        self._btn_del.setFixedHeight(34)
        self._btn_del.setStyleSheet(
            "QPushButton{background:#b03030;color:white;font-weight:bold;border-radius:4px}"
            "QPushButton:hover{background:#8c2020}"
            "QPushButton:disabled{background:#c88;color:#edd}")
        self._btn_del.clicked.connect(self._iniciar_del)
        del_row.addWidget(self._btn_del, stretch=1)
        ll.addLayout(del_row)
        return w

    # ═══ Pestaña: restaurar (PC → consola) ═══
    def _tab_restore(self) -> QWidget:
        w = QWidget(); ll = QVBoxLayout(w); ll.setContentsMargins(0,6,6,0)

        ctrl = QHBoxLayout()
        btn_ref = QPushButton("Actualizar lista"); btn_ref.clicked.connect(self._cargar_rst)
        ctrl.addWidget(btn_ref); ctrl.addStretch()
        ll.addLayout(ctrl)

        sel = QHBoxLayout()
        for txt, fn in [("Todos",self._sel_rst_all),("Ninguno",self._sel_rst_none)]:
            b = QPushButton(txt); b.clicked.connect(fn); b.setFixedWidth(76)
            sel.addWidget(b)
        sel.addStretch()
        self._lbl_count_rst = QLabel("0 copias")
        sel.addWidget(self._lbl_count_rst)
        ll.addLayout(sel)

        self._tbl_rst = QTableWidget(0, 6)
        self._tbl_rst.setHorizontalHeaderLabels(
            ["","Title ID","Nombre","Save","Propietario","Fecha"])
        self._config_tabla(self._tbl_rst,
            [(QHeaderView.ResizeMode.Fixed, 28),
             (QHeaderView.ResizeMode.ResizeToContents, 0),
             (QHeaderView.ResizeMode.Stretch, 0),
             (QHeaderView.ResizeMode.ResizeToContents, 0),
             (QHeaderView.ResizeMode.ResizeToContents, 0),
             (QHeaderView.ResizeMode.ResizeToContents, 0)])
        ll.addWidget(self._tbl_rst, stretch=1)

        self._pgbar_rst = QProgressBar(); self._pgbar_rst.setTextVisible(False)
        self._pgbar_rst.setFixedHeight(6)
        ll.addWidget(self._pgbar_rst)

        aviso = QLabel(
            "Antes de restaurar se verifica que CADA copia pertenezca a un "
            "perfil existente en la consola destino (uid/id/account_id/aid). "
            "Si alguna no coincide, se aborta toda la operacion y no se "
            "restaura ninguna partida.")
        aviso.setWordWrap(True)
        aviso.setStyleSheet("color:#804000; font-size:10px;")
        ll.addWidget(aviso)

        btn_row = QHBoxLayout()
        self._btn_rst = QPushButton("Restaurar seleccionados")
        self._btn_rst.setFixedHeight(34)
        self._btn_rst.setStyleSheet(
            "QPushButton{background:#2766c4;color:white;font-weight:bold;border-radius:4px}"
            "QPushButton:hover{background:#1d56a8}"
            "QPushButton:disabled{background:#aac;color:#dde}")
        self._btn_rst.clicked.connect(self._iniciar_rst)
        self._btn_rst_stop = QPushButton("Cancelar")
        self._btn_rst_stop.setEnabled(False)
        self._btn_rst_stop.clicked.connect(lambda: self._pipe and self._pipe.stop())
        btn_row.addWidget(self._btn_rst, stretch=1); btn_row.addWidget(self._btn_rst_stop)
        ll.addLayout(btn_row)

        del_loc_row = QHBoxLayout()
        self._btn_del_local = QPushButton("Eliminar copia local")
        self._btn_del_local.setFixedHeight(34)
        self._btn_del_local.setStyleSheet(
            "QPushButton{background:#b03030;color:white;font-weight:bold;border-radius:4px}"
            "QPushButton:hover{background:#8c2020}"
            "QPushButton:disabled{background:#c88;color:#edd}")
        self._btn_del_local.clicked.connect(self._borrar_locales)
        del_loc_row.addWidget(self._btn_del_local, stretch=1)
        ll.addLayout(del_loc_row)
        return w

    @staticmethod
    def _config_tabla(tbl: QTableWidget, cols):
        tbl.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        tbl.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        tbl.setAlternatingRowColors(True)
        tbl.verticalHeader().setVisible(False)
        hdr = tbl.horizontalHeader()
        for i, (mode, w) in enumerate(cols):
            hdr.setSectionResizeMode(i, mode)
            if w: tbl.setColumnWidth(i, w)

    def _operacion_activa(self) -> bool:
        return bool(self._thread and self._thread.isRunning())

    # ── barra de estado ───────────────────────────────────────────────────────
    def _actualizar_barra(self):
        c = self.cfg.consola
        self._lbl_consola.setText(f"{c.nombre or 'PS5'}  {c.ip or '—'}")

    # ── escaneo (backup) ─────────────────────────────────────────────────────
    def _escanear_bk(self):
        cons  = self.cfg.consola
        uid_f = self._ed_uid.text().strip()
        if not cons.ip:
            QMessageBox.warning(self,"","Configura la IP en Ajustes."); return
        self.statusBar().showMessage(f"Escaneando {cons.ip}…")
        self._log_msg(f"Escaneando {cons.nombre} ({cons.ip})…", "info")

        def _work():
            # Se ejecuta en un hilo aparte: aqui NUNCA se toca un widget
            # directamente, solo se emite la señal (ver _on_escaneo_bk).
            api = API(cons.ip, cons.puerto)
            if not api.ping():
                self._escaneo_bk_listo.emit((None, "Sin conexion.", uid_f))
                return
            try:
                self._escaneo_bk_listo.emit((api.scan_titles(uid_f), None, uid_f))
            except GarlicError as e:
                self._escaneo_bk_listo.emit((None, str(e), uid_f))

        threading.Thread(target=_work, daemon=True).start()

    def _on_escaneo_bk(self, resultado):
        """Slot real conectado a _escaneo_bk_listo. Al ser un metodo de un
        QObject (self), PySide6 detecta que el emisor vive en otro hilo y
        encola la llamada en el hilo de la GUI en vez de ejecutarla en el
        hilo en segundo plano — a diferencia de un lambda o una funcion
        anidada suelta, que no tienen hilo asociado y se ejecutan tal cual
        en el hilo que emite la señal."""
        raw, err, uid_f = resultado
        if err:
            self.statusBar().showMessage(f"Error: {err}")
            self._log_msg(f"Error: {err}", "error"); return
        self._titulos = [
            Titulo(title_id    = t.get("title_id",""),
                   uid         = t.get("uid",""),
                   title_name  = t.get("title_name",""),
                   slot_count  = t.get("slot_count", len(t.get("slots",[]))),
                   backup_count= t.get("backup_count",0),
                   slots       = t.get("slots",[]))
            for t in raw if _es_ps5(t)]
        self._poblar_bk()
        msg = f"{len(self._titulos)} titulos"
        if uid_f: msg += f"  (UID={uid_f})"
        self.statusBar().showMessage(msg)
        self._log_msg(msg + ".", "ok")

    def _poblar_bk(self):
        self._tbl_bk.setRowCount(0)
        for tit in self._titulos:
            r = self._tbl_bk.rowCount(); self._tbl_bk.insertRow(r)
            chk = QTableWidgetItem()
            chk.setCheckState(Qt.CheckState.Checked)
            chk.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsUserCheckable)
            self._tbl_bk.setItem(r, 0, chk)
            self._tbl_bk.setItem(r, 1, QTableWidgetItem(tit.title_id))
            self._tbl_bk.setItem(r, 2, QTableWidgetItem(str(tit.slot_count)))
            self._tbl_bk.setItem(r, 3, QTableWidgetItem(tit.title_name))
            self._tbl_bk.setItem(r, 4, QTableWidgetItem(tit.uid))
        self._lbl_count_bk.setText(f"{len(self._titulos)} titulos")

    def _sel_bk(self, state: Qt.CheckState):
        for r in range(self._tbl_bk.rowCount()):
            self._tbl_bk.item(r, 0).setCheckState(state)
    def _sel_bk_all(self):  self._sel_bk(Qt.CheckState.Checked)
    def _sel_bk_none(self): self._sel_bk(Qt.CheckState.Unchecked)

    def _iniciar_bk(self):
        if self._operacion_activa():
            QMessageBox.warning(self,"","Ya hay una operacion en curso."); return
        cons = self.cfg.consola
        if not cons.ip:
            QMessageBox.warning(self,"","Configura la IP en Ajustes."); return
        sel = [t for r,t in enumerate(self._titulos)
               if self._tbl_bk.item(r,0).checkState()==Qt.CheckState.Checked]
        if not sel:
            QMessageBox.information(self,"","Selecciona al menos un titulo."); return

        self._btn_bk.setEnabled(False); self._btn_del.setEnabled(False)
        self._btn_bk_stop.setEnabled(True)
        self._pgbar_bk.setValue(0); self._pgbar_bk.setMaximum(len(sel))
        self._log_msg(f"\n{'='*58}", "sep")
        self._log_msg(f"Guardando copia de seguridad  —  {len(sel)} titulos  ({cons.ip})", "info")

        self._pipe   = BackupPipeline(sel, cons)
        self._thread = QThread()
        self._pipe.moveToThread(self._thread)
        self._thread.started.connect(self._pipe.run)
        self._pipe.log.connect(self._log_msg)
        self._pipe.prog.connect(self._prog_bk)
        self._pipe.estado.connect(self._marcar_bk)
        self._pipe.done.connect(self._fin_bk)
        self._thread.start()

    def _fin_bk(self):
        self._btn_bk.setEnabled(True); self._btn_del.setEnabled(True)
        self._btn_bk_stop.setEnabled(False)
        if self._thread: self._thread.quit(); self._thread.wait()
        self.statusBar().showMessage("Listo.")
        self._cargar_rst()

    def _marcar_bk(self, title_id: str, uid: str, estado: str):
        col = {"proc":"#e08000","ok":"#007000","err":"#c00000"}
        for r,t in enumerate(self._titulos):
            if t.title_id == title_id and t.uid == uid:
                it = self._tbl_bk.item(r,1)
                if it: it.setForeground(QColor(col.get(estado,"#333")))

    def _prog_bk(self, hecho: int, total: int):
        self._pgbar_bk.setMaximum(max(total, 1))
        self._pgbar_bk.setValue(hecho)

    # ── eliminar de consola ───────────────────────────────────────────────────
    def _iniciar_del(self):
        if self._operacion_activa():
            QMessageBox.warning(self,"","Ya hay una operacion en curso."); return
        cons = self.cfg.consola
        if not cons.ip:
            QMessageBox.warning(self,"","Configura la IP en Ajustes."); return
        sel = [t for r, t in enumerate(self._titulos)
               if self._tbl_bk.item(r, 0) and
               self._tbl_bk.item(r, 0).checkState() == Qt.CheckState.Checked]
        if not sel:
            QMessageBox.information(self,"","Selecciona al menos un titulo."); return

        lista = "\n".join(f"  • {t.title_id}  {t.title_name or '—'}" for t in sel)
        if QMessageBox.warning(
                self, "Confirmar eliminacion en consola",
                f"Se eliminaran {len(sel)} titulo(s) de la consola {cons.nombre} ({cons.ip}):\n\n"
                f"{lista}\n\n"
                "Esta operacion NO tiene deshacer. ¿Continuar?",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.Cancel,
                QMessageBox.StandardButton.Cancel) != QMessageBox.StandardButton.Yes:
            return

        self._btn_bk.setEnabled(False); self._btn_del.setEnabled(False)
        self._btn_bk_stop.setEnabled(True)
        self._pgbar_bk.setValue(0); self._pgbar_bk.setMaximum(len(sel))
        self._log_msg(f"\n{'='*58}", "sep")
        self._log_msg(f"Eliminando de consola  —  {len(sel)} titulos  ({cons.ip})", "warn")

        self._pipe   = DeleteConsolePipeline(sel, cons)
        self._thread = QThread()
        self._pipe.moveToThread(self._thread)
        self._thread.started.connect(self._pipe.run)
        self._pipe.log.connect(self._log_msg)
        self._pipe.prog.connect(self._prog_bk)
        self._pipe.estado.connect(self._marcar_bk)
        self._pipe.done.connect(self._fin_del)
        self._thread.start()

    def _fin_del(self):
        self._btn_bk.setEnabled(True); self._btn_del.setEnabled(True)
        self._btn_bk_stop.setEnabled(False)
        if self._thread: self._thread.quit(); self._thread.wait()
        self.statusBar().showMessage("Listo.")

    # ── restaurar ─────────────────────────────────────────────────────────────
    def _cargar_rst(self):
        self._backups = leer_backups_locales()
        self._poblar_rst()

    def _poblar_rst(self):
        self._tbl_rst.setRowCount(0)
        for b in self._backups:
            r = self._tbl_rst.rowCount(); self._tbl_rst.insertRow(r)
            chk = QTableWidgetItem()
            chk.setCheckState(Qt.CheckState.Checked)
            chk.setFlags(Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsUserCheckable)
            prop = ", ".join(f"{k}={v}" for k,v in b.propietario.items()) or "—"
            self._tbl_rst.setItem(r, 0, chk)
            self._tbl_rst.setItem(r, 1, QTableWidgetItem(b.title_id))
            self._tbl_rst.setItem(r, 2, QTableWidgetItem(b.title_name))
            self._tbl_rst.setItem(r, 3, QTableWidgetItem(b.save_name))
            self._tbl_rst.setItem(r, 4, QTableWidgetItem(prop))
            self._tbl_rst.setItem(r, 5, QTableWidgetItem(b.fecha))
        self._lbl_count_rst.setText(f"{len(self._backups)} copias")

    def _sel_rst(self, state: Qt.CheckState):
        for r in range(self._tbl_rst.rowCount()):
            self._tbl_rst.item(r, 0).setCheckState(state)
    def _sel_rst_all(self):  self._sel_rst(Qt.CheckState.Checked)
    def _sel_rst_none(self): self._sel_rst(Qt.CheckState.Unchecked)

    def _iniciar_rst(self):
        if self._operacion_activa():
            QMessageBox.warning(self,"","Ya hay una operacion en curso."); return
        cons = self.cfg.consola
        if not cons.ip:
            QMessageBox.warning(self,"","Configura la IP en Ajustes."); return
        sel = [b for r,b in enumerate(self._backups)
               if self._tbl_rst.item(r,0).checkState()==Qt.CheckState.Checked]
        if not sel:
            QMessageBox.information(self,"","Selecciona al menos una copia."); return

        self._btn_rst.setEnabled(False); self._btn_del_local.setEnabled(False)
        self._btn_rst_stop.setEnabled(True)
        self._pgbar_rst.setValue(0); self._pgbar_rst.setMaximum(len(sel))
        self._log_msg(f"\n{'='*58}", "sep")
        self._log_msg(f"Restaurando  —  {len(sel)} copias  →  {cons.ip}", "info")

        self._pipe   = RestorePipeline(sel, cons)
        self._thread = QThread()
        self._pipe.moveToThread(self._thread)
        self._thread.started.connect(self._pipe.run)
        self._pipe.log.connect(self._log_msg)
        self._pipe.prog.connect(self._prog_rst)
        self._pipe.estado.connect(self._marcar_rst)
        self._pipe.done.connect(self._fin_rst)
        self._thread.start()

    def _fin_rst(self):
        self._btn_rst.setEnabled(True); self._btn_del_local.setEnabled(True)
        self._btn_rst_stop.setEnabled(False)
        if self._thread: self._thread.quit(); self._thread.wait()
        self.statusBar().showMessage("Listo.")

    # ── eliminar copias locales (PC) ──────────────────────────────────────────
    def _borrar_locales(self):
        if self._operacion_activa():
            QMessageBox.warning(self,"","Ya hay una operacion en curso."); return
        sel_idx = [r for r in range(self._tbl_rst.rowCount())
                   if self._tbl_rst.item(r, 0) and
                   self._tbl_rst.item(r, 0).checkState() == Qt.CheckState.Checked]
        sel = [self._backups[r] for r in sel_idx]
        if not sel:
            QMessageBox.information(self,"","Selecciona al menos una copia."); return

        lista = "\n".join(
            f"  • {b.title_id}  {b.save_name}  ({b.fecha})" for b in sel)
        if QMessageBox.warning(
                self, "Eliminar copias locales",
                f"Se eliminaran {len(sel)} copia(s) del PC:\n\n{lista}\n\n"
                "Esta operacion NO tiene deshacer. ¿Continuar?",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.Cancel,
                QMessageBox.StandardButton.Cancel) != QMessageBox.StandardButton.Yes:
            return

        self._log_msg(f"\n{'='*58}", "sep")
        self._log_msg(f"Eliminando {len(sel)} copia(s) local(es)…", "warn")
        err_n = 0
        for b in sel:
            try:
                b.img_path.unlink(missing_ok=True)
                b.img_path.with_suffix(".json").unlink(missing_ok=True)
                self._log_msg(f"  Eliminado: {b.img_path.name}", "ok")
            except OSError as e:
                self._log_msg(f"  ERR al eliminar {b.img_path.name}: {e}", "error")
                err_n += 1
        ok_n = len(sel) - err_n
        self._log_msg(f"\n{'═'*58}", "sep")
        self._log_msg(
            f"Fin:  {ok_n} eliminado(s)  /  {err_n} error(es).",
            "ok" if err_n == 0 else "warn")
        self._cargar_rst()

    def _marcar_rst(self, row: int, estado: str):
        col = {"proc":"#e08000","ok":"#007000","err":"#c00000"}
        it = self._tbl_rst.item(row,1)
        if it: it.setForeground(QColor(col.get(estado,"#333")))

    def _prog_rst(self, hecho: int, total: int):
        self._pgbar_rst.setMaximum(max(total, 1))
        self._pgbar_rst.setValue(hecho)

    # ── log ───────────────────────────────────────────────────────────────────
    def _log_msg(self, msg: str, nivel: str = "info"):
        c = {"info":"#222","ok":"#005500","warn":"#804000",
             "error":"#990000","sep":"#888"}.get(nivel,"#222")
        ts = datetime.now().strftime("%H:%M:%S")
        self._log.append(
            f'<span style="color:#999">{ts}</span> '
            f'<span style="color:{c}">{msg.replace("<","&lt;")}</span>')
        (L.warning if nivel=="warn" else L.error if nivel=="error" else L.info)(msg)

    def _ajustes(self):
        if DlgAjustes(self.cfg, self).exec():
            self._actualizar_barra()
            self._log_msg("Configuracion guardada.", "ok")

    @staticmethod
    def _abrir(path: Path):
        path.mkdir(parents=True, exist_ok=True)
        if platform.system()=="Windows": os.startfile(str(path))
        elif platform.system()=="Darwin": subprocess.Popen(["open",str(path)])
        else: subprocess.Popen(["xdg-open",str(path)])

# ──────────────────────────────────────────────────────────────────────────────
# Tema claro
# ──────────────────────────────────────────────────────────────────────────────
def _font_mono(sz: int = 9) -> QFont:
    return QFont("Consolas", sz) if platform.system() == "Windows" else QFont("Monospace", sz)


def _tema_claro(app: QApplication):
    app.setStyle("Fusion")
    p = QPalette()
    bg = QColor(248, 248, 248)
    p.setColor(QPalette.ColorRole.Window,          bg)
    p.setColor(QPalette.ColorRole.WindowText,      QColor(30,  30,  30))
    p.setColor(QPalette.ColorRole.Base,            QColor(255, 255, 255))
    p.setColor(QPalette.ColorRole.AlternateBase,   QColor(240, 243, 248))
    p.setColor(QPalette.ColorRole.ToolTipBase,     QColor(255, 255, 220))
    p.setColor(QPalette.ColorRole.ToolTipText,     QColor(30,  30,  30))
    p.setColor(QPalette.ColorRole.Text,            QColor(30,  30,  30))
    p.setColor(QPalette.ColorRole.Button,          QColor(230, 232, 236))
    p.setColor(QPalette.ColorRole.ButtonText,      QColor(30,  30,  30))
    p.setColor(QPalette.ColorRole.BrightText,      Qt.GlobalColor.white)
    p.setColor(QPalette.ColorRole.Link,            QColor(0,   90,  200))
    p.setColor(QPalette.ColorRole.Highlight,       QColor(0,   100, 200))
    p.setColor(QPalette.ColorRole.HighlightedText, Qt.GlobalColor.white)
    p.setColor(QPalette.ColorGroup.Disabled,
               QPalette.ColorRole.WindowText,      QColor(160, 160, 160))
    p.setColor(QPalette.ColorGroup.Disabled,
               QPalette.ColorRole.ButtonText,      QColor(160, 160, 160))
    app.setPalette(p)
    f = app.font()
    f.setFamily("Tahoma" if platform.system()=="Windows" else "DejaVu Sans")
    f.setPointSize(9)
    app.setFont(f)

# ──────────────────────────────────────────────────────────────────────────────
# Arranque
# ──────────────────────────────────────────────────────────────────────────────
def main():
    app = QApplication(sys.argv)
    app.setApplicationName(APP)
    app.setOrganizationName(ORG)
    _tema_claro(app)
    cfg = Cfg.load()
    win = MainWindow(cfg)
    win.show()
    if not cfg.consola.ip:
        if DlgAjustes(cfg, win).exec():
            win._actualizar_barra()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()
