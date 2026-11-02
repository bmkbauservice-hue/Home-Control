# Design-System „Nachtlicht“

**Zum Lernen:** [Lernunterlage-Design-System.pdf](Lernunterlage-Design-System.pdf)

Home Control soll wie ein fertiges Smart-Home-Produkt wirken, nicht wie ein Admin-Dashboard. Gewählt wurde am
02.11.2026 die Richtung **Nachtlicht** (ruhiges Zuhause bei Nacht) mit dem ruhigen Status-Satz aus der Richtung
„Wohnraum“. Die drei Vorschauen des Konzepts liegen in [design/](../design/).

Technik: React und eigenes CSS in `frontend/src/index.css`, **keine UI-Bibliothek**. Alles hängt an
**Design-Tokens** (CSS-Variablen) oben in der Datei.

## Design-Tokens

| Gruppe | Token | Wert | Wofür |
|---|---|---|---|
| Fläche | `--bg` | `#070b14` | Hintergrund, dazu zwei weiche Lichtflecken (blau, bernstein) |
| | `--glass` | `rgba(22, 32, 54, 0.62)` | Glas-Karte, mit `backdrop-filter: blur(18px)` |
| | `--glass-border` | `rgba(148, 178, 255, 0.16)` | feine helle Kante |
| Text | `--text` | `#eef3ff` | Haupttext |
| | `--text-muted` | `#9aa8c4` | Nebentext (Kontrast auf Glas 6,9 : 1) |
| Akzent | `--accent` | `#7cc4ff` | Eisblau: Auswahl, Fokus, Links |
| | `--warm` | `#ffbe5c` | Bernstein: Licht an, Energie |
| LED | `--online` / `--danger` / `--offline` / `--warning` | grün / rot / grau / gelb | Bedeutung wie bisher, immer mit Text |
| Form | `--radius-large` / `--radius` / `--radius-small` | 28 / 22 / 14 px | Karten, Kacheln, Knöpfe |
| Maß | `--touch` | 52 px | Mindesthöhe für alles, was man antippt |

Die alten Namen (`--card-gradient`, `--card-border`, `--surface` …) bleiben als Brücke bestehen und zeigen auf
die neuen Werte.

**Schrift:** Manrope (variable Schrift, alle Stärken in einer Datei), **lokal** ausgeliefert aus
`frontend/public/fonts` (Lizenz SIL OFL, `OFL-Manrope.txt`). Kein Aufruf bei Google (Datenschutz), geht auch ohne
Internet. **Zahlen** mit `font-variant-numeric: tabular-nums` (feste Ziffernbreite, keine Monospace-Schrift).

## Bausteine

| Baustein | Klassen | Merkmal |
|---|---|---|
| Glas-Karte | `.glass`, `.tile`, `.room-card`, `.energy-card`, `.camera-card`, `.status-bar` | halbtransparent, Unschärfe dahinter, Radius 28 px |
| Status-Leiste | `.status-bar`, `.status-chips` | ruhiger Satz mit farbiger Kante links, Chips (Server, Tuya, Blink, Zahlen) |
| Raumkarte | `.room-card`, `.room-device` | Kopf mit Symbol, Geräte als Zeilen: LED · Name/Typ · Wert rechts |
| Gerätekachel | `.tile-head`, `.device-state` | Zustand groß („Aus“, „An“ in Bernstein), Messwerte darunter |
| Energie | `.energy-card` | „Energie jetzt“ (Summe aktueller Watt) und „Batterien“ (niedrigster Akku, gelb ab 20 %) |
| Kamera | `.camera-figure`, `.camera-tag` | Bild 16:9, Etikett „Online · Akku“ im Bild, Name und Aufnahmezeit darunter |
| LED | `.led`, `.led-*` | Punkt mit Lichthof, grün pulsiert sanft |
| Knöpfe | `.button`, `.button-primary`, `.button-icon`, `.link-button` | Glas, Primär in Eisblau, Symbol-Knopf mit `aria-label` |

## Responsive Layout

- **Handy (bis 767 px):** Navigation unten fixiert, alles in einer Spalte. Status-Leiste kompakt: eine Zeile mit
  LED und Satz, Chips in einer wischbaren Reihe. Die erste Raumkarte ist ohne Scrollen sichtbar (390 × 844 px).
- **Ab 768 px:** Seitenleiste links.
- **Ab 1100 px:** Übersicht zweispaltig, Räume links, „Auf einen Blick“ und Kameras rechts.
- Lange Gerätenamen werden getrennt statt abgeschnitten (`hyphens: auto`, `lang="de"`).

## Barrierefreiheit

- Kontrast: Text mindestens 6,9 : 1 (gefordert 4,5 : 1).
- Fokus immer sichtbar (`:focus-visible`, Eingabefelder mit Ring).
- LED-Farbe nie allein: immer Text (sichtbar oder `aria-label`).
- Bewegung nur ohne `prefers-reduced-motion: reduce`.
- Wischbare Chip-Reihe ist per Tastatur fokussierbar (`tabIndex={0}`), Akkubalken als `<meter>`.
- „Neues Gerät anlegen“ als `<details>`: eingebaut, mit Tastatur und Screenreader bedienbar.

## Grenzen

- Glas-Effekt (`backdrop-filter`) kostet auf sehr alten Handys Leistung.
- Kein heller Modus: Dark Mode ist Standard und einzige Variante.
