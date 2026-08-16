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
        el._avtoRouteLayer = null;
        el._avtoRouteLine = null;
        el._avtoRouteMarkers = null;
        el._avtoFavLayer = null;
        el._avtoClickHandler = null;
        el._avtoRouteRef = null;
    }

    const map = L.map(el, { zoomControl: true, attributionControl: false });
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 18,
        attribution: ""
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
    el._avtoRouteLayer = L.layerGroup().addTo(map);
    el._avtoFavLayer = L.layerGroup().addTo(map);
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

export function setFavoriteHeat(id, items, options) {
    const el = document.getElementById(id);
    if (!el || !el._avtoMap) {
        return;
    }

    if (!el._avtoFavLayer) {
        el._avtoFavLayer = L.layerGroup().addTo(el._avtoMap);
    }

    el._avtoFavLayer.clearLayers();
    const list = (items || []).filter((x) => x && Number.isFinite(x.lat) && Number.isFinite(x.lng));
    if (list.length === 0) {
        return;
    }

    const scaleMin = Number.isFinite(Number(options?.scaleMin)) ? Number(options.scaleMin) : 0;
    const scaleMax = Number(options?.scaleMax) || 100;
    const fit = options?.fit !== false;
    const bounds = [];

    for (const item of list) {
        const count = Math.max(0, Number(item.count) || 0);
        const t = Math.min(1, Math.max(0, (Math.min(count, scaleMax) - scaleMin) / Math.max(1, scaleMax - scaleMin)));
        const color = favColor(t);
        const radius = 7 + Math.round(11 * t);
        const circle = L.circleMarker([item.lat, item.lng], {
            radius,
            color: "#fff",
            weight: 2,
            fillColor: color,
            fillOpacity: 0.82
        });
        const label = item.name || "Остановка";
        circle.bindPopup(`<b>${label}</b><br/>В избранном: <b>${count}</b>`);
        circle.addTo(el._avtoFavLayer);
        bounds.push([item.lat, item.lng]);
    }

    if (fit && bounds.length > 0) {
        if (bounds.length === 1) {
            el._avtoMap.setView(bounds[0], 12);
        } else {
            el._avtoMap.fitBounds(bounds, { padding: [36, 36], maxZoom: 12 });
        }
    }
}

function favColor(t) {
    // 0 (cool mint) → 100 (warm terracotta)
    const stops = [
        [0, [180, 220, 190]],
        [0.35, [60, 170, 110]],
        [0.65, [210, 150, 70]],
        [1, [154, 75, 46]]
    ];
    let a = stops[0];
    let b = stops[stops.length - 1];
    for (let i = 0; i < stops.length - 1; i++) {
        if (t >= stops[i][0] && t <= stops[i + 1][0]) {
            a = stops[i];
            b = stops[i + 1];
            break;
        }
    }
    const span = (b[0] - a[0]) || 1;
    const u = (t - a[0]) / span;
    const rgb = a[1].map((c, i) => Math.round(c + (b[1][i] - c) * u));
    return `rgb(${rgb[0]}, ${rgb[1]}, ${rgb[2]})`;
}

export function enableRouteClicks(id, dotNetRef) {
    const el = document.getElementById(id);
    if (!el || !el._avtoMap) {
        return false;
    }

    el._avtoRouteRef = dotNetRef;

    if (el._avtoClickHandler) {
        el._avtoMap.off("click", el._avtoClickHandler);
    }

    el._avtoClickHandler = (e) => {
        if (el._avtoDragging) {
            return;
        }
        dotNetRef.invokeMethodAsync("OnMapClick", e.latlng.lat, e.latlng.lng);
    };
    el._avtoMap.on("click", el._avtoClickHandler);
    el._avtoMap.getContainer().style.cursor = "crosshair";
    return true;
}

function refreshRouteLine(el) {
    if (el._avtoRouteLine) {
        el._avtoMap.removeLayer(el._avtoRouteLine);
        el._avtoRouteLine = null;
    }

    const points = (el._avtoRouteMarkers || []).map((m) => m.getLatLng());
    if (points.length >= 2) {
        el._avtoRouteLine = L.polyline(points, {
            color: "#1f7a4d",
            weight: 3,
            opacity: 0.85
        }).addTo(el._avtoMap);
    }
}

export function setRouteStops(id, stops, fit) {
    const el = document.getElementById(id);
    if (!el || !el._avtoMap || !el._avtoRouteLayer) {
        return;
    }

    el._avtoRouteLayer.clearLayers();
    el._avtoRouteMarkers = [];
    if (el._avtoRouteLine) {
        el._avtoMap.removeLayer(el._avtoRouteLine);
        el._avtoRouteLine = null;
    }

    const points = [];
    (stops || []).forEach((stop, index) => {
        const seq = stop.seq ?? stop.sequence ?? (index + 1);
        const icon = L.divIcon({
            // keep leaflet-div-icon — without it DivIcon hit-testing/drag often breaks
            className: "leaflet-div-icon route-pin",
            html: `<div class="route-pin-hit"><div class="route-pin-inner">${seq}</div></div>`,
            iconSize: [44, 44],
            iconAnchor: [22, 22]
        });
        const marker = L.marker([stop.lat, stop.lng], {
            icon,
            draggable: true,
            autoPan: true,
            autoPanPadding: [40, 40],
            bubblingMouseEvents: false,
            interactive: true
        });
        marker._stopIndex = index;
        if (stop.label) {
            marker.bindTooltip(`${seq}. ${stop.label}`, {
                direction: "top",
                offset: [0, -16],
                sticky: false
            });
        }

        marker.on("dragstart", (e) => {
            el._avtoDragging = true;
            el._avtoMap.dragging.disable();
            L.DomEvent.stopPropagation(e);
            if (marker.getTooltip()) {
                marker.closeTooltip();
            }
        });

        let dragRaf = 0;
        marker.on("drag", (e) => {
            refreshRouteLine(el);
            const ll = e.target.getLatLng();
            if (!el._avtoRouteRef) {
                return;
            }
            // throttle Blazor updates — flood stalls the drag gesture
            if (dragRaf) {
                return;
            }
            dragRaf = requestAnimationFrame(() => {
                dragRaf = 0;
                const cur = e.target.getLatLng();
                el._avtoRouteRef.invokeMethodAsync("OnStopDrag", index, cur.lat, cur.lng);
            });
        });

        marker.on("dragend", (e) => {
            const ll = e.target.getLatLng();
            refreshRouteLine(el);
            el._avtoMap.dragging.enable();
            if (el._avtoRouteRef) {
                el._avtoRouteRef.invokeMethodAsync("OnStopDragEnd", index, ll.lat, ll.lng);
            }
            setTimeout(() => { el._avtoDragging = false; }, 250);
        });

        marker.addTo(el._avtoRouteLayer);
        el._avtoRouteMarkers.push(marker);
        points.push(L.latLng(stop.lat, stop.lng));
    });

    refreshRouteLine(el);

    if (!fit || points.length === 0) {
        return;
    }

    const applyFit = () => {
        el._avtoMap.invalidateSize();
        if (points.length === 1) {
            el._avtoMap.setView(points[0], 14);
            return;
        }

        el._avtoMap.fitBounds(L.latLngBounds(points), {
            padding: [48, 48],
            maxZoom: 14
        });
    };

    applyFit();
    setTimeout(applyFit, 50);
    setTimeout(applyFit, 250);
}
