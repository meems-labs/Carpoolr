# Carpoolr: Oberfläche und Navigation

Carpoolr ist zuerst für die Nutzung am Telefon gestaltet. Warme helle oder dunkle Flächen, Grün und sparsame helle Akzente bilden die gemeinsame Sprache. Die Oberfläche verwendet funktionale Überschriften und Hilfetexte; dekorative Motto- und Werbetexte entfallen. Die variable Schrift Manrope wird lokal ausgeliefert; es werden keine externen Font-Dienste angefragt. Die Lizenz liegt unter `apps/web/public/licenses/manrope.txt`.

## Farbschema

Die Auswahl **System**, **Hell** oder **Dunkel** steht im Seitenfuß und ist auch ohne Anmeldung erreichbar. Standardmäßig gilt die Gerätepräferenz. Eine explizite Auswahl bleibt nur auf diesem Gerät unter `carpoolr.theme` gespeichert; Änderungen in anderen Fenstern werden übernommen. Ohne verfügbaren Browser-Speicher funktioniert der Wechsel für die laufende Nutzung weiterhin.

`theme-init.js` setzt das Farbschema vor dem ersten Rendern; das Skript gehört zur statischen PWA-Hülle. `src/state/theme.ts` reagiert auf Änderungen der Gerätepräferenz und setzt den Theme-Color-Wert der Browser-/PWA-Leiste. Alle Komponenten verwenden semantische Tokens aus `src/styles.css`, einschließlich Formularen, Statusmeldungen, Navigation und Aufgabenpanel. Text auf hellen Akzentflächen verwendet `--on-accent`; Text auf der Hauptfarbe verwendet `--on-brand`. Native Datumsfelder und Auswahlmenüs erhalten das passende `color-scheme`.

## Gemeinsame Bausteine

Farben, Abstände, Rundungen und grundlegende Typografie stehen in `src/styles.css`. Bestehende Seiten verwenden dieselben Formularfelder, Buttons, Meldungen und Abstände. `AppIcon` liefert einheitliche SVG-Symbole. `TripCard` zeigt Datum und Uhrzeit, Strecke, Fahrer, freie Plätze und eine ausdrücklich als Vorschau beschriftete Kostenaufteilung.

Aktionen erhalten klare Textbeschriftungen. Hauptaktionen sind grün, weniger wichtige Aktionen zurückhaltend und destruktive Aktionen zusätzlich textlich erkennbar. Fehler- und Erfolgszustände unterscheiden sich auch durch ihren Inhalt, nicht allein durch Farbe. Fokusindikatoren bleiben sichtbar; reduzierte Bewegungspräferenzen werden berücksichtigt.

## Navigation

Auf kleinen Bildschirmen sind fünf beschriftete Ziele dauerhaft unten erreichbar: **Start**, **Suchen**, **Meine Fahrten**, **Kosten** und **Profil**. Ab 1024 CSS-Pixeln übernimmt eine linke Seitenleiste diese Aufgaben. Die mobilen Bereiche berücksichtigen die Geräte-Safe-Area und reservieren unten Platz für die Navigation.

**Meine Fahrten** enthält die verwandten Bereiche **Angebote**, **Mitfahrten** und **Anfragen**. Sie verlinken auf die bestehenden Seiten; sie verändern keine Buchungsregeln. Die Fahrtsuche liegt unter `/fahrten`, die persönliche Startseite unter `/`. Profil, Kosten und Detail-URLs bleiben erhalten.

Die Struktur orientiert sich an den [Empfehlungen für adaptive Navigation und verwandte Ziele](https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-and-nav-patterns). Wichtige Touch-Flächen sind mindestens 44 CSS-Pixel groß; siehe [W3C Target Size (Enhanced)](https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced.html).

## Startseite und Daten

Ohne ausgewählte Testperson zeigt die Startseite eine Einführung. Es werden keine persönlichen Beispielwerte oder erfundenen Kennzahlen eingesetzt. Die Personenauswahl bleibt direkt erreichbar.

Nach der Auswahl werden nächste Fahrt, offene Bestätigungen und festgeschriebene Kosten unabhängig geladen. Jeder Bereich hat einen eigenen Lade-, Fehler- und Leerzustand. Ein Fehler in einem Bereich verdeckt die erfolgreich geladenen anderen Bereiche nicht. Fehlgeschlagene Anfragen werden nicht als null offene Aufgaben oder null Euro dargestellt.

Die nächste Fahrt kommt aus `Dashboard.GetNextTrip`: früheste zukünftige geplante eigene Fahrt oder bestätigte Mitfahrt, mit stabiler ID-Sortierung bei gleicher Uhrzeit. Unbestätigte, abgelehnte, zurückgezogene, vergangene und abgesagte Mitfahrten zählen nicht. Die Auswahl ist unabhängig von der Pagination der Anfragehistorie. Geldsummen kommen unverändert als Centwerte aus der bestehenden Kostenübersicht.

## Mobile Details und PWA

Zahlungsübersichten werden auf kleinen Bildschirmen als beschriftete Zeilen untereinander dargestellt; Aktionen bleiben direkt erreichbar. Die Glocke begrenzt ihre Höhe, damit ihre Einträge nicht hinter der unteren Navigation verschwinden. Lange Namen, Adressen und vergrößerte Texte dürfen keinen horizontalen Seitenüberlauf erzeugen.

Installation und manuelle Update-Prüfung stehen im **Profil** unter **Carpoolr als App**. Verbindungs- und Update-Hinweise bleiben global sichtbar. Die gemeinsame Installationsregistrierung bleibt aktiv, wenn der Profilbereich verlassen wird. Header, Hauptinhalt und beide Navigationsvarianten nehmen am bestehenden sicheren PWA-Update-Verfahren teil. Die neue Suchroute gehört zur erlaubten statischen Offline-App-Hülle; API- und persönliche Daten bleiben ausgeschlossen.
