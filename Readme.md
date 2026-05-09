# SoftUpdater

Система автообновления десктопного ПО через приватный GitLab.

Состоит из двух частей:
- **UpdateService** — микросервис-прокси между клиентами и приватным GitLab
- **UpdaterLib** — клиентская библиотека, встраивается в десктопное приложение *(в разработке)*

---

## Архитектура

```
Десктоп (UpdaterLib)
        │
        │  X-Api-Key
        ▼
UpdateService (публичный)
        │
        │  PRIVATE-TOKEN
        ▼
GitLab (приватный)
  └── releases
        ├── assets.links  ← билд (zip)
        └── description   ← changelog (markdown)
```

---

## UpdateService

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
dotnet run
# Scalar UI: http://localhost:5000/
```

```bash
GITLAB_TOKEN=glpat-xxx MASTER_KEY=secret docker compose up -d
```

---

## UpdaterLib

Клиентская библиотека для встраивания в десктопное приложение.

> **TODO:** Реализация библиотеки запланирована после финализации API.

### Пайплайн обновления

```
1. Запуск приложения
   └── CheckForUpdateAsync(currentVersion)
         ├── 204 → тихо, ничего не делать
         └── 200 → показать пользователю диалог с новой версией

2. Фоновая проверка каждые 5 минут (повторяет шаг 1)

3. Пользователь нажимает "Обновить"
   ├── Параллельно запускаются два процесса:
   │     ├── [A] Скачивание архива релиза
   │     │       GET /api/updates/download/{version}
   │     │
   │     └── [B] Сборка changelog
   │               GET /api/updates/changelog?from={currentVersion}
   │               → создаётся/перезаписывается releaseNotes.md рядом с exe
   │               → туда пишутся все changelog'и от текущей до новой версии
   │
   └── После завершения [A]:
         └── Процесс установки  ← TODO: описать механизм
               (вторичный процесс / консоль)
               ├── Дождаться завершения основного приложения
               ├── Распаковать архив поверх текущей директории
               ├── Запустить приложение заново
               └── Удалить временный архив
```

### Инициализация *(планируемый API)*

```csharp
var updater = new SoftUpdater(new UpdaterConfig
{
    ServiceUrl:     "https://updater.your-company.com",
    ApiKey:         "ключ-зашитый-в-сборку",
    CurrentVersion: "1.2.3",
    CheckInterval:  TimeSpan.FromMinutes(5),
});

updater.UpdateAvailable += async (info) =>
{
    // Показать пользователю — на усмотрение автора приложения
    // info.Version, info.ChangelogMarkdown доступны здесь
};

await updater.StartAsync();
```

---

## Структура репозитория

```
/
├── UpdateService/          ← микросервис
│   ├── Endpoints/
│   │   ├── UpdatesEndpoints.cs
│   │   ├── AppsEndpoints.cs
│   │   └── HealthEndpoints.cs
│   ├── ApiKeyService.cs
│   ├── GitLabService.cs
│   ├── Models.cs
│   ├── Program.cs
│   ├── Dockerfile
│   └── docker-compose.yml
│
└── UpdaterLib/             ← клиентская библиотека (TODO)
```

---

## Как подготовить релиз в GitLab

1. Собрать билд, упаковать в zip
2. Создать GitLab Release с тегом версии (например `2.4.7`)
3. Прикрепить zip как **release link** (`assets → links`) — именно он будет скачан клиентами
4. В описании релиза написать changelog в markdown — он уйдёт в `releaseNotes.md` на машине пользователя

> Исходники которые GitLab прикладывает автоматически (`assets → sources`) игнорируются — сервис всегда берёт первый `link`.