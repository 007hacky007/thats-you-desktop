// Resource handshake (contract 5.4): ResourceRequirementsMessage ->
// RequestResourceMessage for missing Required images -> AllResourcesReceivedMessage.

import { nullLog } from '../util/log.js';
import { build, REQUIREMENT } from './messages.js';

export const REQUEST_RETRY_MS = 5000;

export function createResources({ store, sendMsg, clock, log = nullLog, retryMs = REQUEST_RETRY_MS }) {
  let manifest = null; // { required: [guid], confirmed: bool }
  const requestedAt = new Map();

  function evaluate() {
    if (!manifest || manifest.confirmed) return;
    const now = clock.now();
    const missing = manifest.required.filter((g) => !store.has(g));
    for (const g of missing) {
      const at = requestedAt.get(g);
      if (at !== undefined && now - at < retryMs) continue;
      if (sendMsg(build.requestResource(g))) requestedAt.set(g, now);
    }
    if (missing.length === 0) {
      const reqs = manifest.required.map((g) => ({ GUID: g, Rqrmnt: REQUIREMENT.Required }));
      if (sendMsg(build.allResourcesReceived(reqs))) manifest.confirmed = true;
    }
  }

  function onManifest(msg) {
    const list = Array.isArray(msg.Requirements) ? msg.Requirements : [];
    const required = [];
    const safe = [];
    for (const r of list) {
      if (!r || typeof r.GUID !== 'string' || r.GUID === '') continue;
      if (r.Rqrmnt === REQUIREMENT.Required) {
        if (!required.includes(r.GUID)) required.push(r.GUID);
      } else if (r.Rqrmnt === REQUIREMENT.SafeToUnload) {
        safe.push(r.GUID);
      }
    }
    for (const g of safe) if (!required.includes(g)) store.remove(g);
    store.setRequired(required);
    manifest = { required, confirmed: false };
    evaluate();
  }

  const off = store.on('added', () => evaluate());

  return {
    onManifest,
    evaluate,
    reset() {
      manifest = null;
      requestedAt.clear();
    },
    get manifest() { return manifest; },
    destroy: off,
  };
}
