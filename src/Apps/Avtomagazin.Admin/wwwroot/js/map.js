export function init(id, lat, lon, zoom) {
    const el = document.getElementById(id);
    if (!el || typeof L === "undefined") {
        return false;
    }

    if (el._avtoMap) {
        el._avtoMap.remove();
        el._avtoMap = null;
    }

    const map = L.map(el, { zoomControl: true, attributionControl: true });
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 18,
        attribution: "&copy; OpenStreetMap"
    }).addTo(map);
    map.setView([lat, lon], zoom);
    el._avtoMap = map;
    el._avtoMarkers = L.layerGroup().addTo(map);
    setTimeout(() => map.invalidateSize(), 200);
    return true;
}

export function setMarkers(id, items) {
    const el = document.getElementById(id);
    if (!el || !el._avtoMap) {
        return;
    }

    el._avtoMarkers.clearLayers();
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
        marker.addTo(el._avtoMarkers);
        bounds.push([item.lat, item.lng]);
    }

    if (bounds.length === 1) {
        el._avtoMap.setView(bounds[0], 12);
    } else if (bounds.length > 1) {
        el._avtoMap.fitBounds(bounds, { padding: [28, 28], maxZoom: 13 });
    }

    setTimeout(() => el._avtoMap.invalidateSize(), 150);
}

export function invalidate(id) {
    const el = document.getElementById(id);
    if (el && el._avtoMap) {
        el._avtoMap.invalidateSize();
    }
}
