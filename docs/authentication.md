# Anmeldung mit Authentik

Carpoolr unterstützt eine Anmeldung über `https://auth.splashmich.net`. Der Browser wird zu Authentik weitergeleitet; die API tauscht den Authorization Code mit PKCE aus und legt eine eigene Sitzung an. OAuth-Tokens und Client-Secrets werden nicht an die Vue-App ausgegeben oder im Browser gespeichert.

Der bisherige Staging-Testmodus bleibt aktiv, bis ein tatsächlicher OAuth2/OpenID-Connect-Provider und seine Zugangsdaten konfiguriert sind. Der Benutzer hat bestätigt, dass dieser Provider noch nicht vorhanden ist. Das optionale Kubernetes-Component ist noch nicht im beobachteten Staging-Overlay aktiviert.

## 1. Application und Provider anlegen

In der Authentik-Administration unter **Applications → Applications → New Application** eine Application mit einem **OAuth2/OIDC**-Provider erstellen. Die genaue Bezeichnung kann je nach Authentik-Version abweichen; die [offizielle Anleitung](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/create-oauth2-provider/) beschreibt den gemeinsamen Ablauf.

| Einstellung | Wert für Carpoolr-Staging |
| --- | --- |
| Application name | Beispielsweise `Carpoolr Staging` |
| Application slug | Für diese neue Einrichtung `carpoolr`; anschließend unverändert lassen |
| Launch URL | `https://carpoolr.staging.splashmich.net` |
| Client type | **Confidential**; der Client-Secret bleibt ausschließlich in der API |
| Redirect URI matching mode | **Strict** |
| Redirect URI | `https://carpoolr.staging.splashmich.net/api/auth/signin-oidc` |
| Grant type | **Authorization code**; unnötige Grant Types deaktivieren, falls die installierte Version eine Auswahl anbietet |
| Scope mappings | `openid` und `profile` |
| Subject mode | Ein stabiler interner Bezeichner, beispielsweise **hashed user ID**; keine E-Mail-Adresse oder veränderbarer Benutzername |
| Issuer mode | **Each provider has a different issuer** |
| Signing Key | Ein vorhandenes asymmetrisches Signaturzertifikat auswählen, beispielsweise RSA/RS256 |
| Encryption Key | Für diese Integration nicht erforderlich |

Die Redirect-URI vollständig eintragen; weder Wildcards noch eine leere Liste verwenden. Die API fordert weder `offline_access` noch Zugriff auf die Authentik-Administrations-API an. Signaturschlüssel und Discovery müssen von der API per HTTPS erreichbar sein. Details zu [Redirects, Issuer und Signaturen](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/) stehen in der Provider-Dokumentation.

