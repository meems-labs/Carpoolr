<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useId } from 'vue'
import { apiReachable } from '../state/connection'
import {
  applyUpdate,
  checkForUpdates,
  isOnline,
  updateApplying,
  updateAvailable,
  updateBlocked,
  updateError,
} from '../pwa/client'
import {
  canInstall,
  disposeInstall,
  initInstall,
  installed,
  installing,
  installError,
  isIos,
  promptInstall,
} from '../pwa/install'

const helpId = useId()
const helpOpen = ref(false)
const checking = ref(false)
const message = ref('')

onMounted(initInstall)
onBeforeUnmount(disposeInstall)

async function install() {
  message.value = ''
  const result = await promptInstall()
  if (result === 'accepted') message.value = 'Die Installation wurde gestartet.'
  if (result === 'dismissed') message.value = 'Die Installation wurde nicht bestätigt.'
}

async function check() {
  if (checking.value) return
  checking.value = true
  message.value = ''
  try {
    await checkForUpdates()
    if (!updateAvailable.value && !updateError.value && isOnline.value)
      message.value = 'Update-Prüfung abgeschlossen. Keine neue Version gefunden.'
  } finally {
    checking.value = false
  }
}
</script>

<template>
  <section class="pwa-controls" aria-label="App installieren und aktualisieren">
    <p v-if="!isOnline || !apiReachable" class="pwa-notice" role="status">
      Keine Verbindung. Für Fahrten, Anfragen und Zahlungen ist eine Internetverbindung nötig.
    </p>
    <div v-if="updateApplying || updateAvailable" class="pwa-notice" role="status">
      <p v-if="updateApplying">App wird aktualisiert …</p>
      <template v-else>
        <p v-if="updateBlocked">
          Ein Update ist bereit. Speichere oder verwirf offene Änderungen, auch in anderen
          geöffneten Fenstern. Laufende Vorgänge werden zuerst abgeschlossen. Danach wird die App
          automatisch aktualisiert.
        </p>
        <p v-else>Eine neue Version ist bereit und wird automatisch angewendet.</p>
        <button
          type="button"
          class="secondary"
          :disabled="updateBlocked || !isOnline"
          @click="applyUpdate"
        >
          Update anwenden
        </button>
      </template>
    </div>
    <p v-if="updateError || installError" class="pwa-error" role="alert">
      {{ updateError || installError }}
    </p>
    <div class="pwa-actions">
      <button
        v-if="(canInstall || installing) && !installed"
        type="button"
        class="secondary"
        :disabled="installing"
        @click="install"
      >
        {{ installing ? 'Installation wird geöffnet …' : 'App installieren' }}
      </button>
      <button
        v-if="!installed"
        type="button"
        class="secondary"
        :aria-expanded="helpOpen"
        :aria-controls="helpId"
        @click="helpOpen = !helpOpen"
      >
        Installationshilfe
      </button>
      <button
        type="button"
        class="secondary"
        :disabled="checking || !isOnline || updateApplying"
        @click="check"
      >
        {{ checking ? 'Suche nach Updates …' : 'Nach Updates suchen' }}
      </button>
    </div>
    <div v-if="helpOpen && !installed" :id="helpId" class="pwa-help">
      <p v-if="isIos">
        Öffne Carpoolr in Safari. Tippe auf „Teilen“, dann auf „Zum Home-Bildschirm“ und
        „Hinzufügen“. Falls „Als Web-App öffnen“ angezeigt wird, lass diese Option eingeschaltet.
      </p>
      <p v-else>
        Öffne das Browsermenü und suche nach „App installieren“ oder „Zum Startbildschirm
        hinzufügen“. Am Computer kann auch ein Installationssymbol in der Adressleiste erscheinen.
        Die Bezeichnung hängt vom Browser ab. Wenn keine Option angeboten wird, kannst du Carpoolr
        im Browser nutzen.
      </p>
      <p class="small">
        Die App prüft beim Öffnen und während der Nutzung auf neue Versionen. Für aktuelle Daten und
        Änderungen brauchst du eine Internetverbindung.
      </p>
    </div>
    <p v-if="message && !updateApplying" class="pwa-message" role="status">{{ message }}</p>
  </section>
</template>

<style scoped>
.pwa-controls {
  text-align: left;
  width: 100%;
  margin-top: 1rem;
  color: #25453a;
}
.pwa-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
.pwa-actions button {
  font-size: 0.85rem;
}
.pwa-notice,
.pwa-help {
  margin: 0 0 0.75rem;
  padding: 0.85rem 1rem;
  border: 1px solid var(--border);
  border-radius: 0.6rem;
  background: #fff;
  font-size: 0.9rem;
}
.pwa-notice p:last-child,
.pwa-help p:last-child {
  margin-bottom: 0;
}
.pwa-notice button {
  margin-top: 0.25rem;
}
.pwa-help {
  margin: 0.75rem 0 0;
  max-width: 48rem;
}
.pwa-error {
  color: #a53131;
}
.pwa-message {
  margin: 0.65rem 0 0;
  font-size: 0.85rem;
}
</style>
