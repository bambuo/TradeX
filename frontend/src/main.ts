import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import AppButton from './components/AppButton.vue'
import AppCard from './components/AppCard.vue'
import AppIcon from './components/AppIcon.vue'
import AppModal from './components/AppModal.vue'
import router from './router'
import './style.css'

const app = createApp(App)
app.component('AppButton', AppButton)
app.component('AppCard', AppCard)
app.component('AppIcon', AppIcon)
app.component('AppModal', AppModal)
app.use(createPinia())
app.use(router)
app.mount('#app')
