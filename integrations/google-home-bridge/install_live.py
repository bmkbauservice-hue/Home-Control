#!/usr/bin/env python3
"""Home Control: Google Home Sensoren -> Klima. Arbeitet nur im Wetter-Testworktree."""
from pathlib import Path
from datetime import datetime
import ipaddress, os, re, secrets, signal, socket, subprocess, sys, time, urllib.request

ROOT = Path.home() / "Schreibtisch/Umschulung/Home-Control-Wetter"
ANDROID = Path.home() / "Schreibtisch/Umschulung/google-home-api-sample-app-android"
BRANCH = "feature/live-google-home-klima"
BASE = "integrations/google-home-bridge/"
changes = {}
backup = Path.home() / ".local/share/home-control/bridge-backups" / datetime.now().strftime("%Y%m%d-%H%M%S")

def stop(msg):
    raise SystemExit("ABBRUCH: " + msg)

def remote(filename):
    cmd = ["git", "-C", str(ROOT), "show", "FETCH_HEAD:" + BASE + filename]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode: stop("GitHub-Quelldatei nicht gefunden: " + filename)
    return result.stdout

def patch(path, old, new):
    text = changes.get(path, path.read_text())
    if new in text: return
    if text.count(old) != 1: stop("Einfügestelle fehlt/mehrdeutig: " + str(path))
    changes[path] = text.replace(old, new, 1)

def source(path, filename):
    text = remote(filename)
    if path.exists() and path.read_text() != text:
        stop("Vorhandene Datei abweichend, nicht überschrieben: " + str(path))
    changes[path] = text

def add(path, text, identifier):
    original = changes.get(path, path.read_text())
    if identifier not in original: changes[path] = original + "\n" + text

def build(cmd, cwd, timeout=240):
    print("Baue:", " ".join(cmd), flush=True)
    try:
        subprocess.run(cmd, cwd=cwd, check=True, timeout=timeout)
        return True
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, FileNotFoundError) as exc:
        print("Build fehlgeschlagen:", exc)
        return False

def set_up_backend():
    db = ROOT / "backend/HomeControl.Api/Climate/ClimateDbContext.cs"
    program = ROOT / "backend/HomeControl.Api/Program.cs"
    if not db.exists(): stop("Klima-Modul fehlt. Keine Änderung.")
    patch(db,
      "    public DbSet<ClimateRoomVisibility> RoomVisibility => Set<ClimateRoomVisibility>();",
      "    public DbSet<ClimateRoomVisibility> RoomVisibility => Set<ClimateRoomVisibility>();\n    public DbSet<BridgeSensor> Sensors => Set<BridgeSensor>();")
    patch(db, "        modelBuilder.Entity<ClimateRoomVisibility>(entity =>",
      """        modelBuilder.Entity<BridgeSensor>(entity =>
        {
            entity.ToTable("ClimateSensors");
            entity.HasKey(s => s.SensorId);
            entity.Property(s => s.SensorId).HasMaxLength(256).IsRequired();
            entity.Property(s => s.Name).HasMaxLength(120).IsRequired();
        });
        modelBuilder.Entity<ClimateRoomVisibility>(entity =>""")
    patch(program, "app.MapClimateEndpoints();",
      "app.MapClimateEndpoints();\napp.MapClimateBridgeEndpoints();")
    source(ROOT / "backend/HomeControl.Api/Climate/ClimateBridgeEndpoints.cs",
      "ClimateBridgeEndpoints.cs")

