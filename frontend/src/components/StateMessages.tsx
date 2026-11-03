// Einheitliche Anzeigen für "lädt gerade" und "Laden ist fehlgeschlagen", damit jede Seite gleich aussieht.
// Leere Listen zeigen die Seiten selbst mit <p className="empty">, weil der Text dort je Seite anders ist.

// Ladehinweis im gestrichelten Kasten. role="status": Ein Screenreader liest ihn vor, ohne zu stören.
export function LoadingMessage({ text }: { text: string }) {
  return (
    <p className="empty" role="status">
      {text}
    </p>
  )
}

type ErrorMessageProps = {
  message: string
  // Optional: Knopf "Erneut versuchen", der die Daten noch einmal lädt.
  onRetry?: () => void
}

// Rote Fehlermeldung. role="alert": Ein Screenreader liest sie sofort vor.
export function ErrorMessage({ message, onRetry }: ErrorMessageProps) {
  return (
    <div className="alert alert-row" role="alert">
      <span>{message}</span>
      {onRetry && (
        <button type="button" className="button" onClick={onRetry}>
          Erneut versuchen
        </button>
      )}
    </div>
  )
}
