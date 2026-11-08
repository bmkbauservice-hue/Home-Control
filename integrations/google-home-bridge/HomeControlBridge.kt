package com.example.googlehomeapisampleapp.bridge

import android.util.Log
import com.example.googlehomeapisampleapp.BuildConfig
import com.google.home.ConnectivityState
import com.google.home.DeviceType
import com.google.home.matter.standard.BooleanState
import com.google.home.matter.standard.ContactSensorDevice
import com.google.home.matter.standard.HumiditySensorDevice
import com.google.home.matter.standard.OnOff
import com.google.home.matter.standard.PowerSource
import com.google.home.matter.standard.RelativeHumidityMeasurement
import com.google.home.matter.standard.TemperatureMeasurement
import com.google.home.matter.standard.TemperatureSensorDevice
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Verbindet Muju Home mit Home Control: sendet echte Klimawerte, meldet Geräte mit Ein/Aus
 * und führt nur Befehle aus, die Home Control für ausdrücklich verknüpfte Geräte stellt.
 */
object HomeControlBridge {
    private const val POLL_MS = 1500L
    private const val CONFIRM_MS = 7000L

    private class SwitchDevice(val name: String, val online: Boolean, val trait: OnOff)

    private val switches = ConcurrentHashMap<String, SwitchDevice>()
    private val inventory = ConcurrentHashMap<String, String>()

    private class ContactDevice(val name: String, val online: Boolean, val closed: Boolean)

    private val contacts = ConcurrentHashMap<String, ContactDevice>()
    private val polling = AtomicBoolean(false)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    private val configured: Boolean
        get() = BuildConfig.HOME_CONTROL_SENSOR_URL.isNotBlank() && BuildConfig.HOME_CONTROL_SENSOR_KEY.length >= 32

    suspend fun publishDevice(id: String, name: String, types: Set<DeviceType>) {
        rememberSwitch(id, name, types)
        rememberContact(id, name, types)
        logInventory(id, name, types)

        val traits = types.filter { type ->
            (type.factory == TemperatureSensorDevice || type.factory == HumiditySensorDevice) && isOnline(type)
        }.flatMap { it.traits() }
        // Beide Matter-Messwerte sind in Hundertsteln angegeben. Fehlende Werte bleiben null.
        val temperature = traits.filterIsInstance<TemperatureMeasurement>()
            .firstOrNull()?.measuredValue?.toDouble()?.div(100.0)
        val humidity = traits.filterIsInstance<RelativeHumidityMeasurement>()
            .firstOrNull()?.measuredValue?.toDouble()?.div(100.0)
        if (temperature != null && (!temperature.isFinite() || temperature !in -40.0..60.0)) return
        if (humidity != null && (!humidity.isFinite() || humidity !in 0.0..100.0)) return
        publish(id, name, temperature, humidity)
    }

    suspend fun publish(id: String, name: String, temperature: Double?, humidity: Double?) {
        if (temperature == null && humidity == null) return
        val body = JSONObject()
            .put("sensorId", id)
            .put("name", name)
            .put("temperatureCelsius", temperature ?: JSONObject.NULL)
            .put("humidityPercent", humidity ?: JSONObject.NULL)
        if (post("/api/climate/bridge/readings", body) != null) Log.i("HomeControlBridge", "Sensorwert angenommen")
    }

    private fun isOnline(type: DeviceType): Boolean {
        val state = type.metadata.sourceConnectivity.connectivityState
        return state == ConnectivityState.ONLINE || state == ConnectivityState.PARTIALLY_ONLINE
    }

    /** Matter meldet den Akku in halben Prozent. Fehlt er, bleibt der Wert null. */
    private fun batteryPercent(types: Set<DeviceType>): Int? =
        types.flatMap { it.traits() }.filterIsInstance<PowerSource>()
            .firstNotNullOfOrNull { it.batPercentRemaining }?.toInt()?.div(2)?.takeIf { it in 0..100 }

    /** Inventur fürs Handy-Protokoll, nur bei Änderungen. Von der ID nur die letzten vier Zeichen. */
    private fun logInventory(id: String, name: String, types: Set<DeviceType>) {
        val onOff = types.flatMap { it.traits() }.filterIsInstance<OnOff>().firstOrNull()
        val line = "Inventar: $name | ID ...${id.takeLast(4)} | online=${types.any(::isOnline)}" +
            " | ein=${onOff?.onOff} | kontakt=${types.flatMap { it.traits() }.filterIsInstance<BooleanState>().firstOrNull()?.stateValue}" +
            " | akku=${batteryPercent(types)}" +
            " | typen=${types.map { it.factory.toString().substringAfterLast('.') }.sorted()}" +
            " | faehigkeiten=${types.flatMap { it.traits() }.map { it.factory.toString().substringAfterLast('.') }.distinct().sorted()}"
        if (inventory.put(id, line) != line) Log.i("HomeControlBridge", line)
    }

    /** Merkt sich den neuesten Stand. Das SDK liefert bei jeder Änderung ein neues Trait-Objekt. */
    private fun rememberSwitch(id: String, name: String, types: Set<DeviceType>) {
        val type = types.firstOrNull { candidate -> candidate.traits().any { it is OnOff } }
        if (type == null) {
            switches.remove(id)
            return
        }
        switches[id] = SwitchDevice(name, isOnline(type), type.traits().filterIsInstance<OnOff>().first())
        startPolling()
    }