def set_up_frontend():
    page = ROOT / "frontend/src/pages/ClimatePage.tsx"
    patch(page, "import { getRooms, type Room } from '../api/rooms'",
      "import { getRooms, type Room } from '../api/rooms'\nimport { getBridgeSensors, assignBridgeSensor, type BridgeSensor } from '../api/climateSensors'")
    patch(page, "  const [readings, setReadings] = useState<ClimateReading[]>([])",
      "  const [readings, setReadings] = useState<ClimateReading[]>([])\n  const [bridgeSensors, setBridgeSensors] = useState<BridgeSensor[]>([])")
    patch(page,
      "return Promise.all([getRooms(), getClimate(), getClimateRoomVisibility()])\n      .then(([loadedRooms, loadedReadings, loadedVisibility]) => {",
      "return Promise.all([getRooms(), getClimate(), getClimateRoomVisibility(), getBridgeSensors()])\n      .then(([loadedRooms, loadedReadings, loadedVisibility, loadedSensors]) => {")
    patch(page, "        setVisibility(loadedVisibility)",
      "        setVisibility(loadedVisibility)\n        setBridgeSensors(loadedSensors)")
    patch(page, "  const climateRooms = rooms.filter((room) => {",
      """  async function linkSensor(id: string, roomId: string | null) {
    setBusy(id)
    setError(null)
    try {
      await assignBridgeSensor(id, roomId)
      await loadData()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(null)
    }
  }

  const climateRooms = rooms.filter((room) => {""")
    panel = """      <section className="climate-bridge-panel" aria-label="Google Home Sensoren">
        <header className="climate-bridge-header">
          <h2>📡 Automatische Sensoren</h2>
          <span>{bridgeSensors.length} erkannt</span>
        </header>
        {bridgeSensors.length === 0 ? <p>Noch keine Sensordaten übertragen. Bitte die aktualisierte
          Google-Home-Test-App auf dem Android-Handy im selben WLAN öffnen.</p> :
          <div className="climate-bridge-list">
            {bridgeSensors.map((sensor) => (
              <div className="climate-bridge-row" key={sensor.sensorId}>
                <div>
                  <strong>{sensor.name}</strong>
                  <small>🌡 {display(sensor.temperatureCelsius, '°C')} · 💧 {display(sensor.humidityPercent, '%')}</small>
                  <small>Übertragen: {measuredAt(sensor.updatedAtUtc)}
                    {Date.now() - new Date(sensor.updatedAtUtc).getTime() > 20 * 60_000 ? ' · älterer Wert' : ''}</small>
                </div>
                <label>
                  <span className="visually-hidden">Raum für {sensor.name}</span>
                  <select value={sensor.roomId ?? ''} disabled={!isAdmin || busy !== null}
                    onChange={(event) => void linkSensor(sensor.sensorId, event.target.value || null)}>
                    <option value="">Nicht zugeordnet</option>
                    {rooms.map((room) => <option key={room.id} value={room.id}>{room.name}</option>)}
                  </select>
                </label>
              </div>
            ))}
          </div>}
      </section>
"""
    patch(page, '      <div className="climate-summary">', panel + '      <div className="climate-summary">')
    source(ROOT / "frontend/src/api/climateSensors.ts", "climateSensors.ts")
    add(ROOT / "frontend/src/pages/ClimatePage.css", """
/* Google-Home-Sensoren */
.climate-bridge-panel {padding:18px;border:1px solid var(--glass-border,#33465e);border-radius:18px;
  background:var(--surface,#172337);display:grid;gap:12px}
.climate-bridge-header {display:flex;justify-content:space-between;align-items:center;gap:12px}
.climate-bridge-header h2 {font-size:1.2rem;margin:0}
.climate-bridge-panel p {color:var(--text-muted,#a8b7c7);margin:0}
.climate-bridge-list {display:grid;gap:10px}
.climate-bridge-row {display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:12px;
  padding:12px;background:rgba(255,255,255,.04);border-radius:12px}
.climate-bridge-row>div {display:grid;gap:5px}
.climate-bridge-row small {display:block;color:var(--text-muted,#a8b7c7)}
.climate-bridge-row select {padding:10px;min-height:42px;border-radius:9px;background:#15233a;
  color:white;border:1px solid #50627c}
""", "/* Google-Home-Sensoren */")

