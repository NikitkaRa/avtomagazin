import { tileXY } from "./geo.js";

const GRODNO = { lat: 53.62, lng: 24.05, zoom: 9 };

function el(id) {
    return document.getElementById(id);
}

function destroy(id) {
    const node = el(id);
    if (!node) {
        return;
    }
    if (node._avtoMap) {
        node._avtoMap.remove();
        node._avtoMap = null;
        node._avtoMarkers = null;
    }
    if (node._avtoYandex) {
        node._avtoYandex.destroy();
        node._avtoYandex = null;
    }
    node._avtoMode = null;
    node.innerHTML = "";
}

function initOsm(id) {
    const node = el(id);
    const map = L.map(node, { zoomControl: true, attributionControl: true });
    const tiles = L.tileLayer("/tiles/{z}/{x}/{y}.png", {
        maxZoom: 16,
        minZoom: 8,
        attribution: "&copy; OpenStreetMap"
    });
    tiles.addTo(map);
    map.setView([GRODNO.lat, GRODNO.lng], GRODNO.zoom);
    node._avtoMap = map;
    node._avtoMarkers = L.layerGroup().addTo(map);
    node._avtoMode = "osm";
    node._avtoTiles = tiles;
    setTimeout(() => map.invalidateSize(), 200);
    return tiles;
}

function initSchematic(id) {
    const node = el(id);
    node._avtoMode = "schematic";
    node.innerHTML = `<svg class="schematic" viewBox="0 0 400 260" role="img" aria-label="Схема остановок">
        <rect width="400" height="260" fill="#dfe8e1"/>
        <text x="12" y="22" fill="#5a6b5f" font-size="12">схема · тайлы ещё не кэшированы</text>
        <g id="${id}-schematic-points"></g>
    </svg>`;
}

function project(lat, lng, box) {
    const x = ((lng - box.minLng) / (box.maxLng - box.minLng)) * 400;
    const y = (1 - (lat - box.minLat) / (box.maxLat - box.minLat)) * 260;
    return { x, y };
}

function boundsOf(items) {
    const lats = items.map((item) => item.lat);
    const lngs = items.map((item) => item.lng);
    const minLat = Math.min(...lats);
    const maxLat = Math.max(...lats);
    const minLng = Math.min(...lngs);
    const maxLng = Math.max(...lngs);
    const latPad = Math.max((maxLat - minLat) * 0.18, 0.08);
    const lngPad = Math.max((maxLng - minLng) * 0.18, 0.12);
    return {
        minLat: minLat - latPad,
        maxLat: maxLat + latPad,
        minLng: minLng - lngPad,
        maxLng: maxLng + lngPad
    };
}

function setSchematicMarkers(id, items) {
    const group = document.getElementById(`${id}-schematic-points`);
    if (!group) {
        return;
    }
    const box = items.length ? boundsOf(items) : { minLat: 53.4, maxLat: 53.8, minLng: 23.7, maxLng: 27.9 };
    group.innerHTML = items.map((item) => {
        const { x, y } = project(item.lat, item.lng, box);
        const fill = item.kind === "van" ? "#1f6b4a" : item.kind === "home" ? "#d7a441" : "#ffffff";
        const stroke = item.kind === "stop" ? "#1f6b4a" : fill;
        const label = (item.label || item.emoji || "").replace(/</g, "");
        const href = item.stopId ? `#/stop/${item.stopId}` : "";
        return `<g>
            <circle cx="${x}" cy="${y}" r="10" fill="${fill}" stroke="${stroke}" stroke-width="3"/>
            <a href="${href}"><text x="${x + 14}" y="${y + 4}" font-size="11" fill="#122018">${label}</text></a>
        </g>`;
    }).join("");
}

