// DeviceInfoMessage values from an injected user agent and screen (contract 5.4).

export const DEVICE_OS = Object.freeze({ ANDROID: 1, IOS: 2, WINDOWS: 3, MAC: 4, LINUX: 5, WINPHONE: 6 });

function sizeClass(w, h) {
  const m = Math.min(Number(w) || 0, Number(h) || 0);
  if (m < 600) return 1;
  if (m < 900) return 2;
  return 3;
}

function iosVersion(ua) {
  let m = /Macintosh/.test(ua) ? null : /\bOS (\d+)[_.](\d+)(?:[_.](\d+))?\b/.exec(ua);
  if (m) return m[1] + '.' + m[2] + (m[3] ? '.' + m[3] : '');
  m = /\bVersion\/(\d+(?:\.\d+)*)/.exec(ua);
  return m ? m[1] : '';
}

// env: { ua, screenWidth, screenHeight, maxTouchPoints }
export function detectDevice(env = {}) {
  const ua = String(env.ua || '');
  const touch = Number(env.maxTouchPoints) || 0;
  let os = DEVICE_OS.LINUX;
  let model = 'Desktop';
  let type = 'Desktop';
  let osName = 'Web';
  if (/Windows Phone/i.test(ua)) {
    os = DEVICE_OS.WINPHONE;
    type = 'Handheld';
  } else if (/iPhone|iPod/.test(ua)) {
    os = DEVICE_OS.IOS;
    model = 'iPhone';
    type = 'Handheld';
  } else if (/iPad/.test(ua) || (/Macintosh/.test(ua) && touch > 1)) {
    os = DEVICE_OS.IOS;
    model = 'iPad';
    type = 'Handheld';
  } else if (/Android/.test(ua)) {
    os = DEVICE_OS.ANDROID;
    model = 'Android';
    type = 'Handheld';
  } else if (/Windows/.test(ua)) {
    os = DEVICE_OS.WINDOWS;
  } else if (/Mac OS X|Macintosh/.test(ua)) {
    os = DEVICE_OS.MAC;
  } else if (/Linux|X11|CrOS/.test(ua)) {
    os = DEVICE_OS.LINUX;
  }
  if (os === DEVICE_OS.IOS) {
    const v = iosVersion(ua);
    osName = v ? 'iOS ' + v : 'iOS';
  } else if (os === DEVICE_OS.ANDROID) {
    const m = /Android (\d+(?:\.\d+)*)/.exec(ua);
    osName = m ? 'Android ' + m[1] : 'Android';
  }
  return {
    DeviceSize: sizeClass(env.screenWidth, env.screenHeight),
    DeviceOS: os,
    DeviceModel: model,
    DeviceType: type,
    DeviceOperatingSystem: osName,
  };
}

// The fields of DeviceInfoMessage (builder adds TypeString and Response).
export function deviceInfoFields(env, uid) {
  const d = detectDevice(env);
  return {
    DeviceSize: d.DeviceSize,
    DeviceOS: d.DeviceOS,
    DeviceModel: d.DeviceModel,
    DeviceType: d.DeviceType,
    DeviceUID: String(uid || ''),
    DeviceOperatingSystem: d.DeviceOperatingSystem,
  };
}