def set_up_android():
    module = ANDROID / "app/src/main/java/com/example/googlehomeapisampleapp/HomeModule.kt"
    vm = ANDROID / "app/src/main/java/com/example/googlehomeapisampleapp/viewmodel/devices/DeviceViewModel.kt"
    gradle = ANDROID / "app/build.gradle.kts"
    if not (module.exists() and vm.exists() and gradle.exists()):
        print("Android-Quellen fehlen; Android-Einbau übersprungen.")
        return False
    patch(module, "import com.google.home.matter.standard.TemperatureSensorDevice",
      "import com.google.home.matter.standard.TemperatureSensorDevice\nimport com.google.home.matter.standard.HumiditySensorDevice\nimport com.google.home.matter.standard.RelativeHumidityMeasurement")
    patch(module, "    TemperatureSensorDevice,",
      "    TemperatureSensorDevice,\n    HumiditySensorDevice,")
    patch(module, "      TemperatureMeasurement,",
      "      TemperatureMeasurement,\n      RelativeHumidityMeasurement,")
    patch(vm, "import com.google.home.matter.standard.TemperatureSensorDevice",
      "import com.google.home.matter.standard.TemperatureSensorDevice\nimport com.google.home.matter.standard.RelativeHumidityMeasurement\nimport com.example.googlehomeapisampleapp.bridge.HomeControlBridge")
    patch(vm, "import kotlinx.coroutines.flow.map",
      "import kotlinx.coroutines.flow.map\nimport kotlinx.coroutines.flow.first\nimport kotlinx.coroutines.isActive")
    patch(vm, "    viewModelScope.launch { subscribeToOta() }",
      "    viewModelScope.launch { subscribeToOta() }\n    viewModelScope.launch { sendClimateToHomeControl() }")
    patch(vm, "  private suspend fun subscribeToType() {",
      """  private suspend fun sendClimateToHomeControl() {
    while (kotlin.coroutines.coroutineContext.isActive) {
      try {
        val traits = device.types().first().flatMap { it.traits() }
        val temp = traits.filterIsInstance<TemperatureMeasurement>()
          .firstNotNullOfOrNull { it.measuredValue?.toDouble()?.div(100.0) }
          ?.takeIf { it in -40.0..60.0 }
        val humidity = traits.filterIsInstance<RelativeHumidityMeasurement>()
          .firstNotNullOfOrNull { it.measuredValue?.toDouble()?.div(100.0) }
          ?.takeIf { it in 0.0..100.0 }
        if (temp != null || humidity != null)
          HomeControlBridge.publish(id, name.value, temp, humidity)
      } catch (error: Exception) {
        Log.w("HomeControlBridge", "Messwerte noch nicht verfügbar: " + error.message)
      }
      delay(60_000L)
    }
  }

  private suspend fun subscribeToType() {""")
    source(ANDROID / "app/src/main/java/com/example/googlehomeapisampleapp/bridge/HomeControlBridge.kt",
      "HomeControlBridge.kt")
    manifest = ANDROID / "app/src/debug/AndroidManifest.xml"
    if not manifest.exists():
        changes[manifest] = """<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
  <application android:usesCleartextTraffic="true" />
</manifest>
"""
    elif 'usesCleartextTraffic="true"' not in manifest.read_text():
        stop("Android-Debug-Manifest wurde bereits angepasst.")
    marker = '    buildConfigField("String", "DEFAULT_WEB_CLIENT_ID", "\\"$webClientIdDev\\"")'
    addition = marker + """
    val hcUrl = localProperties.getProperty("HOME_CONTROL_SENSOR_URL", "")
    val hcKey = localProperties.getProperty("HOME_CONTROL_SENSOR_KEY", "")
    buildConfigField("String", "HOME_CONTROL_SENSOR_URL", "\\"$hcUrl\\"")
    buildConfigField("String", "HOME_CONTROL_SENSOR_KEY", "\\"$hcKey\\"")
"""
    patch(gradle, marker, addition)
    return True

def save_changes():
    original = {}
    for path, content in changes.items():
        old = path.read_bytes() if path.exists() else None
        if old is not None and old.decode() == content: continue
        original[path] = old
        if old is not None:
            base = ANDROID if path.is_relative_to(ANDROID) else ROOT
            target = backup / ("android" if base == ANDROID else "wetter") / path.relative_to(base)
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(old)
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(content)
    return original

def restore(original, android_only=False):
    for path, value in original.items():
        if android_only and not path.is_relative_to(ANDROID): continue
        if value is None: path.unlink(missing_ok=True)
        else: path.write_bytes(value)

def key_file():
    file = Path.home() / ".config/home-control/sensor-bridge.key"
    file.parent.mkdir(parents=True,exist_ok=True)
    if not file.exists():
        fd = os.open(file, os.O_WRONLY|os.O_CREAT|os.O_EXCL, 0o600)
        with os.fdopen(fd,"w") as handle: handle.write(secrets.token_urlsafe(48)+"\n")
    key = file.read_text().strip()
    if len(key) < 32: stop("Vorhandener Bridge-Schlüssel zu kurz.")
    return key

def get_ip():
    try:
        with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as s:
            s.connect(("1.1.1.1",80))
            ip = s.getsockname()[0]
        return ip if ipaddress.ip_address(ip).is_private else None
    except OSError:
        return None

def port_pids(port):
    p = subprocess.run(["ss","-ltnp",f"sport = :{port}"],capture_output=True,text=True)
    return sorted({int(x) for x in re.findall(r"pid=(\d+)",p.stdout)})

def owned(pid):
    try:
        return Path(os.readlink(f"/proc/{pid}/cwd")).resolve().is_relative_to(ROOT.resolve())
    except OSError: return False

def backend_restart(key):
    ids = port_pids(5290)
    if any(not owned(pid) for pid in ids):
        print("Port 5290 gehört nicht eindeutig diesem Testprojekt. Kein Neustart.")
        return False
    for pid in ids:
        try:
            os.kill(pid,signal.SIGCONT)
            os.kill(pid,signal.SIGTERM)
        except ProcessLookupError: pass
    for _ in range(30):
        if not port_pids(5290): break
        time.sleep(.25)
    if port_pids(5290):
        print("Port 5290 ist noch belegt.")
        return False
    env = os.environ.copy()
    env["SensorBridge__Key"] = key
    with open("/tmp/home-control-wetter-api.log","ab") as output:
        subprocess.Popen(["dotnet","run","--project","backend/HomeControl.Api",
          "--no-build","--urls","http://127.0.0.1:5290"],
          cwd=ROOT,env=env,stdin=subprocess.DEVNULL,stdout=output,stderr=subprocess.STDOUT,
          start_new_session=True)
    for _ in range(30):
        time.sleep(.5)
        try:
            with urllib.request.urlopen("http://127.0.0.1:5290/api/weather/current",timeout=2) as resp:
                if resp.status==200: return True
        except Exception: pass
    print("Backendstart fehlgeschlagen, siehe /tmp/home-control-wetter-api.log")
    return False