    /** Matter: stateValue true heißt beim Kontaktsensor "geschlossen". */
    private fun rememberContact(id: String, name: String, types: Set<DeviceType>) {
        val type = types.firstOrNull { it.factory == ContactSensorDevice }
        val closed = type?.traits()?.filterIsInstance<BooleanState>()?.firstOrNull()?.stateValue
        if (type == null || closed == null) {
            contacts.remove(id)
            return
        }
        contacts[id] = ContactDevice(name, isOnline(type), closed)
        startPolling()
    }

    private fun startPolling() {
        if (configured && polling.compareAndSet(false, true)) scope.launch { pollLoop() }
    }

    private suspend fun pollLoop() {
        while (true) {
            try {
                val devices = JSONArray()
                switches.forEach { (id, device) ->
                    devices.put(
                        JSONObject()
                            .put("id", id)
                            .put("name", device.name)
                            .put("online", device.online)
                            .put("on", device.trait.onOff ?: JSONObject.NULL),
                    )
                }
                contacts.forEach { (id, contact) ->
                    devices.put(
                        JSONObject()
                            .put("id", id)
                            .put("name", contact.name)
                            .put("online", contact.online)
                            .put("on", JSONObject.NULL)
                            .put("open", !contact.closed),
                    )
                }
                val answer = post("/api/google-home/bridge/poll", JSONObject().put("devices", devices))
                val commands = answer?.let { JSONObject(it).optJSONArray("commands") } ?: JSONArray()
                for (index in 0 until commands.length()) {
                    val command = commands.getJSONObject(index)
                    // Eigene Coroutine: Das Nachfragen läuft weiter, sonst gälte die App als getrennt.
                    scope.launch {
                        execute(command.getString("id"), command.getString("deviceId"), command.getBoolean("on"))
                    }
                }
            } catch (ex: CancellationException) {
                throw ex
            } catch (ex: Exception) {
                Log.w("HomeControlBridge", "Nachfrage fehlgeschlagen (${ex.javaClass.simpleName})")
            }
            delay(POLL_MS)
        }
    }

    /** Genau ein Schaltversuch ohne Wiederholung. Erfolg meldet erst der neu gelesene Zustand. */
    private suspend fun execute(commandId: String, deviceId: String, on: Boolean) {
        val device = switches[deviceId]
        if (device == null) return report(commandId, null, "unknown")
        if (!device.online) return report(commandId, null, "offline")
        try {
            if (on) device.trait.on() else device.trait.off()
        } catch (ex: CancellationException) {
            throw ex
        } catch (ex: Exception) {
            Log.w("HomeControlBridge", "Schaltbefehl abgelehnt (${ex.javaClass.simpleName})")
            return report(commandId, null, "rejected")
        }
        val confirmed = withTimeoutOrNull(CONFIRM_MS) {
            while (switches[deviceId]?.trait?.onOff != on) delay(250)
            on
        }
        Log.i("HomeControlBridge", "Schaltbefehl ${if (confirmed == null) "nicht bestaetigt" else "bestaetigt"}")
        report(commandId, confirmed, null)
    }

    private suspend fun report(commandId: String, on: Boolean?, error: String?) {
        val body = JSONObject().put("on", on ?: JSONObject.NULL).put("error", error ?: JSONObject.NULL)
        post("/api/google-home/bridge/commands/$commandId/result", body)
    }

    /** Liefert die Antwort bei Erfolg, sonst null. Keine Adresse, ID oder Schlüssel in Protokollen. */
    private suspend fun post(path: String, json: JSONObject): String? {
        if (!configured) return null
        return withContext(Dispatchers.IO) {
            var connection: HttpURLConnection? = null
            try {
                val body = json.toString().toByteArray(Charsets.UTF_8)
                if (body.size > 64 * 1024) return@withContext null
                val base = BuildConfig.HOME_CONTROL_SENSOR_URL.trimEnd('/')
                connection = (URL(base + path).openConnection() as HttpURLConnection).apply {
                    requestMethod = "POST"
                    connectTimeout = 4000
                    readTimeout = 4000
                    doOutput = true
                    instanceFollowRedirects = false
                    setFixedLengthStreamingMode(body.size)
                    setRequestProperty("Content-Type", "application/json")
                    setRequestProperty("X-Sensor-Key", BuildConfig.HOME_CONTROL_SENSOR_KEY)
                }
                connection.outputStream.use { it.write(body) }
                val code = connection.responseCode
                if (code in 200..299) {
                    connection.inputStream.bufferedReader().use { it.readText() }
                } else {
                    Log.w("HomeControlBridge", "Bridge-Aufruf HTTP $code")
                    null
                }
            } catch (ex: CancellationException) {
                throw ex
            } catch (ex: Exception) {
                Log.w("HomeControlBridge", "Bridge-Aufruf fehlgeschlagen (${ex.javaClass.simpleName})")
                null
            } finally {
                connection?.disconnect()
            }
        }
    }
}
