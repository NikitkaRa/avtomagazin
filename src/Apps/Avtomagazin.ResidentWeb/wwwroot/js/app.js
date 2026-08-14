import * as map from "./map.js";
import { inReportWindow, nearestStop, windowLabel } from "./geo.js";
import {
    deviceToken,
    enqueue,
    isFavorite,
    loadFavorites,
    loadQueue,
    loadSettlement,
    loadSnapshot,
    saveQueue,
    saveSettlement,
    saveSnapshot,
    toggleFavoriteLocal
} from "./store.js";

const FALLBACK_SETTLEMENTS = ["Озеричино", "Правдинский", "Дукора", "Индура", "Озёры", "Скидель"];
const state = {
    settlement: loadSettlement(),
    data: loadSnapshot(),
    source: loadSnapshot() ? "cache" : "bootstrap",
    online: navigator.onLine,
    yandexKey: "",
    nearest: null,
    geoError: "",
    stopMessage: "",
    fitted: false
};

let installEvent = null;
let tilesPrefetched = false;

function $(id) {
    return document.getElementById(id);
}

function hashPath() {
    return (location.hash || "#/").replace(/^#/, "") || "/";
}

function pageFromHash() {
    const hash = hashPath();
    if (hash.startsWith("/stop/")) {
        return "/stop";
    }
    return hash;
}

function stopIdFromHash() {
    const match = hashPath().match(/^\/stop\/([^/]+)/);
    return match?.[1] ?? "";
}

function fmtClock(iso) {
    if (!iso) {
        return "—";
    }
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
        return "—";
    }
    return date.toLocaleTimeString("ru-BY", { hour: "2-digit", minute: "2-digit" });
}

function fmtWhen(iso) {
    if (!iso) {
        return "ещё не обновлялось";
    }
    return new Date(iso).toLocaleString("ru-BY", {
        day: "2-digit",
        month: "2-digit",
        hour: "2-digit",
        minute: "2-digit"
    });
}

function asList(value) {
    return Array.isArray(value) ? value : [];
}

function allStops() {
    return asList(state.data?.routes).flatMap((route) => asList(route.stops));
}

function findStop(id) {
    return allStops().find((stop) => stop.id === id) ?? null;
}

function settlementsFrom(data) {
    const names = allStops().map((stop) => stop.settlementName).filter(Boolean);
    const unique = [...new Set(names)];
    return unique.length ? unique : FALLBACK_SETTLEMENTS;
}

function renderNav() {
    const page = pageFromHash();
    document.querySelectorAll("[data-page]").forEach((section) => {
        section.hidden = section.dataset.page !== page;
    });
    document.querySelectorAll("[data-nav]").forEach((link) => {
        link.classList.toggle("active", link.dataset.nav === page);
    });
}

function renderBanner() {
    const banner = $("status-banner");
    const queue = loadQueue();
    const pending = Boolean(queue.favorites?.length || queue.unfavorites?.length || queue.reports?.length);
    if (state.online && state.source === "live" && !pending) {
        banner.hidden = true;
        banner.textContent = "";
        return;
    }
    banner.hidden = false;
    if (!state.online) {
        banner.textContent = pending
            ? `Нет сети. Расписание с ${fmtWhen(state.data?.syncedAtUtc)}. Избранное и отметки уйдут, когда появится связь.`
            : `Нет сети. Показано последнее расписание (${fmtWhen(state.data?.syncedAtUtc)}).`;
        return;
    }
    banner.textContent = `Сеть есть, но свежие данные не пришли. На экране кэш от ${fmtWhen(state.data?.syncedAtUtc)}.`;
}

function renderChips() {
    const root = $("settlement-chips");
    root.innerHTML = settlementsFrom(state.data).map((name) =>
        `<button class="chip${name === state.settlement ? " on" : ""}" type="button" data-settlement="${name}">${name}</button>`
    ).join("");
}

