"""Erzeugt docs/Lernunterlage-Design-System.pdf (Aufruf aus dem Repo-Hauptordner:
PYTHONPATH=tools/lernpdf python3 tools/lernpdf/lernunterlage_design_system.py)."""
from lernpdf import Doc

M = "<font name='Mono'>"
E = "</font>"

d = Doc(
    "Design-System",
    "Lernunterlage: CSS-Variablen, Design-Tokens, responsives Layout und Barrierefreiheit am Beispiel „Nachtlicht“",
    footer="Home Control · Design-System · Lernunterlage",
)

# ------------------------------------------------------------------ 1
d.h1("1  Worum es geht")
d.p("Home Control soll bei der Präsentation wie ein fertiges Smart-Home-Produkt wirken. Vorgehen: erst ein "
    "<b>Konzept</b> mit drei Richtungen als statische Vorschau (Ordner design/), dann die Wahl (<b>Nachtlicht</b>), "
    "dann die Umsetzung Seite für Seite, nach jeder Seite " + M + "./pruefen.sh" + E + " und Screenshots am Desktop und "
    "bei 390 px Breite.")
d.p("Ein <b>Design-System</b> ist die Sammlung aller Regeln, aus denen die Oberfläche gebaut wird: Farben, Schriften, "
    "Abstände, Bausteine. Wer es kennt, kann neue Seiten bauen, die zum Rest passen.")

# ------------------------------------------------------------------ 2
d.h1("2  CSS-Variablen und Design-Tokens")
d.p("Ein <b>Design-Token</b> ist eine benannte Entscheidung, z. B. „die Akzentfarbe ist Eisblau“. In CSS steht er als "
    "<b>Variable</b> (Custom Property) einmal oben, überall sonst wird nur der Name benutzt:")
d.code(""":root {
  --accent: #7cc4ff;          /* Eisblau: Auswahl, Fokus, Links */
  --warm: #ffbe5c;            /* Bernstein: Licht an, Energie */
  --glass: rgba(22, 32, 54, 0.62);
  --radius-large: 28px;
}

.link-button { color: var(--accent); }
.room-device-value.is-on { color: var(--warm); }""")
d.bullets([
    "<b>Eine Stelle ändern, alles ändert sich:</b> Die neue Palette kam fast nur über die Tokens in die App.",
    "<b>Namen nach Bedeutung</b> (" + M + "--warm" + E + ", " + M + "--online" + E + "), nicht nach Farbe "
    "(" + M + "--orange" + E + "): So bleibt der Code richtig, auch wenn sich die Farbe ändert.",
    "<b>Brücke für alte Namen:</b> " + M + "--card-gradient: var(--glass);" + E + " Seiten, die noch die alten Namen "
    "benutzen, bekommen automatisch das neue Aussehen. So ließ sich Seite für Seite umstellen, ohne etwas zu brechen.",
    "<b>Variablen sind vererbbar:</b> " + M + ".status-bar-warning { --state: var(--warning); }" + E + " setzt die Farbe "
    "der Kante nur für diese Leiste.",
])

d.h2("Die Palette „Nachtlicht“")
d.table([
    ["Token", "Wert", "Bedeutung", "Kontrast auf Glas"],
    ["--text", "#eef3ff", "Haupttext", "14,8 : 1"],
    ["--text-muted", "#9aa8c4", "Nebentext", "6,9 : 1"],
    ["--accent", "#7cc4ff", "Auswahl, Fokus, Links", "8,8 : 1"],
    ["--warm", "#ffbe5c", "Licht an, Energie", "10,0 : 1"],
    ["--online / --danger", "#3ddc97 / #ff6b6b", "LED grün / rot", "–"],
    ["--offline / --warning", "#7d8aa5 / #ffcc4d", "LED grau / gelb", "4,7 : 1 (grau)"],
], [0.25, 0.25, 0.28, 0.22])

# ------------------------------------------------------------------ 3
d.h1("3  Schrift: lokal statt Google")
d.code("""@font-face {
  font-family: 'Manrope';
  font-weight: 200 800;          /* variable Schrift: alle Stärken in EINER Datei */
  font-display: swap;            /* erst Ersatzschrift zeigen, dann tauschen */
  src: url('/fonts/manrope-latin-wght-normal.woff2') format('woff2');
}""")
d.bullets([
    "<b>Datenschutz:</b> Würde die Schrift von Google geladen, ginge bei jedem Öffnen die IP-Adresse an Google. "
    "Lokal ausgeliefert passiert das nicht, und die App funktioniert auch ohne Internet.",
    "<b>Lizenz:</b> SIL Open Font License, Text liegt daneben (" + M + "OFL-Manrope.txt" + E + ").",
    "<b>Zahlen:</b> " + M + "font-variant-numeric: tabular-nums" + E + " gibt allen Ziffern die gleiche Breite. Werte "
    "untereinander springen nicht, ohne eine Monospace-Schrift zu brauchen.",
])

# ------------------------------------------------------------------ 4
d.h1("4  Bausteine")
d.table([
    ["Baustein", "Idee"],
    ["Glas-Karte", "halbtransparent, Unschärfe dahinter (backdrop-filter), feine Kante, Radius 28 px"],
    ["Status-Leiste", "ein ruhiger Satz („1 Gerät ist offline“) mit farbiger Kante, darunter Chips"],
    ["Raumkarte", "Kopf mit Symbol; je Gerät eine Zeile: LED · Name und Typ · Wert rechts"],
    ["Gerätekachel", "Zustand groß („Aus“, „An“ in Bernstein), Messwerte klein darunter"],
    ["Energie jetzt", "Summe der aktuellen Watt; leer mit ehrlichem Grund („kein Gerät mit Strommessung online“)"],
    ["Kamera", "Bild 16:9 mit Etikett im Bild, Aufnahmezeit groß darunter"],
], [0.25, 0.75])
d.p("Gleiche Bausteine auf allen Seiten: Der Kopf der Raumkarte (Symbol, Name, Untertitel) steht genauso auf der "
    "Räume- und der Geräteseite (" + M + ".tile-head" + E + "). Das wirkt ruhig und ist weniger Code.")

