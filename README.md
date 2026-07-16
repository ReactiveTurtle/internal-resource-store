# internal-resource-store

Внутренний сервис хранения и выдачи бинарных ресурсов для доменных приложений системы.

На текущем этапе сервис работает только с изображениями PNG и JPEG. Он принимает файл от приложения, проверяет и очищает изображение, сохраняет его в собственном файловом хранилище и возвращает внутренний `resourceId`.

## Главный принцип

```text
Доступ пользователя к доменной сущности определяет приложение-владелец.
Доступ приложения к бинарному ресурсу определяет internal-resource-store.
```

Сервис не знает:

- пользователей и их роли;
- гильдии, организации и владельцев;
- типы и идентификаторы доменных сущностей;
- поля, в которых используется ресурс;
- правила доступа доменных приложений.

Сервис знает только ресурс и приложение, которому этот ресурс принадлежит.

## Граница доступа

`internal-resource-store` не должен быть доступен из внешнего мира. Browser и внешние клиенты обращаются только к доменному приложению.

```text
Browser
  -> endpoint доменного приложения
     -> авторизация пользователя
     -> проверка прав и доменного контекста
     -> internal-resource-store с API key приложения
```

Например, preview изображения шаблона профиля должен работать так:

```text
GET /api/member-profile-template/{templateId}/image
```

Приложение проверяет пользователя и шаблон, получает сохранённый `resourceId`, запрашивает bytes во внутреннем сервисе и проксирует их клиенту. Browser не знает адрес сервиса, storage key и API key приложения.

Публичных static URL сервис не создаёт.

## Что хранит приложение

Доменное приложение хранит только внутренний идентификатор ресурса:

```text
templateImageResourceId
```

Приложение не должно хранить:

- URL внутреннего сервиса;
- путь к файлу;
- storage key;
- исходное имя файла пользователя.

## Модель доступа по API key

В сервисе используются два независимых типа ключей.

### Internal API key

`X-Internal-Api-Key` защищает административные endpoints `/internal/*`.

Ключ задаётся в конфигурации:

```json
{
  "InternalApi": {
    "Key": "<secret>"
  }
}
```

Он используется для создания и просмотра application API keys, а также управления системными переменными.

### Application API key

`X-Api-Key` используется доменными приложениями для работы с ресурсами.

- ключ генерируется через `POST /internal/api-keys`;
- raw key возвращается только один раз при создании;
- в PostgreSQL сохраняется только hash;
- ресурс сохраняет hash ключа приложения-владельца;
- другое приложение не может получить, перечислить или удалить чужой ресурс.

`ApiKeys:HashPepper` является постоянным секретом сервиса. Его замена сделает ранее выданные application API keys невалидными.

## Изображения

Поддерживаются:

- `image/png`;
- `image/jpeg`.

При загрузке сервис:

1. Проверяет заявленный MIME type.
2. Декодирует фактическое содержимое через ImageSharp.
3. Отклоняет повреждённые файлы и неподдерживаемые сигнатуры.
4. Повторно кодирует изображение без переноса исходных metadata.
5. Сохраняет очищенную версию в собственный Docker volume.
6. Возвращает `resourceId` и metadata.

Resize и ограничения ширины/высоты сейчас не применяются.

## Жизненный цикл ресурса

### Создание

Ресурс создаётся с owner hash приложения, MIME type, размером, шириной, высотой и датой создания.

### Чтение

Получить bytes или metadata может только приложение, hash API key которого совпадает с владельцем ресурса.

### Soft delete

`DELETE /resources/{resourceId}` выставляет `deleted_at`. Файл сразу не удаляется и ресурс перестаёт выдаваться.

### Purge

Background worker периодически удаляет физические файлы soft-deleted ресурсов. После удаления выставляется `purged_at`, запись в БД остаётся.

Настройки worker хранятся в `system_variables` и меняются без перезапуска:

| Переменная | Значение по умолчанию | Назначение |
|---|---:|---|
| `resource_soft_delete_retention_days` | `30` | Срок хранения soft-deleted файла |
| `resource_cleanup_interval_minutes` | `60` | Интервал запуска очистки |

## API

### Администрирование

Требует header `X-Internal-Api-Key`.

| Метод | Endpoint | Назначение |
|---|---|---|
| `POST` | `/internal/api-keys` | Создать application API key |
| `GET` | `/internal/api-keys` | Получить список ключей без raw key и hash |
| `GET` | `/internal/system-variables` | Получить системные переменные |
| `PUT` | `/internal/system-variables/{key}` | Изменить системную переменную |

### Ресурсы

Требует header `X-Api-Key`.

| Метод | Endpoint | Назначение |
|---|---|---|
| `POST` | `/resources/images` | Загрузить PNG/JPEG |
| `GET` | `/resources?limit=20&offset=0` | Получить активные ресурсы приложения |
| `GET` | `/resources/{resourceId}` | Получить bytes ресурса |
| `GET` | `/resources/{resourceId}/metadata` | Получить metadata |
| `DELETE` | `/resources/{resourceId}` | Выполнить soft-delete |

Пагинация списка ресурсов:

- `limit`: от `1` до `100`, по умолчанию `20`;
- `offset`: от `0`, по умолчанию `0`;
- `total`: общее число активных ресурсов приложения.

### Служебные endpoints

| Метод | Endpoint | Назначение |
|---|---|---|
| `GET` | `/health` | Проверка состояния процесса |
| `GET` | `/swagger` | Swagger UI |

