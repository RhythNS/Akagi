// The browser side of the interval timer: the running workout in localStorage (so a refresh finds it), the sounds,
// keeping the screen on, and Space to pause.
const sounds = {
    GetReady: 'audio/workout/get-ready.mp3',
    Go: 'audio/workout/go.mp3',
    RestTime: 'audio/workout/rest-time.mp3',
    AlmostThere: 'audio/workout/almost-there.mp3',
    GreatJob: 'audio/workout/great-job.mp3'
};
const players = {};
let wakeLock = null;
let keyHandler = null;

export function load(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function save(key, value) {
    try {
        localStorage.setItem(key, value);
        return true;
    } catch {
        return false;
    }
}

export function remove(key) {
    try {
        localStorage.removeItem(key);
    } catch {
        // Nothing stored then
    }
}

function player(name) {
    if (!players[name] && sounds[name]) {
        const audio = new Audio(sounds[name]);
        audio.preload = 'auto';
        players[name] = audio;
    }
    return players[name];
}

export function preload() {
    Object.keys(sounds).forEach(player);
}

// Plays a cue from the start; false when the browser blocks sound until the page is clicked.
export async function play(name) {
    const audio = player(name);
    if (!audio) {
        return true;
    }
    try {
        audio.currentTime = 0;
        await audio.play();
        return true;
    } catch (e) {
        return !(e && e.name === 'NotAllowedError');
    }
}

// Called from a click, so the browser lets the page play sound from now on.
export async function unlock() {
    preload();
    const audio = player('Go');
    try {
        audio.muted = true;
        await audio.play();
        audio.pause();
        audio.currentTime = 0;
        return true;
    } catch {
        return false;
    } finally {
        audio.muted = false;
    }
}

export async function keepAwake(on) {
    try {
        if (on && !wakeLock && navigator.wakeLock) {
            wakeLock = await navigator.wakeLock.request('screen');
            wakeLock.addEventListener('release', () => { wakeLock = null; });
        } else if (!on && wakeLock) {
            await wakeLock.release();
            wakeLock = null;
        }
    } catch {
        // Not supported or not allowed; the workout runs anyway
    }
}

export function listen(dotnet) {
    unlisten();
    keyHandler = (e) => {
        const tag = (e.target && e.target.tagName) || '';
        if (e.code === 'Space' && tag !== 'INPUT' && tag !== 'TEXTAREA' && tag !== 'BUTTON' && tag !== 'SELECT') {
            e.preventDefault();
            dotnet.invokeMethodAsync('TogglePauseFromKeyboard');
        }
    };
    window.addEventListener('keydown', keyHandler);
    // The lock is dropped when the tab is hidden; take it again when it comes back
    document.addEventListener('visibilitychange', onVisible);
}

function onVisible() {
    if (document.visibilityState === 'visible' && wakeLock === null && document.querySelector('[data-workout-running]')) {
        keepAwake(true);
    }
}

export function unlisten() {
    if (keyHandler) {
        window.removeEventListener('keydown', keyHandler);
        keyHandler = null;
    }
    document.removeEventListener('visibilitychange', onVisible);
    keepAwake(false);
}
