# SoftUpdater

Система автообновления десктопного ПО через приватный GitLab.

Состоит из трёх частей:
- **soft-updater-api** — микросервис-прокси между клиентами и приватным GitLab
- **soft-updater-lib** — клиентская библиотека (.NET), встраивается в десктопное приложение
- **soft-updater-installer** — нативный инсталлер (Rust), зашит в библиотеку и выполняет саму замену файлов

---

## Архитектура

```
Десктоп (soft-updater-lib)
        │
        │  X-Api-Key
        ▼
soft-updater-api (публичный)
        │
        │  PRIVATE-TOKEN
        ▼
GitLab (приватный)
  └── releases
        ├── assets.links  ← билд (zip)
        └── description   ← changelog (markdown)
```

---

## soft-updater-api

Minimal API на ASP.NET Core 8. Хранит GitLab токен на своей стороне — клиенты его никогда не видят. Каждый клиент идентифицируется по `X-Api-Key`, который привязан к конкретному GitLab проекту.

### Эндпоинты

| Метод | URL | Описание |
|---|---|---|
| `GET` | `/api/updates/latest?currentVersion=1.2.3` | Последний релиз. `204` — версия актуальна |
| `GET` | `/api/updates/changelog?from=1.2.3` | Все релизы новее указанной версии |
| `GET` | `/api/updates/download/{version}` | Скачать zip-архив релиза |
| `GET` | `/api/apps` | Статус всех приложений *(только master key)* |
| `GET` | `/api/health` | Доступность сервиса и GitLab |

### Аутентификация

Каждый запрос требует заголовок `X-Api-Key`.

- **App key** — привязан к одному проекту, выдаётся на каждое приложение и зашивается в его сборку
- **Master key** — видит все проекты, используется для отладки и мониторинга

### Конфигурация

```json
{
  "GitLab": {
    "ApiUrl": "https://your-gitlab.com/api/v4",
    "Token": "",
    "IgnoreSslErrors": false
  },
  "Auth": {
    "MasterKey": "",
    "Keys": {
      "ключ-приложения": 123
    }
  }
}
```

Секреты через переменные окружения:

```bash
GitLab__Token=glpat-xxx
Auth__MasterKey=my-master-secret
Auth__Keys__my-app-key=123
```

### Запуск

```bash
dotnet run --project soft-updater-api
# Scalar UI: http://localhost:5000/
```

```bash
GITLAB_TOKEN=glpat-xxx MASTER_KEY=secret docker compose up -d
```

---

## soft-updater-lib

Клиентская библиотека для встраивания в десктопное приложение. Проверяет
обновления, качает архив и changelog, и запускает нативный инсталлер.

### Пайплайн обновления

```
1. Запуск приложения
   └── StartAsync()  → разовая проверка + фоновый цикл
         ├── 204 → тихо, ничего не делать
         └── 200 → событие UpdateAvailable (показать пользователю диалог)

2. Фоновая проверка каждые CheckInterval (по умолчанию 5 минут)

3. Пользователь согласился → ApplyUpdateAsync(update)
   ├── Параллельно:
   │     ├── [A] Скачивание архива релиза   GET /api/updates/download/{version}
   │     └── [B] Сборка changelog            GET /api/updates/changelog?from={version}
   │               → пишется releaseNotes.md рядом с exe
   │
   └── После завершения [A]:
         └── Запуск нативного инсталлера (soft-updater-installer),
             после чего приложение завершает само себя.
```

### Механизм установки (soft-updater-installer)

Библиотека достаёт зашитый в неё нативный бинарь, кладёт рядом с exe и
запускает его, передавая аргументы. Сразу после запуска инсталлера
**приложение завершает себя** — пока оно живо, его файлы залочены и
заменить их нельзя (особенно на Windows).

Инсталлер — отдельный процесс. Он:
1. ждёт завершения хост-процесса по PID (или убивает его по таймауту);
2. распаковывает архив в целевую директорию — **корень-в-корень**
   (что в корне архива, то и в корне директории, без обёртки-папки);
3. удаляет временный архив;
4. перезапускает приложение.

