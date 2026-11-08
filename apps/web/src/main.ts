import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import './styles.css'
import { disposePwaClient, initPwaClient } from './pwa/client'

createApp(App).use(router).mount('#app')
initPwaClient()
if (import.meta.hot) import.meta.hot.dispose(disposePwaClient)
