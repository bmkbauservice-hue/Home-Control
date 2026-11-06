import { send } from './http'

// Die Uhr ist lokal; Temperaturen und Wetterzustand kommen vom Backend.
export type CurrentWeather = {
  location: string
  temperatureC: number
  weatherCode: number
  isDay: boolean
  observationTime: string
  retrievedAtUtc: string
  source: string
}

export async function getCurrentWeather(): Promise<CurrentWeather> {
  const response = await send('/api/weather/current')
  return (await response.json()) as CurrentWeather
}