function renderNearestHint() {
    const hint = $("nearest-hint");
    if (state.nearest) {
        const km = state.nearest.km < 1
            ? `${Math.round(state.nearest.km * 1000)} м`
            : `${state.nearest.km.toFixed(1)} км`;
        hint.textContent = `Ближайшая: ${state.nearest.stop.settlementName} (${km}).`;
        return;
    }
    hint.textContent = state.geoError || "";
}

function renderEta() {
    const root = $("eta-list");
    const vans = asList(state.data?.vehicles);
    const etas = asList(state.data?.eta)
        .filter((item) => !state.settlement || (item.settlementName || "").includes(state.settlement))
        .sort((a, b) => a.minutesUntilArrival - b.minutesUntilArrival)
        .slice(0, 3);

    $("sync-hint").textContent = state.source === "live"
        ? "данные сейчас с сервера"
        : `офлайн / кэш · ${fmtWhen(state.data?.syncedAtUtc)}`;

    const stopCards = (state.settlement
        ? allStops().filter((item) => item.settlementName.includes(state.settlement))
        : allStops().slice(0, 3));

    if (etas.length) {
        root.innerHTML = etas.map((eta) => {
            const van = vans.find((item) => item.id === eta.vehicleId);
            const live = (van?.lastSource || "").toLowerCase() === "driver-app";
            return `<a class="sheet eta-card card-link" href="#/stop/${eta.stopId}">
                <div class="eta-kicker">${live ? "live с машины" : "по графику"}</div>
                <div class="eta-title">${eta.settlementName}</div>
                <p>Через ~${eta.minutesUntilArrival} мин · ${fmtClock(eta.estimatedArrivalUtc)}</p>
                ${van ? `<p class="muted">${van.plateNumber} · ${van.operatorName}</p>` : ""}
            </a>`;
        }).join("");
        return;
    }

    root.innerHTML = stopCards.map((stop) =>
        `<a class="sheet eta-card card-link" href="#/stop/${stop.id}">
            <div class="eta-kicker">${isFavorite(stop.id) ? "в избранном" : "по графику"}</div>
            <div class="eta-title">${stop.settlementName}</div>
            <p>План около ${fmtClock(stop.plannedArrivalUtc)}</p>
        </a>`
    ).join("") || `<section class="sheet"><p>Остановок пока нет.</p></section>`;
}

function renderSchedule() {
    const root = $("schedule-list");
    const routes = asList(state.data?.routes);
    if (!routes.length) {
        root.innerHTML = `<div class="alert">Расписание ещё не сохранялось на этом телефоне.</div>`;
        return;
    }
    root.innerHTML = routes.map((route) => {
        const rows = asList(route.stops)
            .slice()
            .sort((a, b) => a.sequence - b.sequence)
            .map((stop) => `<a class="list-row card-link" href="#/stop/${stop.id}">
                <div>
                    <strong>${stop.settlementName}</strong>
                    <div class="muted">${isFavorite(stop.id) ? "избранное · " : ""}остановка ${stop.sequence}</div>
                </div>
                <div>${fmtClock(stop.plannedArrivalUtc)}</div>
            </a>`)
            .join("");
        return `<section class="sheet"><h2>${route.name}</h2>${rows}</section>`;
    }).join("");
}

function renderFavorites() {
    const root = $("favorites-list");
    const favorites = loadFavorites();
    if (!favorites.length) {
        root.innerHTML = `<section class="sheet"><p>Пока пусто. Откройте остановку и нажмите «В избранное».</p></section>`;
        return;
    }
    root.innerHTML = favorites.map((item) => {
        const stop = findStop(item.id);
        return `<a class="sheet eta-card card-link" href="#/stop/${item.id}">
            <div class="eta-title">${item.settlementName}</div>
            <p class="muted">${stop ? `план ${fmtClock(stop.plannedArrivalUtc)}` : "остановка из кэша"}</p>
        </a>`;
    }).join("");
}

