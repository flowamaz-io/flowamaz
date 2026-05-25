import { createApp } from 'vue';
import { createPinia } from 'pinia';
import App from './App.vue';
import { router } from './router';
import { useAuthStore } from './stores/auth.store';
import './assets/main.css';

const app = createApp(App);
const pinia = createPinia();

app.use(pinia);

// Instantiate the auth store before the router mounts so the Axios bridge is wired and the
// first navigation guard can perform a silent session restore.
useAuthStore();

app.use(router);
app.mount('#app');