Аргументы инсталлера:

```text
soft-updater-installer \
    --archive  <путь к скачанному zip> \
    --target   <директория установки> \
    --restart  <что запустить после обновления> \
    --wait-pid <PID хост-процесса> \
    --wait-timeout-secs <сколько ждать смерти хоста; по умолчанию 30>
```

### Инициализация

```csharp
var updater = new SoftUpdater(new UpdaterConfig
{
    ServiceUrl     = "https://updater.your-company.com",
    ApiKey         = "ключ-зашитый-в-сборку",
    CurrentVersion = "1.2.3",                 // или null — возьмётся из Assembly
    CheckInterval  = TimeSpan.FromMinutes(5),
});

updater.UpdateAvailable += async info =>
{
    // Показать пользователю — на усмотрение автора приложения.
    // info.Version, info.ChangelogMarkdown доступны здесь.
    await updater.ApplyUpdateAsync(info);     // когда пользователь согласился
};

updater.DownloadProgress += percent => { /* прогресс 0..100 */ };

await updater.StartAsync();
```

Также доступны `CheckOnceAsync()` — разовая проверка без фонового цикла,
и `GetVersionsAsync(page, pageSize)` — список версий с пагинацией.

---

## Тесты

### soft-updater-installer (Rust)

```bash
cd soft-updater-installer
cargo test
```

Покрыто:
- **распаковка** — файлы из корня и вложенных папок ложатся куда надо;
  существующий файл перезаписывается; вредный архив (`../`) отбивается;
- **ожидание процесса** — сам завершился / убит по таймауту;
- **сквозной прогон** — на настоящем бинаре: старый файл → архив с новым →
  мусорный процесс → инсталлер → файл заменён, архив удалён, рестарт сработал.

Тесты кроссплатформенные (Windows и Linux/Mac).

### soft-updater-api / soft-updater-lib (.NET)

```bash
dotnet test
```

---

## Сборка нативного инсталлера

Бинарь зашивается в библиотеку как EmbeddedResource. Пересобрать под обе
платформы:

```bash
cd soft-updater-installer
./build.sh        # кладёт win-x64 и linux-x64 в soft-updater-lib/Resources/
```

---

## Структура репозитория

```
/
├── soft-updater.sln
├── compose.yaml
│
├── soft-updater-api/            ← микросервис
│   ├── Endpoints/
│   │   ├── UpdatesEndpoints.cs
│   │   ├── AppsEndpoints.cs
│   │   └── HealthEndpoints.cs
│   ├── ApiKeyService.cs
│   ├── GitLabService.cs
│   ├── Model.cs
│   ├── Program.cs
│   ├── Dockerfile
│   └── appsettings.json
│
├── soft-updater-lib/            ← клиентская библиотека (.NET)
│   ├── Installer/InstallerRunner.cs
│   ├── Services/
│   │   ├── UpdaterClient.cs
│   │   └── ChangelogWriter.cs
│   ├── Models.cs
│   ├── SoftUpdater.cs
│   └── UpdaterConfig.cs
│
└── soft-updater-installer/      ← нативный инсталлер (Rust)
    ├── src/
    │   ├── main.rs              ← обёртка: разобрать аргументы → run
    │   ├── lib.rs
    │   ├── args.rs
    │   ├── extract.rs
    │   ├── wait.rs
    │   └── bin/test_helper.rs   ← пустышка для тестов
    ├── tests/
    ├── Cargo.toml
    └── build.sh
```

---

## Как подготовить релиз в GitLab

1. Собрать билд, упаковать в zip — **сборка в корне архива** (exe лежит на верхнем уровне, без обёртки-папки)
2. Создать GitLab Release с тегом версии (например `2.4.7`)
3. Прикрепить zip как **release link** (`assets → links`) — именно он будет скачан клиентами
4. В описании релиза написать changelog в markdown — он уйдёт в `releaseNotes.md` на машине пользователя

> Исходники которые GitLab прикладывает автоматически (`assets → sources`) игнорируются — сервис всегда берёт первый `link`.


# TODO

- Возможность резолвить платформу при поиске ассетов релиза