function renderStop() {
    const root = $("stop-detail");
    const stop = findStop(stopIdFromHash());
    if (!stop) {
        root.innerHTML = `<div class="alert">Остановка не найдена в кэше. Откройте, когда будет сеть.</div>`;
        return;
    }

    const favorite = isFavorite(stop.id);
    const open = inReportWindow(stop.plannedArrivalUtc);
    const eta = asList(state.data?.eta).find((item) => item.stopId === stop.id);
    const reportBtns = open
        ? `<div class="row">
                <button class="btn" type="button" data-report="on-site">На месте</button>
                <button class="btn btn-ghost" type="button" data-report="no-show">Не приехала</button>
           </div>
           <p class="tiny muted">Кнопки доступны ${windowLabel(stop.plannedArrivalUtc)}.</p>`
        : `<p class="muted">Отметить приезд можно за 15 минут до плана и час после (${windowLabel(stop.plannedArrivalUtc)}).</p>`;

    root.innerHTML = `
        <section class="hero">
            <p class="eyebrow">Остановка</p>
            <h1>${stop.settlementName}</h1>
            <p class="sub">план ${fmtClock(stop.plannedArrivalUtc)}${eta ? ` · живой ETA ~${eta.minutesUntilArrival} мин` : ""}</p>
        </section>
        <section class="sheet stack">
            <button class="btn ${favorite ? "btn-ghost" : ""}" type="button" id="favorite-btn">
                ${favorite ? "Убрать из избранного" : "В избранное"}
            </button>
            <p class="tiny muted">Избранным уйдёт пуш, когда водитель нажмёт «На месте».</p>
            ${reportBtns}
            ${state.stopMessage ? `<p class="ok">${state.stopMessage}</p>` : ""}
        </section>`;
}

function markersFrom(data) {
    const items = [];
    for (const stop of allStops()) {
        const mine = state.settlement && stop.settlementName.includes(state.settlement);
        const fav = isFavorite(stop.id);
        items.push({
            lat: stop.latitude,
            lng: stop.longitude,
            kind: mine || fav ? "home" : "stop",
            emoji: fav ? "★" : mine ? "⌂" : "•",
            label: stop.settlementName,
            stopId: stop.id,
            popup: `<b>${stop.settlementName}</b><br/>план ${fmtClock(stop.plannedArrivalUtc)}`
        });
    }
    for (const van of asList(data?.vehicles).filter((item) => item.lastLatitude != null)) {
        const live = (van.lastSource || "").toLowerCase() === "driver-app";
        items.push({
            lat: van.lastLatitude,
            lng: van.lastLongitude,
            kind: "van",
            emoji: "🚐",
            label: van.plateNumber,
            popup: `<b>${van.plateNumber}</b><br/>${van.operatorName}<br/>${live ? "сейчас транслирует водитель" : "последняя известная точка"}`
        });
    }
    return items;
}

async function drawMap({ fit = false } = {}) {
    const mode = await map.ensure("resident-map", {
        online: state.online,
        yandexKey: state.yandexKey
    });
    map.setMarkers("resident-map", markersFrom(state.data), { fit: fit || !state.fitted });
    state.fitted = true;
    $("map-hint").textContent = mode === "schematic"
        ? "Схема: куски карты ещё не скачивались. При сети они кэшируются, как в MAUI."
        : mode === "yandex"
            ? "Яндекс.Карты (тайлы Яндекса офлайн не копим)"
            : "OSM: тайлы вокруг остановок копятся на телефоне и открываются без сети.";
}

function render() {
    renderNav();
    renderBanner();
    renderChips();
    renderNearestHint();
    renderEta();
    renderSchedule();
    renderFavorites();
    renderStop();
}

async function loadBootstrap() {
    const response = await fetch("/data/bootstrap.json");
    return response.json();
}

