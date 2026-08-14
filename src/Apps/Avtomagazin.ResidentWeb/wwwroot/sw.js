const APP_CACHE = "avtomagazin-resident-v2";
const TILE_CACHE = "avtomagazin-tiles-v1";
const PRECACHE = [
    "/",
    "/index.html",
    "/app.css",
    "/icon.svg",
    "/manifest.webmanifest",
    "/js/app.js",
    "/js/map.js",
    "/js/store.js",
    "/js/geo.js",
    "/lib/leaflet/leaflet.css",
    "/lib/leaflet/leaflet.js",
    "/data/bootstrap.json"
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(APP_CACHE)
            .then((cache) => cache.addAll(PRECACHE))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener("activate", (event) => {
    const keep = new Set([APP_CACHE, TILE_CACHE]);
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys.filter((key) => !keep.has(key)).map((key) => caches.delete(key)))
        ).then(() => self.clients.claim())
    );
});

self.addEventListener("fetch", (event) => {
    const request = event.request;
    if (request.method !== "GET") {
        return;
    }

    const url = new URL(request.url);
    if (url.origin !== self.location.origin) {
        return;
    }

    if (url.pathname.startsWith("/tiles/")) {
        event.respondWith(cacheFirst(request, TILE_CACHE));
        return;
    }

    if (isApi(url.pathname)) {
        event.respondWith(networkFirst(request));
        return;
    }

    if (request.mode === "navigate") {
        event.respondWith(
            fetch(request).catch(() => caches.match("/index.html"))
        );
        return;
    }

    event.respondWith(cacheFirst(request, APP_CACHE));
});

function isApi(pathname) {
    return pathname === "/api/snapshot"
        || pathname === "/config.json"
        || pathname.startsWith("/fleet/")
        || pathname.startsWith("/routing/")
        || pathname.startsWith("/notifications/");
}

async function cacheFirst(request, cacheName) {
    const cached = await caches.match(request);
    if (cached) {
        return cached;
    }

    const response = await fetch(request);
    if (response.ok) {
        const copy = response.clone();
        const cache = await caches.open(cacheName);
        await cache.put(request, copy);
    }
    return response;
}

async function networkFirst(request) {
    try {
        const response = await fetch(request);
        if (response.ok) {
            const copy = response.clone();
            const cache = await caches.open(APP_CACHE);
            await cache.put(request, copy);
        }
        return response;
    } catch {
        const cached = await caches.match(request);
        if (cached) {
            return cached;
        }
        return new Response(JSON.stringify({ online: false }), {
            status: 503,
            headers: { "Content-Type": "application/json" }
        });
    }
}
