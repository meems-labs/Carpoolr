# Carpoolr: bestaetigter Umfang und offene Entscheidungen

Diese Datei haelt Entscheidungen fuer die weitere Umsetzung fest. Vorhandener Beispielcode ist keine Bestaetigung einer fachlichen Anforderung. Offene Entscheidungen werden nicht durch Standardannahmen ersetzt.

## Bestaetigt

| Thema | Entscheidung | Grundlage |
| --- | --- | --- |
| Ergebnis | Vollstaendige Anwendung zum Testen im Staging | Benutzerantwort: "Ich brauche eine im staging testbare anwendung, wo ich einmal alle features testen kann ohne authentication." |
| Anmeldung im Staging | Ohne Authentication testbar | Dieselbe Benutzerantwort |
| Testpersonen | Umschalter zwischen vordefinierten Testpersonen | Bestaetigte Auswahl |
| Fachliches Modell | Kollegen bieten Fahrten im eigenen Auto an; andere Kollegen fragen an mitzufahren | Benutzerbeschreibung |
| Profile und Fahrzeuge | Wohnort, Auto, Kosten fuer Laden/Tanken und Verbrauch eintragen | Benutzerbeschreibung |
| Wiederholungen | Wiederkehrende Fahrten gehoeren zum Umfang | Bestaetigte Funktionsauswahl |
| Zwischenstopps und Routing | Wohnorte und Zielort fuer eine optimierte Route mit Google-Maps-Ausgabe nutzen | Benutzerbeschreibung und Funktionsauswahl |
| Kostenbeteiligung | Kosten je Person anzeigen | Benutzerbeschreibung und Funktionsauswahl |
| Zahlungsstatus | Zahlende Person meldet eine Zahlung; der Empfaenger bestaetigt sie, erst danach gilt sie als bezahlt | Benutzerbeschreibung |
| Anfragen | Fahrer bestaetigt oder lehnt ab; erst bestaetigte Personen belegen einen Platz | Bestaetigte Auswahl |
| Ruecknahme und erneute Anfrage | Bis Abfahrt zurueckziehen; nach Ablehnung/Ruecknahme erneut anfragen | Bestaetigte Auswahl |
| Fahrzeuganzahl | Ein Fahrzeug pro Person reicht; Profilmodell mit Abholadresse und Fahrzeugdaten bestaetigt | Bestaetigte Auswahl |
| Kostenformel | Routenkilometer x Verbrauch/100 x Energiepreis; gleichmaessig einschliesslich Fahrer in EUR | Bestaetigter Vorschlag |
| Abrechnungsabschluss | Fahrer schliesst nach Fahrt ab; fixierte Strecke/Verbrauch/Preis/Anteile; Zahlungsbericht mit Bestaetigung oder Zurueckweisung durch Fahrer | Bestaetigter Vorschlag |
| Serienmodell | Woechentlich nach gewaehlten Wochentagen, feste Uhrzeit Europe/Berlin, Start-/Enddatum; einzelne Fahrt oder Serie absagen; Hin-/Rueckfahrt getrennt | Bestaetigter Vorschlag |
| Routing jetzt | Kein Routendienst; API-Key und automatische Optimierung werden spaeter ergaenzt | Benutzeranweisung |
| Entfernung jetzt | Fahrer gibt Kilometer vorlaeufig manuell ein, damit Abrechnung testbar ist | Bestaetigte Auswahl |
| Routing spaeter | Kostenloser Dienst mit kostenlosem API-Key, keine kostenpflichtige Google-API | Bestaetigte Auswahl |
| Staging-Datenbank | Eigene PostgreSQL-Instanz mit persistentem Speicher im Staging-Namespace vorbereiten | Bestaetigte Auswahl |
| GitOps-Ziel | ArgoCD beobachtet develop und deploy/kubernetes/overlays/staging | Bestaetigte Auswahl |
| Kostenuebersicht | Eigene Kosten/Zahlungsstaende und Erstattungen als Fahrer, Gesamtsummen und Aufschluesselung je Fahrt | Bestaetigte Erweiterung |
| Anfrageeingang | Fahrer sieht und bearbeitet Anfragen ueber alle eigenen Angebote | Benutzeranforderung |
| Aufgaben-Glocke | Offene Mitfahranfragen und gemeldete Zahlungen; vorerst kein Gelesen-/Ungelesen-Verlauf | Bestaetigte Erweiterung |
| Sortierung der Angebote | Naechste zukuenftige Abfahrt zuerst | Benutzeranforderung |
| Mobile Installation | Vue-Frontend als installierbare PWA mit automatischen Updates | Benutzeranforderung |
| PWA-Updateverhalten | Automatisch aktivieren, sobald keine ungespeicherten Eingaben oder laufenden Aktionen offen sind; bis dahin Hinweis anzeigen | Bestaetigte Auswahl |
| Backend | ASP.NET Core / .NET 10; .NET 11 spaeter angedacht | Urspruengliche Benutzeranforderung |
| API-Kommunikation | gRPC fuer das Frontend; SignalR als spaetere Moeglichkeit | Urspruengliche Benutzeranforderung |
| Identity Provider | Authentik im Zielcluster vorhanden; Integration spaeter vorgesehen | Urspruengliche Benutzeranforderung |
| Zusammenarbeit | Fachliche und Feature-Entscheidungen vor Umsetzung bestaetigen lassen | Benutzer: "assume nichts, was nicht bestaettigt ist" |

