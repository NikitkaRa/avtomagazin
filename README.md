# Avtomagazin

Платформа цифровых автолавок (РБ): микросервисы на **.NET 10**, Postgres, RabbitMQ/MassTransit, YARP gateway.

Цель — рабочий каркас, который потом стыкуется с GPS-телематикой и гос.контурами («Умный город»).

## Архитектура

```
ResidentApp / StaffApp / Admin :5200
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
| **Identity** | 5101 | JWT, роли, заявки персонала |
| **Fleet** | 5102 | автопарк + GPS |
| **Routing** | 5103 | маршруты, coverage |
| **Notifications** | 5104 | пуши / подписки |
| **Admin** | 5200 | диспетчерская Blazor (диспетчер и админ) |
| **ResidentApp** | Android / iOS | житель: регистрация сразу, карта, подписка |
| **StaffApp** | Android / iOS | водитель / диспетчер: заявка, вход после подтверждения |

### Роли

| Роль (JWT) | Кто | Как появляется |
|------------|-----|----------------|
| `resident` | Житель деревни | Регистрация в ResidentApp, сразу активен |
| `driver` | Водитель-продавец в автолавке | Регистрация в StaffApp → админ подтверждает |
| `operator` | Диспетчер в офисе / админке | То же, заявка из StaffApp |
| `admin` | Обычно один, подтверждает персонал | Только сидом, саморегистрации нет |

Статусы: `pending` (заявка, JWT не выдаём) → `active` → `disabled`. Логин в `pending`/`disabled` — 403.

### События (MassTransit)

- `VehiclePositionUpdated` — Fleet публикует GPS-точку
- `DriverArrivedAtStop` — водитель нажал «На месте» → пуш избранным остановки
- `ScheduleChanged` — Routing → Notifications (избранное этой остановки)
- `CoverageVisitRecorded` — Routing → Gov adapter / audit

Database-per-service: `avtomagazin_identity`, `avtomagazin_fleet`, `avtomagazin_routing`, `avtomagazin_notifications`.

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
```

Или `./scripts/start-dev.sh`.

UI:
- Персонал http://127.0.0.1:5200 — диспетчер и админ, вход email/пароль
- Телефон: `./scripts/run-android.sh resident` или `staff`. Dev сам ходит на локальный API (эмулятор `10.0.2.2:5100`, устройство — `adb reverse` на `127.0.0.1:5100`). URL руками не вводится.

### Среды

| Конфиг | Клиенты | API |
|--------|---------|-----|
| **Debug** (Development) | локальный gateway `:5100` | `ASPNETCORE_ENVIRONMENT=Development` |
| **Staging** | `https://api.staging.avtomagazin.by` | `ASPNETCORE_ENVIRONMENT=Staging` |
| **Release** (Production) | `https://api.avtomagazin.by` | `ASPNETCORE_ENVIRONMENT=Production` |

```bash
dotnet build src/Apps/Avtomagazin.ResidentApp/Avtomagazin.ResidentApp.csproj -c Staging -f net10.0-android
dotnet run --project src/Apps/Avtomagazin.Admin --launch-profile staging
```

### 3. Проверка

```bash
# регистрация жителя — сразу JWT
curl -s http://localhost:5100/identity/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"marina@example.com","password":"secret12","name":"Марина","client":"resident"}'

# заявка водителя — без JWT, ждёт админа
curl -s http://localhost:5100/identity/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"petr@example.com","password":"secret12","name":"Пётр","client":"staff","staffRole":"driver"}'

# логин (в Development есть fixture-аккаунты *@test.local / testpass1)
curl -s http://localhost:5100/identity/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"resident@test.local","password":"testpass1"}'

# автолавки
curl -s http://localhost:5100/fleet/api/vehicles

# подписка на пуши (нужен JWT жителя)
curl -s http://localhost:5100/notifications/api/devices/register \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"deviceToken":"device-token-1","platform":"ios","settlementName":"Индура"}'
```

В Development автоматически создаются fixture-пользователи (пароль `testpass1`): `resident@test.local`, `driver@test.local`, `seller@test.local`, `operator@test.local`, `admin@test.local`. В Production — только bootstrap-админ из `.env`.

Новая регистрация: житель (`client=resident`) входит сразу; водитель и диспетчер (`client=staff`, `staffRole=driver|operator`) ждут `POST /identity/api/users/{id}/approve` от админа. Водителю при подтверждении нужна автолавка. Пароль от 8 символов.

Локально Fleet крутит `MockGpsProvider` каждые 15с. В Production мок выключен: точку шлёт телефон водителя.

## Выкат

```bash
cp .env.example .env   # ADMIN_EMAIL, ADMIN_PASSWORD; пустые ключи скрипт сам сгенерит
./scripts/up-prod.sh
```

API `:8080`, админка `:8081`. Postgres/Rabbit наружу не торчат. TLS — Cloudflare Full на эти порты.

## Куда расширять

1. **GPS** — реализовать `IGpsProvider` под Wialon/Traccar; mock остаётся для локальной разработки.
2. **Пуши** — `IPushSender` → FCM/APNs.
3. **Гос** — `IGovIntegration` → контракт «Умный город» / OpenAPI от Минсвязи.
4. **Моб** — два бинарника MAUI (житель / персонал), общая `MauiShared` + `ApiClient`.
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
