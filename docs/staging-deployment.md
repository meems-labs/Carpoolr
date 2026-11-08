# Staging mit ArgoCD

Das bestaetigte Ziel ist der Branch `develop` und der Pfad `deploy/kubernetes/overlays/staging`. Nur dieses Overlay schaltet `ASPNETCORE_ENVIRONMENT=Staging` und `DemoMode__Enabled=true` ein. Es ist bewusst ohne Anmeldung nutzbar; ueber den Testpersonen-Umschalter lassen sich unterschiedliche Rollen ausprobieren.

## Ablauf

1. Backend- und Frontend-Pruefungen muessen erfolgreich sein.
2. CI baut beide Images und veroeffentlicht zusaetzlich Tags mit dem vollstaendigen Git-Commit-SHA.
3. Erst danach aktualisiert der Workflow beide Image-Referenzen im Staging-Overlay und pusht einen reinen Deployment-Commit nach `develop`. Dieser Commit startet keinen weiteren Image-Build. Ein inzwischen neuerer Quellcode-Commit verhindert eine veraltete Promotion.
4. ArgoCD synchronisiert die unveraenderlichen Image-Tags. Die Produktionsreferenzen werden dabei nicht veraendert.

Der Repository-Workflow benoetigt Schreibrechte fuer `contents`, um den Deployment-Commit zu erstellen. Falls Branch-Schutz direkte Bot-Pushes verbietet, muss die bestehende GitOps-Policy entsprechend eingerichtet oder die Image-Promotion als Pull Request uebernommen werden; der Workflow pusht niemals mit `--force`.

## Datenbank und Startreihenfolge

Das Staging-Overlay definiert eine eigene PostgreSQL-17-Instanz mit einem persistenten 1-GiB-Volume. Der Cluster muss eine passende Standard-StorageClass bereitstellen. Die Datenbank ist nur ueber einen ClusterIP-Service erreichbar.

ArgoCD Sync-Waves ordnen den Start:

- `-4`: dedizierter ServiceAccount und namespace-begrenzte Berechtigungen fuer den einmaligen Secret-Bootstrap.
- `-3`: ein kurzlebiger Job erzeugt ein zufaelliges Datenbankpasswort und speichert Passwort plus Verbindungszeichenfolge im Kubernetes-Secret `carpoolr-database`. Ein vorhandenes, vollstaendiges Secret wird beibehalten, niemals automatisch rotiert. Zugangsdaten stehen weder im Git-Repository noch in Prozessargumenten.
- `-2`: PostgreSQL-Service und StatefulSet; der Speicher bleibt bei Pod-Neustarts erhalten.
- `-1`: ein Migration-Job verwendet dasselbe API-Image mit `--migrate` und wendet EF-Core-Migrationen an.
- `0`: API und Web-Anwendung; `/readyz` gibt die API erst mit erreichbarer, passender Datenbank frei.

Der Bootstrap nutzt die im Pod bereitgestellte Kubernetes-CA mit Hostnamen- und Serverzertifikatspruefung. Sein ServiceAccount darf nur das benannte Secret lesen und Secrets im Namespace erstellen. Kubernetes kann das Recht `create` nicht auf einen Secret-Namen beschraenken. Normale API-, Web-, Migrations- und Datenbank-Pods erhalten keinen ServiceAccount-Token.

Als manuelle Alternative kann ein Administrator mit schreibfaehigem Staging-Kontext `scripts/prepare-staging-database.ps1 -Context <kontext>` ausfuehren. Auch das Skript behaelt vorhandene Zugangsdaten bei und uebergibt neue Werte ausschliesslich ueber stdin.

## Nach dem Rollout pruefen

Unter `https://carpoolr.staging.splashmich.net` muss die Anwendung die Testpersonen-Auswahl anzeigen. `/api/demo/users` liefert den Katalog, `/api/healthz` die Liveness und `/api/readyz` die Datenbankbereitschaft. Die Website verwendet gRPC-Web ueber `/api/`; natives gRPC ist am bisherigen nginx-Proxy nicht eingerichtet.

Im Browser zuerst fuer die Testpersonen Profile anlegen, fuer einen Fahrer ein Fahrzeug eintragen und anschliessend Angebote und Mitfahranfragen erstellen. Streckenkilometer werden bis zur spaeteren Routendienst-Anbindung manuell erfasst. Nach Abfahrt kann der Fahrer die Fahrt abschliessen; danach lassen sich Zahlungen melden und bestaetigen.

Die Manifeste koennen lokal ohne Clusterzugriff mit `kubectl kustomize deploy/kubernetes/overlays/staging` geprueft werden. Ein erfolgreicher Build oder ein gerendertes Manifest belegt noch keinen erfolgreichen Rollout. Den tatsaechlichen ArgoCD-Sync und die Endpunkte nach der Veroeffentlichung kontrollieren.
