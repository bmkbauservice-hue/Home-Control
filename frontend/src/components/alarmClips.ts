// Ab wann ein Clip zeitlich zu einem Türereignis passt: kurz davor bis drei Minuten danach.
// Das sagt nur etwas über die Zeit aus, nicht darüber, dass die Tür den Clip ausgelöst hat.
const beforeEventMs = 30_000
const afterEventMs = 180_000

export function isTimeMatch(eventAtUtc: string, clipAtUtc: string): boolean {
  const difference = Date.parse(clipAtUtc) - Date.parse(eventAtUtc)
  return difference >= -beforeEventMs && difference <= afterEventMs
}

export function matchingClips<T extends { createdAtUtc: string }>(eventAtUtc: string, clips: T[]): T[] {
  return clips.filter((clip) => isTimeMatch(eventAtUtc, clip.createdAtUtc))
}

export function hasMatchingEvent(clipAtUtc: string, events: { startedAtUtc: string }[]): boolean {
  return events.some((event) => isTimeMatch(event.startedAtUtc, clipAtUtc))
}
