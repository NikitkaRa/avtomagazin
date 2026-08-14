export function haversineKm(lat1, lon1, lat2, lon2) {
    const toRad = (deg) => deg * Math.PI / 180;
    const dLat = toRad(lat2 - lat1);
    const dLon = toRad(lon2 - lon1);
    const a = Math.sin(dLat / 2) ** 2
        + Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
    return 6371 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

export function nearestStop(stops, lat, lng) {
    let best = null;
    let bestKm = Infinity;
    for (const stop of stops) {
        const km = haversineKm(lat, lng, stop.latitude, stop.longitude);
        if (km < bestKm) {
            best = { stop, km };
            bestKm = km;
        }
    }
    return best;
}

export function tileXY(lat, lng, z) {
    const n = 2 ** z;
    const x = Math.floor((lng + 180) / 360 * n);
    const latRad = lat * Math.PI / 180;
    const y = Math.floor((1 - Math.log(Math.tan(latRad) + 1 / Math.cos(latRad)) / Math.PI) / 2 * n);
    return { x, y };
}

export function inReportWindow(plannedIso, now = Date.now()) {
    const planned = new Date(plannedIso);
    if (Number.isNaN(planned.getTime())) {
        return false;
    }
    if (inside(planned, now)) {
        return true;
    }
    const today = new Date(now);
    today.setUTCHours(planned.getUTCHours(), planned.getUTCMinutes(), 0, 0);
    return inside(today, now);
}

function inside(planned, now) {
    const start = planned.getTime() - 15 * 60 * 1000;
    const end = planned.getTime() + 60 * 60 * 1000;
    return now >= start && now <= end;
}

export function windowLabel(plannedIso) {
    const planned = new Date(plannedIso);
    const open = new Date(planned.getTime() - 15 * 60 * 1000);
    const close = new Date(planned.getTime() + 60 * 60 * 1000);
    const fmt = (d) => d.toLocaleTimeString("ru-BY", { hour: "2-digit", minute: "2-digit" });
    return `${fmt(open)} – ${fmt(close)}`;
}
