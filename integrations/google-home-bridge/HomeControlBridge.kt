package com.example.googlehomeapisampleapp.bridge

import android.util.Log
import com.example.googlehomeapisampleapp.BuildConfig
import com.google.home.ConnectivityState
import com.google.home.DeviceType
import com.google.home.matter.standard.HumiditySensorDevice
import com.google.home.matter.standard.RelativeHumidityMeasurement
import com.google.home.matter.standard.TemperatureMeasurement
import com.google.home.matter.standard.TemperatureSensorDevice
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/** Sendet nur tatsächlich vom Home SDK gelieferte Klimawerte. */
object HomeControlBridge {
    suspend fun publishDevice(id: String, name: String, types: Set<DeviceType>) {
        val traits = types.filter { type ->
            (type.factory == TemperatureSensorDevice || type.factory == HumiditySensorDevice) &&
                (type.metadata.sourceConnectivity.connectivityState == ConnectivityState.ONLINE ||
                    type.metadata.sourceConnectivity.connectivityState == ConnectivityState.PARTIALLY_ONLINE)
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
        val base = BuildConfig.HOME_CONTROL_SENSOR_URL.trimEnd('/')
        val key = BuildConfig.HOME_CONTROL_SENSOR_KEY
        if (base.isBlank() || key.length < 32 || (temperature == null && humidity == null)) return
        withContext(Dispatchers.IO) {
            var connection: HttpURLConnection? = null
            try {
                val body = JSONObject()
                    .put("sensorId", id)
                    .put("name", name)
                    .put("temperatureCelsius", temperature ?: JSONObject.NULL)
                    .put("humidityPercent", humidity ?: JSONObject.NULL)
                    .toString().toByteArray(Charsets.UTF_8)
                if (body.size > 4096) return@withContext
                connection = (URL("$base/api/climate/bridge/readings").openConnection() as HttpURLConnection).apply {
                    requestMethod = "POST"
                    connectTimeout = 4000
                    readTimeout = 4000
                    doOutput = true
                    instanceFollowRedirects = false
                    setFixedLengthStreamingMode(body.size)
                    setRequestProperty("Content-Type", "application/json")
                    setRequestProperty("X-Sensor-Key", key)
                }
                connection.outputStream.use { it.write(body) }
                val code = connection.responseCode
                if (code in 200..299) Log.i("HomeControlBridge", "Sensorwert angenommen")
                else Log.w("HomeControlBridge", "Sensoruebertragung HTTP $code")
            } catch (ex: CancellationException) {
                throw ex
            } catch (ex: Exception) {
                // Keine Adresse, Sensor-ID oder Schlüssel in Android-Protokollen.
                Log.w("HomeControlBridge", "Sensoruebertragung fehlgeschlagen (${ex.javaClass.simpleName})")
            } finally {
                connection?.disconnect()
            }
        }
    }
}
