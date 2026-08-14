let nextId = 1;
const watches = {};

export function startWatch(dotNetRef) {
    const id = nextId++;
    if (!navigator.geolocation) {
        watches[id] = { type: "none" };
        dotNetRef.invokeMethodAsync("OnGeoError", "Геолокация недоступна в этом браузере");
        return id;
    }

    const wid = navigator.geolocation.watchPosition(
        (pos) => {
            const speed = pos.coords.speed != null && !Number.isNaN(pos.coords.speed)
                ? pos.coords.speed * 3.6
                : null;
            dotNetRef.invokeMethodAsync("OnGeo", pos.coords.latitude, pos.coords.longitude, speed);
        },
        (err) => {
            dotNetRef.invokeMethodAsync("OnGeoError", err.message || "Нет доступа к геолокации");
        },
        { enableHighAccuracy: true, maximumAge: 4000, timeout: 20000 }
    );

    watches[id] = { type: "geo", wid };
    return id;
}

export function startDemo(dotNetRef, points) {
    const id = nextId++;
    let i = 0;
    const ping = () => {
        if (!points || points.length === 0) {
            return;
        }
        const p = points[i % points.length];
        dotNetRef.invokeMethodAsync("OnGeo", p.lat, p.lng, 32);
        i += 1;
    };
    ping();
    const timer = setInterval(ping, 2500);
    watches[id] = { type: "demo", timer };
    return id;
}

export function stop(id) {
    const w = watches[id];
    if (!w) {
        return;
    }
    if (w.type === "geo") {
        navigator.geolocation.clearWatch(w.wid);
    }
    if (w.type === "demo") {
        clearInterval(w.timer);
    }
    delete watches[id];
}
