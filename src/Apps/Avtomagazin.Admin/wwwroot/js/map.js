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
        el._avtoClickHandler = null;
        el._avtoRouteRef = null;
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
    el._avtoRouteLayer = L.layerGroup().addTo(map);
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