async function refresh() {
    const cached = loadSnapshot();
    try {
        const response = await fetch("/api/snapshot", { cache: "no-store" });
        if (response.ok) {
            const live = await response.json();
            live.syncedAtUtc = live.syncedAtUtc || new Date().toISOString();
            saveSnapshot(live);
            state.data = live;
            state.source = "live";
            state.online = true;
            render();
            await drawMap();
            await maybePrefetch();
            return;
        }
    } catch {
        state.online = false;
    }

    if (cached) {
        state.data = cached;
        state.source = "cache";
    } else {
        try {
            state.data = await loadBootstrap();
        } catch {
            state.data = { vehicles: [], routes: [], eta: [] };
        }
        state.source = "bootstrap";
    }
    render();
    await drawMap();
}

async function maybePrefetch() {
    if (tilesPrefetched || !state.online) {
        return;
    }
    tilesPrefetched = true;
    try {
        await map.prefetchTiles(allStops());
    } catch {
        tilesPrefetched = false;
    }
}

async function syncFavorite(stop, added) {
    const token = deviceToken();
    if (!state.online) {
        const queue = loadQueue();
        const favorites = queue.favorites || [];
        const unfavorites = queue.unfavorites || [];
        if (added) {
            enqueue({
                favorites: [...favorites.filter((item) => item.id !== stop.id), { id: stop.id, settlementName: stop.settlementName }],
                unfavorites: unfavorites.filter((id) => id !== stop.id)
            });
        } else {
            enqueue({
                unfavorites: [...unfavorites.filter((id) => id !== stop.id), stop.id],
                favorites: favorites.filter((item) => item.id !== stop.id)
            });
        }
        return;
    }

    if (added) {
        const response = await fetch("/notifications/api/favorites", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                deviceToken: token,
                platform: "web",
                stopId: stop.id,
                settlementName: stop.settlementName
            })
        });
        if (!response.ok) {
            throw new Error("не удалось сохранить избранное");
        }
        return;
    }

    const response = await fetch(
        `/notifications/api/favorites?deviceToken=${encodeURIComponent(token)}&stopId=${stop.id}`,
        { method: "DELETE" }
    );
    if (!response.ok && response.status !== 404) {
        throw new Error("не удалось убрать из избранного");
    }
}

async function sendReport(stop, kind) {
    const token = deviceToken();
    if (!state.online) {
        const queue = loadQueue();
        enqueue({
            reports: [...(queue.reports || []), { stopId: stop.id, kind }]
        });
        state.stopMessage = kind === "on-site"
            ? "Нет сети. Отметка «на месте» уйдёт, когда появится связь."
            : "Нет сети. Отметка «не приехала» уйдёт, когда появится связь.";
        renderStop();
        renderBanner();
        return;
    }

    const response = await fetch(`/routing/api/stops/${stop.id}/reports`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ kind, deviceToken: token })
    });
    if (response.status === 403) {
        throw new Error("сейчас не окно отметки (за 15 мин до плана и час после)");
    }
    if (!response.ok) {
        throw new Error("не удалось отправить отметку");
    }
    state.stopMessage = kind === "on-site" ? "Спасибо, записали: автолавка на месте." : "Спасибо, записали: не приехала.";
}

async function flushQueue() {
    const queue = loadQueue();
    try {
        for (const item of queue.favorites || []) {
            const stop = findStop(item.id) || item;
            await syncFavorite({ id: item.id, settlementName: stop.settlementName || item.settlementName }, true);
        }
        for (const id of queue.unfavorites || []) {
            const stop = findStop(id) || { id, settlementName: "" };
            await syncFavorite(stop, false);
        }
        for (const item of queue.reports || []) {
            const stop = findStop(item.stopId);
            if (stop) {
                await sendReport(stop, item.kind);
            }
        }
        saveQueue({});
    } catch {
        // keep remaining queue
    }
}

