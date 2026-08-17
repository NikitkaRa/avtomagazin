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
            dotNetRef.invokeMethodAsync("OnGeo", pos.coords.latitude, pos.coords.longitude, speed, pos.coords.accuracy ?? null);
        },
        (err) => {
            dotNetRef.invokeMethodAsync("OnGeoError", err.message || "Нет доступа к геолокации");
        },
        { enableHighAccuracy: true, maximumAge: 4000, timeout: 20000 }
    );

    watches[id] = { type: "geo", wid };
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
    delete watches[id];
}
