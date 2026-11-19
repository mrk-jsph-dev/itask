import './styles/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './app/App.vue'
import { router } from './app/router'
import { registerServiceWorker } from './shared/pwa/register'

const app = createApp(App)

app.use(createPinia())
app.use(router)

app.mount('#app')

if (import.meta.env.PROD) void registerServiceWorker()
