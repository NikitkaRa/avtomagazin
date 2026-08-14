# Avtomagazin

Платформа цифровых автолавок (РБ): микросервисы на **.NET 10**, Postgres, RabbitMQ/MassTransit, YARP gateway.

Цель — рабочий каркас, который потом стыкуется с GPS-телематикой и гос.контурами («Умный город»), плюс нормальный материал для собесов про микросервисы.

## Архитектура

```
Admin :5200 / Resident :5201 / Driver UI
              │
              ▼
         Gateway (YARP :5100)
              │
┌─────────────┼─────────────┬────────────────┐
▼             ▼             ▼                ▼
Identity   Fleet        Routing       Notifications
:5101      :5102        :5103         :5104
           IGpsProvider IGovIntegration IPushSender
```

| Модуль | Порт | Что делает |
|--------|------|------------|
| **Gateway** | 5100 | единая точка API |
| **Identity** | 5101 | JWT / роли |
| **Fleet** | 5102 | автопарк + GPS |
| **Routing** | 5103 | маршруты, ETA, coverage |
| **Notifications** | 5104 | пуши / подписки |
| **Admin** | 5200 | диспетчерская Blazor |
| **ResidentWeb** | 5201 | кабинет жителя, offline-first PWA |
| **Mobile** | Android / iOS | нативное MAUI: житель, водитель, оператор |

### События (MassTransit)

- `VehiclePositionUpdated` — Fleet → Routing (+опц. другие)
- `StopArrivalEstimated` — Routing считает ETA (пуш с этого события больше не шлётся)
- `DriverArrivedAtStop` — водитель нажал «На месте» → пуш избранным остановки
- `ScheduleChanged` — Routing → Notifications (избранное этой остановки)
- `CoverageVisitRecorded` — Routing → Gov adapter / audit

Database-per-service: `avtomagazin_fleet`, `avtomagazin_routing`, `avtomagazin_notifications`.

## Быстрый старт

### 1. Инфра

```bash
docker compose up -d
```

Postgres `:5432`, RabbitMQ `:5672`, management UI http://localhost:15672 (`guest`/`guest`).

### 2. Сервисы

В отдельных терминалах:

```bash
dotnet run --project src/Services/Identity/Avtomagazin.Identity.Api
dotnet run --project src/Services/Fleet/Avtomagazin.Fleet.Api
dotnet run --project src/Services/Routing/Avtomagazin.Routing.Api
dotnet run --project src/Services/Notifications/Avtomagazin.Notifications.Api
dotnet run --project src/Gateway/Avtomagazin.Gateway
dotnet run --project src/Apps/Avtomagazin.Admin
dotnet run --project src/Apps/Avtomagazin.ResidentWeb
```

UI:
- Житель http://127.0.0.1:5201 — PWA на случай браузера
- Персонал http://127.0.0.1:5200 — диспетчер на десктопе
- Телефон: `./scripts/run-android.sh` (MAUI, Android). На эмуляторе API — `http://10.0.2.2:5100`. На живом телефоне впиши LAN, например `http://192.168.1.7:5100`.

Сценарий для комиссии: `DEMO.md`

### 3. Проверка

```bash
# логин
curl -s http://localhost:5100/identity/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"resident@demo.by","password":"demo"}'

# автолавки
curl -s http://localhost:5100/fleet/api/vehicles

# ETA
curl -s 'http://localhost:5100/routing/api/eta?settlement=Индура'

# подписка на пуши (мок)
curl -s http://localhost:5100/notifications/api/devices/register \
  -H 'Content-Type: application/json' \
  -d '{"deviceToken":"demo-token-1","platform":"ios","settlementName":"Индура"}'
```

Демо-пользователи: `resident@demo.by` / `driver@demo.by` / `operator@demo.by` / `admin@demo.by`, пароль `demo`.

Fleet сам крутит `MockGpsProvider` каждые 15с → публикует позиции → Routing считает ETA → Notifications логирует пуши.

## Куда расширять

1. **GPS** — реализовать `IGpsProvider` под Wialon/Traccar, оставить mock для демо.
2. **Пуши** — `IPushSender` → FCM/APNs.
3. **Гос** — `IGovIntegration` → контракт «Умный город» / OpenAPI от Минсвязи.
4. **Моб** — житель уже PWA; дальше Expo/MAUI, если нужен стор.
5. **Карты** — по умолчанию OSM (точка, без ключа). Яндекс.Карты: ключ в `Maps:YandexApiKey`. Без ключа и без сети — схема остановок, не тайлы.

## Тесты

```bash
# unit + WebApplicationFactory
dotnet test

# Postman/Newman (стек должен быть поднят)
newman run postman/Avtomagazin.postman_collection.json --env-var baseUrl=http://127.0.0.1:5100
# или
./scripts/run-tests.sh
```

Коллекция: `postman/Avtomagazin.postman_collection.json` (можно импортнуть в Postman).

> MassTransit закреплён на **8.3.6** (Apache-2.0). v9 коммерческий — не используем.
> В тестах только **xUnit + NSubstitute** (без FluentAssertions / Xceed).

## Лицензии зависимостей (только free OSS)

| Пакет | Лицензия |
|-------|----------|
| .NET / ASP.NET / EF Core / YARP | MIT |
| MassTransit 8.x | Apache-2.0 |
| Npgsql | PostgreSQL License |
| NSubstitute | BSD-3-Clause |
| xUnit | Apache-2.0 |
| coverlet | MIT |
| Newman (CLI) | Apache-2.0 |