Unter **Policy / Group / User Bindings** an der Carpoolr-Application die tatsächlich freigeschalteten Benutzer oder Gruppen binden. Carpoolr verlangt keine zusätzliche lokale Gruppenmitgliedschaft: Der bestätigte Benutzerkreis wird über diese Application festgelegt. Eine Application ohne Bindings ist in Authentik standardmäßig für alle Benutzer zugänglich; dies ist keine explizite Freischaltung. Bei mehreren unabhängigen erlaubten Benutzern/Gruppen ist der Policy-Engine-Modus **Any** passend. Siehe [Bindings in Authentik](https://docs.goauthentik.io/add-secure-apps/bindings-overview/).

Anschließend die im Provider angezeigte **OpenID Configuration URL** öffnen. Deren `issuer`-Wert exakt übernehmen, einschließlich abschließendem Slash. Nach Einrichtung des Slugs `carpoolr` ist die Discovery-URL `https://auth.splashmich.net/application/o/carpoolr/.well-known/openid-configuration` und bei dem oben gewählten Issuer-Modus der erwartete Issuer `https://auth.splashmich.net/application/o/carpoolr/`. Vor Einrichtung des Providers liefert diese Discovery-URL noch keine Konfiguration.

Die **Client-ID** sowie das **Client-Secret** aus dem Provider übernehmen. Das Secret nicht in Chat, Git, Screenshots oder Build-Argumente eintragen.

## 2. Zugangsdaten und Schlüssel bereitstellen

Die API liest folgende Konfiguration aus Prozessvariablen beziehungsweise Kubernetes-Secrets:

| Variable | Bedeutung |
| --- | --- |
| `DemoMode__Enabled` | `false`, sobald die echte Anmeldung eingeschaltet wird |
| `Authentication__Authority` | Exakter HTTPS-`issuer` aus dem Discovery-Dokument |
| `Authentication__Audience` | Audience für separat verwendete JWT-Access-Tokens; bei der üblichen Authentik-Provider-Konfiguration die tatsächliche Client-ID, bei eigener Audience entsprechend deren Wert |
| `Authentication__Browser__Enabled` | `true` |
| `Authentication__Browser__ClientId` | Tatsächliche Client-ID des Confidential Providers |
| `Authentication__Browser__ClientSecret` | Secret des Providers, ausschließlich über Secret-Verwaltung bereitstellen |
| `Authentication__Browser__PublicOrigin` | `https://carpoolr.staging.splashmich.net` ohne Pfad |
| `Authentication__Browser__DataProtectionKeysPath` | Absoluter Pfad des persistenten ASP.NET-Core-Schlüsselverzeichnisses |

Die Audience ist kein frei erfundener API-Name: Sie muss zum `aud` eines vom Provider ausgestellten Access-Tokens passen. Die OIDC-Anmeldung prüft unabhängig davon ihre ID-Token-Audience gegen die Browser-Client-ID.

Bei aktivierter Browser-Anmeldung müssen separat verwendete Bearer-Access-Tokens außerdem die signierte `azp`-Client-ID dieses Providers und einen nicht leeren `scope` enthalten. Authentik ergänzt diese Felder bei der Ausstellung seiner Access-Tokens; reine ID-Tokens sollen nicht als API-Anmeldeinformationen verwendet werden. Die generische lokale Development-Konfiguration mit `dotnet user-jwts` bleibt davon getrennt.

Das Component erwartet das Secret **`carpoolr-authentik`** im Namespace **`staging`** mit den Schlüsseln `Authentication__Authority`, `Authentication__Audience`, `Authentication__Browser__ClientId` und `Authentication__Browser__ClientSecret`. Ein vorhandener Secret-Controller kann dieses Secret bereitstellen. Alternativ kann ein Administrator mit einem schreibfähigen Kontext das vorbereitete Skript ausführen:

```powershell
./scripts/prepare-staging-authentik.ps1 -Context <tatsaechlicher-kontext>
```

Das Skript fragt die Werte interaktiv ab, verdeckt die Secret-Eingabe und übergibt das Kubernetes-Objekt über stdin. Es schreibt keine Zugangsdaten in Dateien oder Prozessargumente. Ein bestehendes Secret wird nicht überschrieben. Dieses Skript wurde hier nicht gegen einen Cluster ausgeführt; der derzeitige SSH-Zugang wird nicht verwendet.

Das persistente Volume **`carpoolr-authentication-keys`** speichert den ASP.NET-Core-Data-Protection-Schlüsselring. Damit bleiben Sitzungen beim Austausch des API-Pods lesbar. Diese Schlüssel erlauben das Entschlüsseln von Sitzungstickets: PVC-Zugriff und Backups entsprechend beschränken sowie die Verschlüsselung des verwendeten Storage sicherstellen. Der Schlüsselring wird in dieser Konfiguration nicht zusätzlich mit einem eigenen Anwendungszertifikat verschlüsselt. Das PVC ist von automatischem ArgoCD-Pruning ausgenommen; es darf bei Aufräumarbeiten nicht beiläufig gelöscht werden.

## 3. Im bestehenden GitOps-Pfad aktivieren

Der beobachtete Pfad bleibt **`deploy/kubernetes/overlays/staging`**, Branch **`develop`**. Nach der Provider- und Secret-Einrichtung in dessen `kustomization.yaml` den bisherigen Abschnitt

```yaml
patches:
  - path: api-demo.patch.yaml
```

durch diesen ersetzen:

```yaml
components:
  - ../../components/staging-authentik
```

Den Demo-Patch entfernen, damit er die Authentik-Konfiguration nicht wieder überschreibt. Die übrigen Ressourcen und die von CI gepflegten Image-Tags bleiben im Staging-Overlay. Keine Beispiel-Zugangsdaten hinzufügen.

Das Component übernimmt die Datenbank-Verbindung, deaktiviert die Testpersonen-Simulation und bindet den persistenten Schlüsselring ein. Es verwendet eine API-Replik mit **Recreate**, damit sich zwei Pods nicht um das ReadWriteOnce-Volume streiten. Beim API-Rollout entsteht dadurch eine kurze Unterbrechung. Mehrere Repliken benötigen ein gemeinsames geeignetes Schlüssel-Backend und eine dazu passende Rollout-Strategie.

Die API ist bei dieser Betriebsart ausschließlich hinter der konfigurierten öffentlichen HTTPS-Origin vorgesehen. Für den internen HTTP-Hop vom Reverse Proxy setzt sie ihr logisches Request-Schema ausdrücklich aus dieser validierten Konfiguration auf HTTPS; dadurch funktionieren die stets sicheren Cookie-/CSRF-Einstellungen auch nach TLS-Terminierung. Client-gesteuerte `X-Forwarded-Host`-/`X-Forwarded-Proto`-Header bestimmen weder Schema noch Callback-Adresse. Die API nicht zusätzlich als öffentlichen HTTP-Dienst exponieren.

Der getrennte Datenbank-Migrationsjob führt weiterhin nur `--migrate` unter seiner bestehenden Staging-Konfiguration aus. Er startet keinen HTTP-Server und stellt keinen Demo-Zugang bereit.

Vor dem Commit lokal rendern:

```powershell
kubectl kustomize deploy/kubernetes/overlays/staging
```

Danach über den vorhandenen GitOps-Ablauf ausrollen. Ein gerendertes Manifest oder grüner Build bestätigt noch keine erfolgreiche Anmeldung am tatsächlichen IdP.

## Verhalten und Prüfung

- Nicht angemeldete Besucher erhalten eine Anmeldemöglichkeit; im OIDC-Modus ist keine Testpersonenauswahl vorhanden.
- Eine berechtigte Person kann sich anmelden und erhält ein eigenes Profil anhand des stabilen Paars `iss` und `sub`. Vorhandene Demo-Profile, Buchungen und Zahlungen werden nicht automatisch übernommen.
- Eine nicht freigeschaltete Person muss bereits an der Authentik-Application abgewiesen werden. Das sowohl mit einem erlaubten als auch einem nicht erlaubten Konto prüfen.
- Die eigene Sitzung ist ein HTTPS-only, HttpOnly-Cookie mit einer maximalen Laufzeit von acht Stunden, ohne dauerhafte Browser-Anmeldung. Die Vue-App erhält keine OAuth-Tokens. Sitzungs- und Authentifizierungsantworten sowie gRPC-Daten werden vom Service Worker nicht gecacht.
- Alle gRPC-POSTs mit Cookie-Sitzung und das lokale Abmelden benötigen einen zur Sitzung passenden `X-CSRF-Token`. Ein frei eingesetzter Testpersonen-Header erzeugt im OIDC-Modus keine Identität. Explizite Bearer-Clients werden weiterhin separat per JWT geprüft.
- `GET /api/auth/session` liefert Anmeldestatus und CSRF-Token; `GET /api/auth/login?returnUrl=/...` startet die Anmeldung. `returnUrl` akzeptiert ausschließlich lokale Pfade. API-Aufrufe erhalten bei abgelaufener Sitzung einen Fehler statt einer HTML-Anmeldeseite.
- **Abmelden** beendet die Carpoolr-Sitzung. Die Authentik-Sitzung und andere Anwendungen bleiben bestehen; erneutes Anmelden kann daher ohne erneute Passworteingabe erfolgen.
- Änderungen an Authentik-Freigaben beenden bereits ausgestellte Carpoolr-Sitzungen nicht sofort. Ohne zusätzliche Widerrufsprüfung können sie bis zum Ende der achtstündigen Laufzeit weiter gelten. Front-/Back-Channel-Logout und sofortige Sperrung sind in dieser Version nicht implementiert.
- Nach einem API-Pod-Austausch muss eine noch gültige Sitzung mit demselben Schlüsselring weiter funktionieren. Nach acht Stunden ist eine neue Anmeldung erforderlich.

Für lokalen Demo-Betrieb kann die vorhandene getrennte Vite/API-Konfiguration weiterverwendet werden. Für OIDC werden Frontend und API unter derselben konfigurierten HTTPS-Origin einschließlich `/api` betrieben; der bisherige HTTP/CORS-Demo-Start ist kein OIDC-Testaufbau.
