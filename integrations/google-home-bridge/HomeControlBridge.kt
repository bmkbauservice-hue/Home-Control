package com.example.googlehomeapisampleapp.bridge

import android.util.Log
import com.example.googlehomeapisampleapp.BuildConfig
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/** Sendet ausschließlich Sensorwerte an die lokal konfigurierte Home-Control-Bridge. */
object HomeControlBridge {
    suspend fun publish(id: String, name: String, temperature: Double?, humidity: Double?) {
        val base = BuildConfig.HOME_CONTROL_SENSOR_URL.trimEnd('/')
        val key = BuildConfig.HOME_CONTROL_SENSOR_KEY
        if (base.isBlank() || key.isBlank() || (temperature == null && humidity == null)) return
        withContext(Dispatchers.IO) {
            var connection: HttpURLConnection? = null
            try {
                val payload = JSONObject()
                    .put("sensorId", id)
                    .put("name", name)
                    .put("temperatureCelsius", temperature ?: JSONObject.NULL)
                    .put("humidityPercent", humidity ?: JSONObject.NULL)
                    .toString()
                connection = (URL("$base/api/climate/bridge/readings").openConnection() as HttpURLConnection).apply {
                    requestMethod = "POST"
                    connectTimeout = 4000
                    readTimeout = 4000
                    doOutput = true
                    setRequestProperty("Content-Type", "application/json")
                    setRequestProperty("X-Sensor-Key", key)
                }
                connection.outputStream.use { it.write(payload.toByteArray(Charsets.UTF_8)) }
                val code = connection.responseCode
                if (code !in 200..299) Log.w("HomeControlBridge", "Sensoruebertragung HTTP " + code)
            } catch (ex: Exception) {
                Log.w("HomeControlBridge", "Sensoruebertragung fehlgeschlagen: " + ex.message)
            } finally {
                connection?.disconnect()
            }
        }
    }
}
