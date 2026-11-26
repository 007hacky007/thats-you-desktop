// Tracks which camera views are mounted so a start or flip that settles after
// its view unmounted can release the shared camera without taking it away from
// a newer view. No DOM, imports in Node.

export function createCameraOwners() {
  let count = 0;
  return {
    get count() { return count; },
    mount() {
      let mounted = true;
      count++;
      return {
        // call once when the view goes away
        unmount(camera) {
          if (!mounted) return;
          mounted = false;
          count--;
          try { camera.release(); } catch { /* ignore */ }
        },
        // call after every awaited camera start/flip
        settled(camera) {
          if (mounted || count > 0) return;
          try { camera.release(); } catch { /* ignore */ }
        },
      };
    },
  };
}

export const cameraOwners = createCameraOwners();
