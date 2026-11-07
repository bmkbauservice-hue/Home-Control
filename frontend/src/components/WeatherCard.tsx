import { useCallback, useEffect, useState } from 'react'
import { getCurrentWeather, type CurrentWeather } from '../api/weather'
import { useAutoRefresh } from '../hooks/useAutoRefresh'

// Die Wettercodes sind WMO-Codes von Open-Meteo.
export function weatherDescription(code: number, isDay: boolean): { icon: string; label: string } {
  if (code === 0) return { icon: isDay ? '☀️' : '🌙', label: 'Klarer Himmel' }
  if (code === 1) return { icon: isDay ? '🌤️' : '🌙', label: 'Überwiegend klar' }
  if (code === 2) return { icon: '⛅', label: 'Teilweise bewölkt' }
  if (code === 3) return { icon: '☁️', label: 'Bedeckt' }
  if (code === 45 || code === 48) return { icon: '🌫️', label: 'Neblig' }
  if ([51, 53, 55, 56, 57].includes(code)) return { icon: '🌦️', label: 'Nieselregen' }
  if ([61, 63, 65, 66, 67].includes(code)) return { icon: '🌧️', label: 'Regen' }
  if ([71, 73, 75, 77, 85, 86].includes(code)) return { icon: '🌨️', label: 'Schnee' }
  if ([80, 81, 82].includes(code)) return { icon: '🌦️', label: 'Regenschauer' }
  if ([95, 96, 99].includes(code)) return { icon: '⛈️', label: 'Gewitter' }
  return { icon: '🌡️', label: 'Wetterzustand unbekannt' }
}

export function WeatherCard() {
  const [now, setNow] = useState(() => new Date())
  const [weather, setWeather] = useState<CurrentWeather | null>(null)
  const [error, setError] = useState<string | null>(null)

  const loadWeather = useCallback(async () => {
    try {
      const latest = await getCurrentWeather()
      setWeather(latest)
      setError(null)
    } catch (err) {
      setError((err as Error).message)
    }
  }, [])

  useEffect(() => { void loadWeather() }, [loadWeather])
  useAutoRefresh(() => void loadWeather(), 5 * 60_000)
  useAutoRefresh(() => setNow(new Date()), 30_000)

  const description = weather ? weatherDescription(weather.weatherCode, weather.isDay) : null
  const clock = now.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
  const day = now.toLocaleDateString('de-DE', { weekday: 'long', day: 'numeric', month: 'long' })
  const observation = weather?.observationTime.split('T')[1]?.slice(0, 5) ?? ''

  return (
    <section className="weather-card" aria-label="Aktuelle Uhrzeit und Wetter">
      <div className="weather-clock">
        <span className="weather-caption">Heute</span>
        <time className="weather-time" dateTime={now.toISOString()}>{clock}</time>
        <span className="weather-date">{day}</span>
      </div>
      <div className="weather-outside">
        <span className="weather-caption">Draußen {weather ? '· ' + weather.location : ''}</span>
        {weather && description ? (
          <>
            <div className="weather-reading">
              <span className="weather-symbol" aria-hidden="true">{description.icon}</span>
              <strong className="weather-temperature">{weather.temperatureC.toLocaleString('de-DE', { maximumFractionDigits: 1 })} °C</strong>
            </div>
            <span className="weather-condition">{description.label}</span>
            <small className="weather-source">
              {error ? 'Wetterdaten aktuell nicht erreichbar · letzter Stand' : 'Aktuelle Wetterdaten'}
              {observation ? ' · Stand ' + observation + ' Uhr' : ''} · Open-Meteo
            </small>
          </>
        ) : (
          <div className="weather-placeholder">
            {error ?? 'Wetterdaten werden geladen …'}
          </div>
        )}
        {error && (
          <button className="button" type="button" onClick={() => void loadWeather()}>
            Erneut laden
          </button>
        )}
      </div>
    </section>
  )
}
