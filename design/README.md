# Design-Konzept: drei Richtungen für Home Control

Ziel: Home Control soll bei der Präsentation am **09.11.2026** wie ein fertiges Smart-Home-Produkt wirken, nicht
wie ein Admin-Dashboard. Hier liegen drei klar unterschiedliche Richtungen als **statische Vorschau der
Übersicht** (eine HTML-Datei je Richtung, Beispieldaten wie im echten Zuhause). Einfach im Browser öffnen und
die Fensterbreite ändern (Desktop bis Handy, 390 px).

| Datei | Richtung | in einem Satz |
|---|---|---|
| [richtung-1-nachtlicht.html](richtung-1-nachtlicht.html) | **Nachtlicht** | Ruhiges Zuhause bei Nacht: Glas-Karten, Mitternachtsblau, warmes Bernstein für Licht |
| [richtung-2-wohnraum.html](richtung-2-wohnraum.html) | **Wohnraum** | Warm und wohnlich wie eine Möbelmarke: Anthrazit, Terrakotta, Serifen, keine Rahmen |
| [richtung-3-leitstand.html](richtung-3-leitstand.html) | **Leitstand** | Präzise wie ein Profi-System: fast schwarz, Mint, feine Linien, Messwerte in Monospace |

Die Kamerabilder in den Vorschauen sind **gezeichnete Platzhalter**, keine echten Aufnahmen.

## Was alle drei gegenüber heute verbessern

Aus den Screenshots des aktuellen Stands (02.11.2026, Desktop und 390 px):

- **Begrüßung mit Aussage** statt großem Logo-Kasten: „Guten Abend, Muju. Ein Gerät braucht deinen Blick.“
- **Status-Leiste als ein Satz** („1 Gerät ist offline – Bar Licht meldet sich nicht“) mit den Teil-LEDs
  darunter. Die Kacheln „Räume 6 / Geräte 10“ entfallen, sie wiederholten nur, was darunter steht.
- **Raumkarten mit Geräten als Zeilen** (LED · Name/Typ · Wert rechts) statt kleiner Kästchen. Auf dem Handy
  wird nichts mehr abgeschnitten („Tempera…“, „Küche Li…“), lange Namen werden getrennt („Heiz-decke“).
- **Messwerte rechtsbündig mit festen Ziffernbreiten** (`tabular-nums`), Haupt-Wert groß, Zusatz klein
  darunter (z. B. „Aus · 0,0 W · 229,8 V“). „An“ in der Lichtfarbe.
- **Kameras mit Bild, Online/Akku als Etikett im Bild und „Aufnahme von …“** darunter.
- Unten fixierte Navigation auf dem Handy, Seitenleiste ab 641 px.

## Die drei Richtungen im Vergleich

| | 1 · Nachtlicht | 2 · Wohnraum | 3 · Leitstand |
|---|---|---|---|
| **Stimmung** | ruhig, hochwertig, „Wohnung bei Nacht“ | warm, wohnlich, persönlich | technisch, souverän, präzise |
| **Schriften** | Manrope (eine Familie, 400–800) | Fraunces (Serife, Überschriften und Werte) + Inter (Text) | Space Grotesk (Text) + JetBrains Mono (Werte, Etiketten) |
| **Karten** | Glas: halbtransparent, Unschärfe dahinter, feine helle Kante, Radius 28 px | flächig, ohne Rahmen, weicher Schatten, Radius 26 px, Trennlinien zwischen Geräten | 1-px-Linien, Radius 10 px, Geräte als eigene Kästchen, feines Hintergrundraster |
| **Status-Leiste** | leuchtender Kreis + Pillen mit LEDs | farbige Kante links, ruhiger Satz in Serife, LEDs als Liste | Systemleiste mit Lauflicht, eckige LEDs, Etiketten in Großbuchstaben |
| **LEDs** | runde Punkte mit Lichthof, grün „atmet“ | runde Punkte ohne Glanz, „unbekannt“ als Ring | eckige Quadrate, „Achtung“ blinkt langsam |
| **Messwerte** | fett, Bernstein für Licht an | Serife groß, sehr ruhig | Monospace, wie ein Messgerät |
| **Animationen** | Karten steigen beim Laden auf, LED-Atmen | nur sanftes Einblenden | Karten gleiten ein, Lauflicht in der Status-Leiste, LED-Blinken |
| **Stärke** | sofort „Produkt“-Eindruck, passt zur heutigen dunklen Linie | hebt sich am stärksten von Dashboards ab, wohnlich | passt zur Technik-Präsentation (Messwerte, Status) |
| **Risiko** | Glas-Effekt kostet auf alten Handys Leistung | Serifen-Werte sind etwas schwerer zu überfliegen | kann „zu technisch“ wirken, Großbuchstaben brauchen Platz |

