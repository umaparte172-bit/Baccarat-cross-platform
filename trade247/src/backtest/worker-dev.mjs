// Development and tests only: lets a worker thread load the TypeScript worker through tsx.
// The built program (npm run build) starts worker.js directly and never uses this file.
import { register } from 'tsx/esm/api';

register();
await import('./worker.ts');