def gateway_start():
    if port_pids(5390):
        print("Gateway Port 5390 bereits belegt – kein fremder Prozess beendet.")
        return
    file = ROOT / "tools/sensor_gateway.py"
    file.parent.mkdir(parents=True,exist_ok=True)
    expected = remote("sensor_gateway.py")
    if file.exists() and file.read_text()!=expected:
        print("Gateway-Datei abweichend. Nicht überschrieben.")
        return
    file.write_text(expected)
    with open("/tmp/home-control-sensor-gateway.log","ab") as output:
        subprocess.Popen([sys.executable,str(file)],cwd=ROOT,stdin=subprocess.DEVNULL,
          stdout=output,stderr=subprocess.STDOUT,start_new_session=True)
    time.sleep(1)
    try:
        with urllib.request.urlopen("http://127.0.0.1:5390/healthz",timeout=2) as resp:
            print("Sensor-Gateway:",resp.status)
    except Exception as err: print("Gateway nicht erreichbar:",err)

def android_config(key,ip):
    props = ANDROID / "local.properties"
    current = props.read_text() if props.exists() else ""
    current = "\n".join(x for x in current.splitlines() if not x.startswith("HOME_CONTROL_SENSOR_"))
    props.write_text(current.rstrip()+"\nHOME_CONTROL_SENSOR_URL=http://"+ip+
      ":5390\nHOME_CONTROL_SENSOR_KEY="+key+"\n")
    os.chmod(props,0o600)

def install_phone():
    try:
        r = subprocess.run(["adb","devices"],capture_output=True,text=True)
    except FileNotFoundError:
        print("ADB fehlt – bitte Handy später anschließen.")
        return
    if "c84bca2\tdevice" not in r.stdout:
        print("Xiaomi nicht per ADB verbunden; APK wurde nur gebaut.")
        print("Später: adb -s c84bca2 install -r "+str(ANDROID/"app/build/outputs/apk/debug/app-debug.apk"))
        return
    build(["adb","-s","c84bca2","install","-r",
           str(ANDROID/"app/build/outputs/apk/debug/app-debug.apk")],ANDROID,timeout=90)

def main():
    if not (ROOT / "backend/HomeControl.Api/Program.cs").exists(): stop("Wetter-Testprojekt fehlt.")
    br = subprocess.run(["git","branch","--show-current"],cwd=ROOT,capture_output=True,text=True)
    if br.stdout.strip()!="feature/wetter-himmel": stop("Falscher Git-Branch – keine Änderung.")
    print("=== Live-Sensoren: Quellen einbauen ===",flush=True)
    set_up_backend()
    set_up_frontend()
    android = set_up_android()
    previous = save_changes()
    if not build(["dotnet","build","backend/HomeControl.Api/HomeControl.Api.csproj","--no-restore"],ROOT):
        restore(previous);stop("C#-Build fehlgeschlagen, alle Code-Änderungen zurückgesetzt.")
    if not build(["npm","run","build"],ROOT/"frontend"):
        restore(previous);stop("React-Build fehlgeschlagen, alle Code-Änderungen zurückgesetzt.")
    key=key_file()
    ip=get_ip()
    if android and ip:
        android_config(key,ip)
        if build(["./gradlew","assembleDebug","--offline"],ANDROID,timeout=300) or build(["./gradlew","assembleDebug"],ANDROID,timeout=300):
            install_phone()
        else:
            restore(previous,android_only=True)
            print("Android-Build fehlgeschlagen. Android-Änderungen zurückgesetzt.")
    elif android:
        print("Keine private LAN-IP erkannt, Android nicht konfiguriert.")
    if backend_restart(key): gateway_start()
    print("=== FERTIG: Quellen eingebaut, nicht mit Hardware verifiziert ===")
    print("Klima im Browser: http://127.0.0.1:5190")
    print("Sicherung:",backup)
    print("Für Live-Werte: Android-Test-App starten; neue Sensoren auf der Klima-Seite den Räumen zuordnen.")
    print("Die Android-App muss laufen; im Hintergrund kann Android die Übertragung pausieren.")

if __name__=="__main__":
    main()