## Vorhandene technische Grundlage

- Vue 3, TypeScript, Vite und Bun im Frontend.
- PostgreSQL und EF Core im Backend; erste Trip-Migration vorhanden.
- Vier Trip-RPCs als Referenzimplementierung und Backend-Integrationstests.
- Kubernetes-Manifeste fuer Staging und Produktion sowie Container-Build-Workflow.

Diese Bestandsaufnahme bestaetigt keine noch offenen Produktregeln und belegt noch kein erfolgreiches Deployment.

## Ausstehende Benutzerantworten

| Entscheidung | Aktueller Stand |
| --- | --- |
| Browserpruefung | Benutzer hat Bun-basierte Installation bestaetigt; vollstaendiger Buchungs-/Abrechnungsablauf im echten Browser erfolgreich geprueft |
| Tatsaechlicher Rollout | Grundanwendung erfolgreich per GitOps veroeffentlicht und oeffentlich im Staging geprueft; Erweiterungen werden erneut validiert |

Mehrere Arbeitsstandorte, Chat, externe Benachrichtigungen und Administration wurden nicht als Pflichtfunktionen ausgewaehlt und sind daher nicht beauftragt. Das ist keine Aussage darueber, ob sie spaeter gewuenscht werden.

Weitere Detailfragen werden nach der Festlegung des Grundumfangs gestellt, soweit sie fuer die gewaehlten Funktionen erforderlich sind: Sichtbarkeit von Personen-/Kontaktdaten, Bearbeiten/Absagen von Fahrten, Buchungs- und Stornoregeln, Darstellung und benoetigte Staging-Konfiguration.

## Bestehende Beispielregeln zur spaeteren Bestaetigung

Die aktuelle Trip-Referenz verwendet unter anderem freie Ortsangaben, einzelne Fahrttermine, 1 bis 8 angebotene Mitfahrerplaetze, maximal 366 Tage Vorlauf, eine exakte Ortssuche und eine Absage durch den Ersteller vor Abfahrt. Diese Regeln wurden bisher von der Implementierung gewaehlt und sind noch keine bestaetigten Produktentscheidungen. Sie duerfen nicht stillschweigend als endgueltiger Funktionsumfang behandelt werden.

## Arbeit waehrend der Klaerung

Unabhaengige technische Verbesserungen koennen bereits umgesetzt und geprueft werden: lokale Geheimnisse aus Build-Artefakten ausschliessen, Tests vor Image-Veroeffentlichung ausfuehren, Datenbankbereitschaft pruefen und den Browser-Client aus dem vorhandenen gRPC-Vertrag erzeugen. Fachliche Erweiterungen warten auf die jeweils notwendige Antwort.