# ------------------------------------------------------------------ 5
d.h1("5  Responsives Layout")
d.p("Responsiv heißt: Eine Oberfläche passt sich der Bildschirmbreite an. Gebaut wird zuerst für das Handy, "
    "größere Bildschirme bekommen mit " + M + "@media (min-width: …)" + E + " mehr Spalten.")
d.code(""".dashboard-columns { display: grid; gap: 24px; }            /* Handy: eine Spalte */
@media (min-width: 1100px) {
  .dashboard-columns { grid-template-columns: minmax(0, 2fr) minmax(300px, 1fr); }
}
.room-grid { grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); }""")
d.table([
    ["Breite", "Was passiert"],
    ["bis 600 px", "Status-Leiste kompakt (eine Zeile), Chips wischbar, Filter Raum/Typ nebeneinander"],
    ["bis 767 px", "Navigation unten fixiert, alles in einer Spalte"],
    ["ab 768 px", "Navigation als Seitenleiste links"],
    ["ab 1100 px", "Übersicht zweispaltig: Räume links, „Auf einen Blick“ und Kameras rechts"],
], [0.25, 0.75])
d.bullets([
    "<b>auto-fill + minmax:</b> So viele Spalten, wie mit mindestens 260 px hineinpassen, ganz ohne Media Query.",
    "<b>minmax(0, 1fr)</b> erlaubt einer Spalte zu schrumpfen; sonst drückt ein langer Name das Raster auseinander.",
    "<b>Prüfkriterium Handy:</b> Bei 390 × 844 px ist die erste Raumkarte ohne Scrollen sichtbar.",
    "<b>Lange Wörter:</b> " + M + "hyphens: auto" + E + " mit " + M + "lang=\"de\"" + E + " trennt „Heiz-decke“, statt "
    "„Heizdec…“ abzuschneiden.",
])

# ------------------------------------------------------------------ 6
d.h1("6  Barrierefreiheit")
d.table([
    ["Regel", "Umsetzung"],
    ["Kontrast (WCAG 2.1 AA)", "Text mind. 4,5 : 1; hier mind. 6,9 : 1, gerechnet gegen die Kartenfläche"],
    ["Farbe nie allein", "Jede LED hat Text, sichtbar oder als aria-label"],
    ["Fokus sichtbar", ":focus-visible mit 3-px-Rahmen; Eingabefelder mit Ring statt outline: none"],
    ["Bewegung", "Animationen nur, wenn prefers-reduced-motion nicht auf reduce steht"],
    ["Tastatur", "Wischbare Chip-Reihe fokussierbar (tabIndex), <details> statt eigenem Aufklapp-Code"],
    ["Symbol-Knöpfe", "Bearbeiten/Löschen nur mit Symbol, aber mit aria-label und title"],
    ["Touch", "Alles, was man antippt, mindestens 44–52 px hoch"],
], [0.28, 0.72])
d.code("""@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation: none !important; transition: none !important; }
}""")
d.merksatz("Barrierefreiheit hilft allen: Großer Kontrast liest sich auch in der Sonne, große Touch-Ziele treffen "
           "auch mit kalten Fingern, und eine LED mit Text versteht auch, wer die Farben nicht unterscheiden kann.")

# ------------------------------------------------------------------ 7
d.h1("7  Vorgehen und Werkzeuge")
d.bullets([
    "<b>Konzept zuerst:</b> drei Richtungen als statische HTML-Seiten mit Beispieldaten, ohne die App umzubauen.",
    "<b>Screenshots automatisch</b> mit Playwright (Desktop 1440 × 900, Handy 390 × 844), mit einer Kopie der "
    "Datenbank, damit die echte nicht verändert wird. Echte Kamerabilder wurden dabei nicht angesehen.",
    "<b>Seite für Seite</b> mit eigenem Commit und " + M + "./pruefen.sh" + E + " davor.",
])

d.h1("8  Kurzantworten für Rückfragen")
d.table([
    ["Frage", "Antwort"],
    ["Was ist ein Design-Token?", "Eine benannte Gestaltungsentscheidung, in CSS als Variable, z. B. --accent."],
    ["Warum keine UI-Bibliothek?", "Eigenes CSS reicht, ist kleiner und ganz erklärbar; keine fremde Abhängigkeit."],
    ["Warum Schrift lokal?", "Datenschutz (kein Aufruf bei Google) und funktioniert offline."],
    ["Was heißt responsiv?", "Das Layout passt sich der Breite an: Handy eine Spalte, Desktop mehrere."],
    ["Wie wird Kontrast geprüft?", "Verhältnis der Helligkeit von Text und Fläche, Ziel mindestens 4,5 : 1."],
    ["Warum tabular-nums?", "Gleich breite Ziffern, Zahlen springen nicht, ohne Monospace-Schrift."],
], [0.36, 0.64])

d.h1("9  Merksätze")
d.merksatz("• Farben, Radien, Abstände als Tokens oben in der CSS-Datei, sonst nur Namen benutzen.<br/>"
           "• Tokens nach Bedeutung benennen, nicht nach Farbe.<br/>"
           "• Erst Handy, dann mit min-width größere Bildschirme.<br/>"
           "• Farbe nie als einzige Information, Fokus immer sichtbar, Bewegung abschaltbar.<br/>"
           "• Schriften lokal ausliefern.")

d.save("docs/Lernunterlage-Design-System.pdf")
