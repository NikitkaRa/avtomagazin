const SNAPSHOT_KEY = "avtomagazin.resident.snapshot";
const SETTLEMENT_KEY = "avtomagazin.resident.settlement";
const FAVORITES_KEY = "avtomagazin.resident.favorites";
const QUEUE_KEY = "avtomagazin.resident.queue";
const DEVICE_KEY = "avtomagazin.device";

export function deviceToken() {
    let token = localStorage.getItem(DEVICE_KEY);
    if (!token) {
        token = `web-${crypto.randomUUID().slice(0, 12)}`;
        localStorage.setItem(DEVICE_KEY, token);
    }
    return token;
}

export function loadSnapshot() {
    try {
        const raw = localStorage.getItem(SNAPSHOT_KEY);
        return raw ? JSON.parse(raw) : null;
    } catch {
        return null;
    }
}

export function saveSnapshot(data) {
    localStorage.setItem(SNAPSHOT_KEY, JSON.stringify(data));
}

export function loadSettlement() {
    return localStorage.getItem(SETTLEMENT_KEY) || "";
}

export function saveSettlement(name) {
    localStorage.setItem(SETTLEMENT_KEY, name);
}

export function loadFavorites() {
    try {
        const items = JSON.parse(localStorage.getItem(FAVORITES_KEY) || "[]");
        return Array.isArray(items) ? items : [];
    } catch {
        return [];
    }
}

export function saveFavorites(items) {
    localStorage.setItem(FAVORITES_KEY, JSON.stringify(items));
}

export function isFavorite(stopId) {
    return loadFavorites().some((item) => item.id === stopId);
}

export function toggleFavoriteLocal(stop) {
    const items = loadFavorites();
    const index = items.findIndex((item) => item.id === stop.id);
    if (index >= 0) {
        items.splice(index, 1);
        saveFavorites(items);
        return false;
    }
    items.push({ id: stop.id, settlementName: stop.settlementName, routeId: stop.routeId });
    saveFavorites(items);
    return true;
}

export function loadQueue() {
    try {
        return JSON.parse(localStorage.getItem(QUEUE_KEY) || "{}");
    } catch {
        return {};
    }
}

export function saveQueue(queue) {
    localStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
}

export function enqueue(partial) {
    saveQueue({ ...loadQueue(), ...partial });
}