## Farbpaletten als CSS-Variablen

Die LED-Bedeutung bleibt in allen drei Richtungen gleich: **grün = online, rot = offline, grau = unbekannt,
gelb = Achtung**, immer zusammen mit Text (Farbsehschwäche).

```css
/* 1 · Nachtlicht */
:root {
  --bg: #070b14;  --glass: rgba(22, 32, 54, 0.62);  --glass-border: rgba(148, 178, 255, 0.16);
  --text: #eef3ff;  --text-muted: #9aa8c4;  --accent: #7cc4ff;  --warm: #ffbe5c;
  --led-green: #3ddc97;  --led-red: #ff6b6b;  --led-grey: #7d8aa5;  --led-yellow: #ffcc4d;
}

/* 2 · Wohnraum */
:root {
  --bg: #14110f;  --surface: #1e1a17;  --surface-2: #28221e;  --line: rgba(255, 236, 214, 0.08);
  --text: #f5eee6;  --text-muted: #b9ab9c;  --accent: #e8956a;  --lit: #f2c26b;
  --led-green: #6fd39b;  --led-red: #f07a6e;  --led-grey: #8f8579;  --led-yellow: #f3c65b;
}

/* 3 · Leitstand */
:root {
  --bg: #0a0d0c;  --surface: #101513;  --surface-2: #151c19;  --line: #22302a;
  --text: #e6f0eb;  --text-muted: #91a59c;  --accent: #7ef0c0;  --lit: #ffd166;
  --led-green: #39e58c;  --led-red: #ff5d5d;  --led-grey: #6f7f78;  --led-yellow: #ffcf3f;
}
```

## Barrierefreiheit (in allen drei Vorschauen umgesetzt)

- **Kontrast** (WCAG 2.1, gegen die Kartenfläche gerechnet): Text 14,8 bis 15,8 : 1, gedämpfter Text
  6,9 bis 7,7 : 1, Akzentfarben 7,4 bis 13,3 : 1. Gefordert sind 4,5 : 1. Graue LED 4,4 bis 4,8 : 1
  (gefordert für Bedienelemente 3 : 1).
- **Fokus** sichtbar: 3-px-Rahmen in der Akzentfarbe (`:focus-visible`).
- **Bewegung**: Bei `prefers-reduced-motion: reduce` sind alle Animationen aus.
- **LEDs** haben immer Text (sichtbar oder als `aria-label`), die Farbe allein trägt keine Information.
- **Touch-Ziele** mindestens 48 px hoch.

## Rahmen für die Umsetzung

- React und eigenes CSS bleiben, **keine neue UI-Bibliothek**. Die Richtung kommt über CSS-Variablen
  (Design-Tokens) in `frontend/src/index.css`, die Komponenten behalten ihre Logik.
- **Schriften lokal ausliefern**: Die Vorschauen laden sie aus Google Fonts. Im Produkt kommen die
  `woff2`-Dateien (freie Lizenz, SIL OFL) in den Ordner `frontend/public/fonts`. So geht beim Öffnen der App
  kein Aufruf an Google (Datenschutz) und die Schrift ist auch ohne Internet da.
- Dark Mode bleibt Standard.
- Umsetzung Seite für Seite, Übersicht zuerst, nach jeder Seite `./pruefen.sh` und Screenshots.