function setOsmMarkers(id, items, fit) {
    const node = el(id);
    if (!node?._avtoMap || !node._avtoMarkers) {
        return;
    }
    node._avtoMarkers.clearLayers();
    const bounds = [];
    for (const item of items) {
        const icon = L.divIcon({
            className: "avto-pin",
            html: `<div class="avto-pin-inner avto-pin-${item.kind}">${item.emoji || ""}</div>`,
            iconSize: [36, 36],
            iconAnchor: [18, 18]
        });
        const marker = L.marker([item.lat, item.lng], { icon });
        if (item.popup) {
            marker.bindPopup(item.popup);
        }
        if (item.stopId) {
            marker.on("click", () => {
                location.hash = `#/stop/${item.stopId}`;
            });
        }
        marker.addTo(node._avtoMarkers);
        bounds.push([item.lat, item.lng]);
    }
    if (fit) {
        if (bounds.length === 1) {
            node._avtoMap.setView(bounds[0], 12);
        } else if (bounds.length > 1) {
            node._avtoMap.fitBounds(bounds, { padding: [28, 28], maxZoom: 13 });
        }
    }
    setTimeout(() => node._avtoMap.invalidateSize(), 150);
}

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[src="${src}"]`);
        if (existing && window.ymaps) {
            resolve();
            return;
        }
        const script = document.createElement("script");
        script.src = src;
        script.onload = () => resolve();
        script.onerror = () => reject(new Error("yandex maps failed"));
        document.head.appendChild(script);
    });
}

async function initYandex(id, apiKey) {
    const src = `https://api-maps.yandex.ru/2.1/?apikey=${encodeURIComponent(apiKey)}&lang=ru_RU`;
    await loadScript(src);
    await new Promise((resolve, reject) => {
        if (!window.ymaps) {
            reject(new Error("ymaps missing"));
            return;
        }
        window.ymaps.ready(resolve);
    });
    const node = el(id);
    const map = new window.ymaps.Map(node, {
        center: [GRODNO.lat, GRODNO.lng],
        zoom: GRODNO.zoom,
        controls: ["zoomControl"]
    });
    node._avtoYandex = map;
    node._avtoMode = "yandex";
}

function setYandexMarkers(id, items) {
    const node = el(id);
    const map = node?._avtoYandex;
    if (!map || !window.ymaps) {
        return;
    }
    map.geoObjects.removeAll();
    for (const item of items) {
        const placemark = new window.ymaps.Placemark(
            [item.lat, item.lng],
            { balloonContent: item.popup || item.label || "" },
            { preset: item.kind === "van" ? "islands#greenAutoIcon" : item.kind === "home" ? "islands#yellowHomeIcon" : "islands#darkGreenDotIcon" }
        );
        if (item.stopId) {
            placemark.events.add("click", () => {
                location.hash = `#/stop/${item.stopId}`;
            });
        }
        map.geoObjects.add(placemark);
    }
}

export async function ensure(id, { online, yandexKey }) {
    const node = el(id);
    if (!node) {
        return "none";
    }

    const want = yandexKey && online ? "yandex" : "osm";
    if (node._avtoMode === want) {
        return want;
    }

    destroy(id);

    if (want === "yandex") {
        try {
            await initYandex(id, yandexKey);
            return "yandex";
        } catch {
            initOsm(id);
            return "osm";
        }
    }

    if (typeof L !== "undefined") {
        const tiles = initOsm(id);
        let errors = 0;
        tiles.on("tileerror", () => {
            errors += 1;
            if (errors >= 8 && node._avtoMode === "osm") {
                const items = node._avtoLastItems || [];
                destroy(id);
                initSchematic(id);
                setSchematicMarkers(id, items);
            }
        });
        return "osm";
    }

    initSchematic(id);
    return "schematic";
}

export function setMarkers(id, items, { fit = true } = {}) {
    const node = el(id);
    if (!node) {
        return;
    }
    node._avtoLastItems = items;
    if (node._avtoMode === "osm") {
        setOsmMarkers(id, items, fit);
        return;
    }
    if (node._avtoMode === "yandex") {
        setYandexMarkers(id, items);
        return;
    }
    if (node._avtoMode !== "schematic") {
        initSchematic(id);
    }
    setSchematicMarkers(id, items);
}

export async function prefetchTiles(stops) {
    const urls = new Set();
    const addAround = (lat, lng, zooms) => {
        for (const z of zooms) {
            const { x, y } = tileXY(lat, lng, z);
            for (let dx = -1; dx <= 1; dx += 1) {
                for (let dy = -1; dy <= 1; dy += 1) {
                    urls.add(`/tiles/${z}/${x + dx}/${y + dy}.png`);
                }
            }
        }
    };

    addAround(GRODNO.lat, GRODNO.lng, [9, 10]);
    for (const stop of stops) {
        addAround(stop.latitude, stop.longitude, [11, 12, 13]);
    }

    await Promise.allSettled([...urls].slice(0, 140).map((url) => fetch(url)));
}

export { destroy };
