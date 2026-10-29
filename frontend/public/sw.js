// Minimaler Service Worker für Home Control.
// Zweck: App ist installierbar und die Oberfläche öffnet sich auch bei kurzem Netzausfall.
// Wichtig: Aufrufe an /api werden NIE zwischengespeichert (Messwerte und Tokens müssen frisch sein).
const CACHE = 'home-control-shell-v1'

self.addEventListener('install', () => self.skipWaiting())

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (event) => {
  const request = event.request
  const url = new URL(request.url)
  if (request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api')) {
    return
  }
  // Erst das Netz, bei Fehler die zuletzt gespeicherte Kopie.
  event.respondWith(
    fetch(request)
      .then((response) => {
        if (response.ok) {
          const copy = response.clone()
          caches.open(CACHE).then((cache) => cache.put(request, copy))
        }
        return response
      })
      .catch(() => caches.match(request).then((cached) => cached ?? caches.match('/'))),
  )
})