## Примеры запросов

Создать API key приложения:

```bash
curl -X POST http://localhost:32546/internal/api-keys \
  -H "X-Internal-Api-Key: <internal-api-key>" \
  -H "Content-Type: application/json" \
  -d '{"name":"extranet"}'
```

Raw key присутствует только в этом ответе:

```json
{
  "id": "d498c12e-9617-419a-880f-645f6ea06203",
  "name": "extranet",
  "apiKey": "irs_<generated-secret>",
  "createdAt": "2026-07-15T12:00:00Z"
}
```

Загрузить изображение:

```bash
curl -X POST http://localhost:32546/resources/images \
  -H "X-Api-Key: <application-api-key>" \
  -F "file=@image.png;type=image/png"
```

Получить список ресурсов приложения:

```bash
curl "http://localhost:32546/resources?limit=20&offset=0" \
  -H "X-Api-Key: <application-api-key>"
```

Получить файл:

```bash
curl http://localhost:32546/resources/<resource-id> \
  -H "X-Api-Key: <application-api-key>" \
  --output image.png
```

## Данные и изоляция

Сервис использует собственную PostgreSQL schema:

```text
internal_resource_store
```

Таблицы:

- `api_keys`;
- `resources`;
- `system_variables`;
- `__EFMigrationsHistory`.

В записи ресурса хранятся только:

- внутренний `id`;
- `storage_key`;
- `owner_api_key_hash`;
- MIME type;
- размер файла;
- ширина и высота;
- `created_at`, `deleted_at`, `purged_at`.

Доменные пакеты приложений не импортируются. Пересечений с их таблицами и схемами нет.

## Архитектура

Solution построен по принципам DDD и Clean Architecture.

```text
src/
  InternalResourceStore.Domain
  InternalResourceStore.Application
  InternalResourceStore.Infrastructure
  InternalResourceStore.Configuration
  InternalResourceStore.Api
  InternalResourceStore.Migrations
```

Ответственность слоёв:

- `Domain`: сущности, инварианты и переходы состояния.
- `Application`: use cases, ownership и бизнес-проверки.
- `Infrastructure`: EF Core, PostgreSQL, ImageSharp, file storage, hashing и worker.
- `Configuration`: подключение appsettings и типизированные options.
- `Api`: HTTP-контракт, boundary validation, headers и Swagger.
- `Migrations`: отдельная исполняемая программа применения EF migrations и seed системных переменных.

Направление зависимостей:

```text
Api -> Application
Api -> Infrastructure
Api -> Configuration
Infrastructure -> Application -> Domain
Migrations -> Infrastructure + Configuration
Domain -> no dependencies
```

## Локальный deployment

Требования:

- .NET 10 SDK;
- Docker с Compose plugin;
- доступный PostgreSQL.

Deployment не запускает PostgreSQL-контейнер. Connection string передаётся из secrets-файла вне репозитория.

Пример secrets-файла:

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=host.docker.internal;Port=5432;Database=internal_resource_store;Username=internal_resource_store;Password=<password>;GSS Encryption Mode=Disable"
  },
  "InternalApi": {
    "Key": "<internal-api-key>"
  },
  "ApiKeys": {
    "HashPepper": "<long-random-secret>"
  },
  "PublicPort": 32546
}
```

Запуск на Windows, Linux и macOS одинаковый:

```text
dotnet run deployment/deploy.cs -- --target local --secrets-file <path-to-secrets.json>
```

Последовательность deployment:

1. Генерируется ignored-файл `deployment/.generated/appsettings.Production.json`.
2. Собираются образы мигратора и API.
3. Отдельная программа `internal-resource-store-migrations` применяет миграции.
4. API запускается только после успешного завершения мигратора.
5. Файлы ресурсов сохраняются в volume `internal_resource_store_files`.

Swagger после локального запуска:

```text
http://localhost:32546/swagger
```

## Remote deployment

Deployment script умеет передавать проект и generated-конфигурацию на сервер по SSH:

```text
dotnet run deployment/deploy.cs -- \
  --target remote \
  --host deploy@example.com \
  --ssh-key ~/.ssh/id_ed25519 \
  --remote-dir /opt/internal-resource-store \
  --secrets-file /opt/secrets/internal-resource-store.secrets.json
```

Подробнее: [`deployment/README.md`](deployment/README.md).

## Development

Для локальной разработки доступен `docker-compose.yml`, который поднимает API и отдельный PostgreSQL-контейнер:

```bash
docker compose up --build
```

Development volumes:

- `internal_resource_store_files` для бинарных файлов;
- `internal_resource_store_postgres` для данных PostgreSQL.

## Безопасность зависимостей

В `Directory.Build.props` включён NuGet audit прямых и транзитивных пакетов. Уязвимости уровня `low` и выше (`NU1901`-`NU1904`) считаются ошибками сборки.

GitHub Actions выполняет restore, Release build и vulnerability scan для pull request и push в `main`.

Проверка вручную:

```bash
dotnet list internal-resource-store.slnx package --vulnerable --include-transitive
```

## Эксплуатационные требования

- Размещать API только во внутренней сети.
- Не передавать application API keys в browser.
- Хранить secrets-файл вне репозитория.
- Не изменять `ApiKeys:HashPepper` без ротации всех application API keys.
- Резервировать PostgreSQL и файловый volume согласованно.
- Не публиковать `/data/resources` через static file middleware или внешний web server.