async function onFavoriteToggle() {
    const stop = findStop(stopIdFromHash());
    if (!stop) {
        return;
    }
    const added = toggleFavoriteLocal(stop);
    try {
        await syncFavorite(stop, added);
        state.stopMessage = added
            ? "В избранном: пуш придёт, когда водитель будет на месте."
            : "Убрали из избранного.";
    } catch (error) {
        toggleFavoriteLocal(stop);
        state.stopMessage = error.message;
    }
    render();
}

async function locateNearest() {
    if (!navigator.geolocation) {
        state.geoError = "Браузер не даёт геолокацию.";
        renderNearestHint();
        return;
    }
    try {
        const pos = await new Promise((resolve, reject) => {
            navigator.geolocation.getCurrentPosition(resolve, reject, {
                enableHighAccuracy: false,
                timeout: 8000,
                maximumAge: 120000
            });
        });
        const found = nearestStop(allStops(), pos.coords.latitude, pos.coords.longitude);
        if (!found) {
            state.geoError = "Остановок в кэше нет.";
            renderNearestHint();
            return;
        }
        state.nearest = found;
        state.geoError = "";
        state.settlement = found.stop.settlementName;
        saveSettlement(state.settlement);
        state.fitted = false;
        render();
        await drawMap({ fit: true });
        location.hash = `#/stop/${found.stop.id}`;
    } catch {
        state.geoError = "Геолокация недоступна (часто так по HTTP). Можно выбрать деревню вручную.";
        renderNearestHint();
    }
}

async function loadConfig() {
    try {
        const response = await fetch("/config.json", { cache: "no-store" });
        if (response.ok) {
            const config = await response.json();
            state.yandexKey = (config.yandexMapsKey || "").trim();
        }
    } catch {
        state.yandexKey = "";
    }
}

function bind() {
    $("settlement-chips").addEventListener("click", async (event) => {
        const button = event.target.closest("[data-settlement]");
        if (!button) {
            return;
        }
        state.settlement = button.dataset.settlement;
        saveSettlement(state.settlement);
        state.fitted = false;
        render();
        await drawMap({ fit: true });
    });

    $("nearest-btn").addEventListener("click", locateNearest);

    $("stop-detail").addEventListener("click", async (event) => {
        const report = event.target.closest("[data-report]");
        if (report) {
            const stop = findStop(stopIdFromHash());
            if (!stop) {
                return;
            }
            try {
                await sendReport(stop, report.dataset.report);
            } catch (error) {
                state.stopMessage = error.message;
            }
            renderStop();
            return;
        }
        if (event.target.closest("#favorite-btn")) {
            await onFavoriteToggle();
        }
    });

    window.addEventListener("hashchange", () => {
        state.stopMessage = "";
        render();
        if (pageFromHash() === "/") {
            drawMap();
        }
    });
    window.addEventListener("online", async () => {
        state.online = true;
        await refresh();
        await flushQueue();
    });
    window.addEventListener("offline", async () => {
        state.online = false;
        render();
        await drawMap();
    });
    window.addEventListener("beforeinstallprompt", (event) => {
        event.preventDefault();
        installEvent = event;
        $("install-btn").hidden = false;
    });
    $("install-btn").addEventListener("click", async () => {
        if (!installEvent) {
            return;
        }
        installEvent.prompt();
        await installEvent.userChoice;
        installEvent = null;
        $("install-btn").hidden = true;
    });
}

async function registerWorker() {
    if (!("serviceWorker" in navigator)) {
        return;
    }
    try {
        await navigator.serviceWorker.register("/sw.js");
    } catch {
        // ignore in local http quirks
    }
}

if (!state.settlement) {
    state.settlement = FALLBACK_SETTLEMENTS[0];
}

bind();
render();
await loadConfig();
await registerWorker();
await refresh();
locateNearest();
setInterval(() => {
    if (state.online) {
        refresh();
    }
}, 7000);
