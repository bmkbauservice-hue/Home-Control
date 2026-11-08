import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "android-bridge-einrichten.py"
spec = importlib.util.spec_from_file_location("android_setup", SCRIPT)
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)


class AndroidBridgeSetupTests(unittest.TestCase):
    def test_usb_phone_is_selected_with_network_box_connected(self):
        devices = "List of devices attached\nPHONE device usb:3-1 model:Phone\nBOX device model:TV\n"
        self.assertEqual("PHONE", setup.select_device(devices))

    def test_explicit_phone_selection_is_used_with_multiple_usb_devices(self):
        devices = "FIRST device usb:1\nSECOND device usb:2\n"
        self.assertEqual("SECOND", setup.select_device(devices, "SECOND"))

    def test_multiple_usb_devices_require_explicit_selection(self):
        with self.assertRaisesRegex(ValueError, "Mehrere USB-Geräte"):
            setup.select_device("FIRST device usb:1\nSECOND device usb:2\n")

    def test_unauthorized_and_offline_devices_are_not_selected(self):
        for state in ("unauthorized", "offline"):
            with self.subTest(state=state):
                devices = f"PHONE {state} usb:1\nBOX device model:TV\n"
                with self.assertRaisesRegex(ValueError, "nicht verbunden"):
                    setup.select_device(devices, "PHONE")
                with self.assertRaisesRegex(ValueError, "Kein freigegebenes USB"):
                    setup.select_device(devices)

    def test_network_box_alone_is_not_implicitly_selected(self):
        with self.assertRaisesRegex(ValueError, "Kein freigegebenes USB"):
            setup.select_device("List of devices attached\nBOX device model:TV\n")

    def test_empty_device_list_is_rejected(self):
        with self.assertRaises(ValueError):
            setup.select_device("List of devices attached\n")

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "android"
        self.files = {
            setup.PACKAGE / "viewmodel/devices/DeviceViewModel.kt":
                "class DeviceViewModel {\n        traits.emit(combinedTraits)\n}\n",
            setup.PACKAGE / "HomeModule.kt":
                "import com.google.home.matter.standard.TemperatureMeasurement\n"
                "import com.google.home.matter.standard.TemperatureSensorDevice\n"
                "val types = listOf(\n    TemperatureSensorDevice,\n)\n"
                "val traits = listOf(\n    TemperatureMeasurement,\n)\n"
                "val scope = HOME_PLATFORM_SCOPE_VERSION_1\n",
            Path("app/build.gradle.kts"):
                'android {\n  defaultConfig {\n    applicationId = "existing.app"\n  }\n'
                '  buildFeatures { buildConfig = true }\n}\n',
            Path("app/src/main/AndroidManifest.xml"):
                f'<manifest xmlns:android="{setup.ANDROID}"><application android:name=".HomeApplication" /></manifest>',
            Path("local.properties"): "WEB_CLIENT_ID_DEV=existing-client\n",
        }
        for path, content in self.files.items():
            target = self.root / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content)

    def test_plan_is_read_only_and_preserves_oauth(self):
        planned = setup.plan_changes(self.root, "bridge")
        for path, content in self.files.items():
            self.assertEqual(content, (self.root / path).read_text())
        self.assertNotIn(Path("local.properties"), planned)
        self.assertIn("HOME_PLATFORM_SCOPE_VERSION_1", planned[setup.PACKAGE / "HomeModule.kt"])
        self.assertIn('applicationId = "existing.app"', planned[Path("app/build.gradle.kts")])

    def test_drift_stops_before_writing(self):
        path = self.root / setup.PACKAGE / "viewmodel/devices/DeviceViewModel.kt"
        path.write_text("changed upstream source")
        with self.assertRaisesRegex(ValueError, "Quellcode weicht ab"):
            setup.plan_changes(self.root, "bridge")
        self.assertEqual(self.files[Path("app/build.gradle.kts")], (self.root / "app/build.gradle.kts").read_text())

    def test_existing_custom_bridge_is_kept(self):
        path = self.root / setup.PACKAGE / "bridge/HomeControlBridge.kt"
        path.parent.mkdir()
        path.write_text("custom")
        with self.assertRaisesRegex(ValueError, "Andere Bridge-Datei"):
            setup.plan_changes(self.root, "bridge")
        self.assertEqual("custom", path.read_text())

    def test_usb_weiterleitung_wird_erneuert_bis_gateway_endet(self):
        class Gateway:
            returncode = 0
            calls = 0

            def poll(self):
                self.calls += 1
                return None if self.calls < 3 else 0

        commands = []
        with patch.object(setup.subprocess, "run", side_effect=lambda command, **_: commands.append(command)):
            self.assertEqual(0, setup.keep_connected(Gateway(), ["adb"], pause=0))
        self.assertEqual([["adb", "reverse", "tcp:5390", "tcp:5390"]] * 2, commands)

    def test_older_own_bridge_may_be_updated(self):
        path = self.root / setup.PACKAGE / "bridge/HomeControlBridge.kt"
        path.parent.mkdir()
        path.write_text("object HomeControlBridge { }")
        self.assertEqual("bridge", setup.plan_changes(self.root, "bridge")[setup.PACKAGE / "bridge/HomeControlBridge.kt"])

    def test_changes_are_backed_up_and_idempotent(self):
        planned = setup.plan_changes(self.root, "bridge")
        backup = setup.apply_changes(self.root, planned, Path(self.temp.name) / "backups")
        self.assertEqual(planned, setup.plan_changes(self.root, "bridge"))
        manifest = json.loads((backup / "dateien.json").read_text())
        for path, original in self.files.items():
            if path in planned:
                self.assertTrue(manifest[str(path)])
                self.assertEqual(original, (backup / path).read_text())
        self.assertFalse(manifest[str(setup.PACKAGE / "bridge/HomeControlBridge.kt")])
        self.assertEqual(0o700, backup.stat().st_mode & 0o777)
        gradle = planned[Path("app/build.gradle.kts")]
        self.assertIn('"\\\"$bridgeKey\\\""', gradle)
        self.assertEqual(1, gradle.count(setup.TAG))

    def test_custom_network_security_is_not_overwritten(self):
        path = self.root / "app/src/main/AndroidManifest.xml"
        path.write_text(f'<manifest xmlns:android="{setup.ANDROID}"><application android:networkSecurityConfig="@xml/custom"/></manifest>')
        with self.assertRaisesRegex(ValueError, "Netzwerk-Konfiguration"):
            setup.plan_changes(self.root, "bridge")

    def test_new_key_is_private_and_existing_key_is_not_replaced(self):
        path = Path(self.temp.name) / "config/sensor-bridge.key"
        original_mask = os.umask(0o077)
        try:
            first = setup.read_key(path)
            self.assertEqual(first, setup.read_key(path))
            self.assertEqual(64, len(first))
            self.assertEqual(0o600, path.stat().st_mode & 0o777)
            path.write_text("invalid")
            with self.assertRaises(ValueError):
                setup.read_key(path)
            self.assertEqual("invalid", path.read_text())
        finally:
            os.umask(original_mask)

    def test_probe_never_sends_a_valid_measurement(self):
        seen = []

        def receive(request, **kwargs):
            seen.append(request)
            raise setup.HTTPError(request.full_url, 400, "invalid readings", {}, None)

        with patch.object(setup, "urlopen", side_effect=receive):
            self.assertEqual(400, setup.probe("http://127.0.0.1:5181", "test-key"))
        body = json.loads(seen[0].data)
        self.assertIsNone(body["temperatureCelsius"])
        self.assertIsNone(body["humidityPercent"])
        self.assertEqual("test-key", seen[0].get_header("X-sensor-key"))


if __name__ == "__main__":
    unittest.main()
