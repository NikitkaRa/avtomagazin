export function init(id, lat, lon, zoom) {
    const el = document.getElementById(id);
    if (!el || typeof L === "undefined") {
        return false;
    }

    if (el._avtoMap) {
        el._avtoMap.remove();
        el._avtoMap = null;
        el._avtoClusters = null;
        el._avtoVans = null;
    }

    const map = L.map(el, { zoomControl: true, attributionControl: true });
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 18,
        attribution: "&copy; OpenStreetMap"
    }).addTo(map);
    map.setView([lat, lon], zoom);
    el._avtoMap = map;

    if (typeof L.markerClusterGroup === "function") {
        el._avtoClusters = L.markerClusterGroup({
            showCoverageOnHover: false,
            maxClusterRadius: 56,
            spiderfyOnMaxZoom: true,
            disableClusteringAtZoom: 16
        }).addTo(map);
    } else {
        el._avtoClusters = L.layerGroup().addTo(map);
    }

    el._avtoVans = L.layerGroup().addTo(map);
    el._avtoFitted = false;
    el._avtoUserMoved = false;

    const lockView = () => { el._avtoUserMoved = true; };
    map.on("dragstart", lockView);
    map.on("zoomstart", (e) => {
        if (e.originalEvent) {
            lockView();
        }
    });

    setTimeout(() => map.invalidateSize(), 200);
    return true;
}

export function setMarkers(id, items) {
    const el = document.getElementById(id);
    if (!el || !el._avtoMap || !el._avtoClusters || !el._avtoVans) {
        return;
    }

    el._avtoClusters.clearLayers();
    el._avtoVans.clearLayers();
    const bounds = [];

    for (const item of items) {
        const isVan = item.kind === "van" || item.kind === "van-stale";
        const size = item.kind === "van" ? 22 : item.kind === "van-stale" ? 18 : 10;
        const icon = L.divIcon({
            className: "avto-pin",
            html: `<div class="avto-pin-inner avto-pin-${item.kind}"></div>`,
            iconSize: [size, size],
            iconAnchor: [size / 2, size / 2]
        });
        const marker = L.marker([item.lat, item.lng], { icon });
        if (item.popup) {
            marker.bindPopup(item.popup);
        }

        if (isVan) {
            marker.addTo(el._avtoVans);
        } else {
            el._avtoClusters.addLayer(marker);
        }

        bounds.push([item.lat, item.lng]);
    }

    if (bounds.length > 0 && !el._avtoFitted && !el._avtoUserMoved) {
        if (bounds.length === 1) {
            el._avtoMap.setView(bounds[0], 12);
        } else {
            el._avtoMap.fitBounds(bounds, { padding: [28, 28], maxZoom: 13 });
        }
        el._avtoFitted = true;
    }
}
