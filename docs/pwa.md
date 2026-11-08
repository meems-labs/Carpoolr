# Carpoolr als installierbare Web-App

Die Vue-Anwendung enthält ein Web-App-Manifest, eigene PNG-/Maskable-Symbole, ein Apple-Touch-Symbol und einen Service Worker. Installierte Apps starten im eigenständigen Fenster. Die Installation erfolgt über HTTPS; `localhost` ist für lokale Tests ebenfalls ein sicherer Kontext.

## Installation auf Mobilgeräten

- **Android / unterstützte Chromium-Browser:** Sobald der Browser die Installation anbietet, erscheint **App installieren**. Alternativ das Browsermenü verwenden. Die App zeigt eine Installation erst dann als abgeschlossen an, wenn der Browser sie bestätigt oder sie bereits im eigenständigen Modus läuft.
- **iPhone / iPad:** Carpoolr in Safari öffnen, **Teilen → Zum Home-Bildschirm → Hinzufügen** wählen. Falls **Als Web-App öffnen** angezeigt wird, bleibt diese Option eingeschaltet. Die **Installationshilfe** beschreibt diesen Ablauf in der App.
- **Desktop:** Ein unterstützter Browser kann ein Installationssymbol in der Adressleiste oder einen Menüeintrag anbieten. Die Webansicht bleibt weiterhin nutzbar.

Installationsaufforderungen unterliegen den Regeln des jeweiligen Browsers und lassen sich nicht erzwingen. Apples Ablauf ist im [iPhone-Benutzerhandbuch](https://support.apple.com/guide/iphone/open-as-web-app-iphea86e5236/ios) beschrieben.

## Automatische Updates

Neue Versionen werden beim Öffnen, nach Wiederherstellung der Verbindung und bei Rückkehr zur App geprüft. Während die App sichtbar ist, erfolgt zusätzlich alle fünf Minuten eine Prüfung. **Nach Updates suchen** startet sie manuell. Während eine App geschlossen oder vom Betriebssystem angehalten ist, gibt es keine garantierte Ausführungszeit; die nächste Nutzung stößt die Prüfung wieder an.

Ein heruntergeladenes Update wartet, bis alle geöffneten Fenster bereit sind. Ungespeicherte Änderungen an Profil, Fahrzeug, Fahrtangebot, Serienplanung oder Abschlussdaten sowie laufende Vorgänge blockieren die Aktivierung. Auch ein gerade bearbeitetes Eingabefeld oder eine laufende Texteingabe per IME verhindert ein Neuladen. Die App zeigt in dieser Zeit einen Hinweis. Speichern, Zurücksetzen auf den bisherigen Wert oder Verlassen der bearbeiteten Ansicht gibt das Update frei.

Der wartende Service Worker fragt alle Fenster ab. Jedes bereite Fenster sperrt seine Eingabeflächen vor der Bestätigung. Erst wenn alle zustimmen und kein neues Fenster hinzugekommen ist, aktiviert sich der neue Worker. Die Fenster laden die neue Version automatisch. Nicht antwortende oder noch bearbeitete Fenster lassen das Update weiter warten. Zeitüberschreitungen und Fehler lösen die Sperren wieder; vor einem späteren Neuladen wird die Sicherheit erneut geprüft.

Registrierungs- und Verbindungsfehler werden angezeigt. Erneute Prüfungen nach Wiederherstellung der Verbindung oder über den manuellen Knopf können sie beheben; ein fehlgeschlagener erster Start deaktiviert Updates nicht dauerhaft.

## Offline-Grenzen und Daten

Der Cache enthält ausschließlich die App-Hülle: HTML, JavaScript, CSS, Schriftdateien, Manifest und App-Symbole. Es gibt keine API-Laufzeitcaches, keine gespeicherten gRPC-Antworten, keine Warteschlange für Änderungen und keine Hintergrundübermittlung von Zahlungen oder Buchungen.

Die bekannten Vue-Seiten können die App-Hülle offline öffnen. Für Profile, Fahrten, Anfragen und Zahlungsstände ist weiterhin eine Internetverbindung erforderlich. Die App zeigt fehlende Verbindung ausdrücklich an. API-, Authentifizierungs-, Callback- und unbekannte Pfade werden nicht durch eine zwischengespeicherte App-Seite ersetzt. OAuth-Antwortparameter sind ebenfalls ausgeschlossen.

## Build und Auslieferung

`bun run build` erzeugt das Manifest, den gebündelten klassischen Worker `dist/sw.js` und dessen versionierte Liste öffentlicher Dateien. Der Service Worker ist im Entwicklungsserver deaktiviert; zum Testen einen Produktions-Build und `vite preview` oder das Web-Container-Image verwenden.

Nginx liefert Worker ohne persistenten HTTP-Cache und mit Geltungsbereich `/` aus. HTML, Manifest und Icons werden erneut validiert. Dateien mit Inhalts-Hash unter `/assets/` dürfen langfristig gecacht werden; fehlende Dateien ergeben 404 statt HTML. Der API-Proxy setzt `Cache-Control: no-store`.

Bei einer Veröffentlichung müssen HTML, Worker, Manifest und zugehörige Dateien aus demselben Build stammen. Ein erfolgreiches CI-Ergebnis bestätigt den Build; die laufende Staging-Version muss weiterhin den neuen Build tatsächlich ausliefern. Die PWA kann keine Version laden, die der Server noch nicht bereitstellt.

Technische Grundlagen: [Vite PWA InjectManifest](https://vite-pwa-org.netlify.app/guide/inject-manifest), [Workbox-Update-Lebenszyklus](https://developer.chrome.com/docs/workbox/handling-service-worker-updates).
