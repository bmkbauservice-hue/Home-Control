import { useDemoMode } from '../hooks/useDemoMode'

// Deutlich sichtbares Banner "Demo-Modus – Beispieldaten". Erscheint nur, wenn das Backend im Demo-Modus läuft,
// auf der Anmeldeseite und über jeder Seite der App. So hält niemand auf dem Beamer die Werte für echt.
export function DemoBanner() {
  if (!useDemoMode()) return null

  return (
    <p className="demo-banner" role="note">
      <strong>Demo-Modus – Beispieldaten</strong>
      <span>Kein echtes Zuhause: Geräte, Werte und Kamerabilder sind erfunden.</span>
    </p>
  )
}
