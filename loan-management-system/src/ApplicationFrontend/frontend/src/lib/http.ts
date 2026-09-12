import axios from 'axios';

/**
 * Shared Axios instance. Base URL comes from VITE_API_BASE_URL (default '/api',
 * proxied to the Application BFF in dev). This frontend only ever calls the
 * Application BFF — never a backend service directly. Auth is a later deep-dive.
 */
const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
});

export default http;
