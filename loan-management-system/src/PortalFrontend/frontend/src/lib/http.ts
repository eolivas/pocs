import axios from 'axios';

/**
 * Shared Axios instance. Base URL comes from VITE_API_BASE_URL (default '/api',
 * proxied to the BFF in dev). The portal only ever calls the BFF — never a
 * backend service directly. An auth token interceptor is added in a later deep-dive.
 */
const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
});

export default http;
