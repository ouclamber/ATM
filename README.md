# ATM (Bankomat)

Консольное приложение, имитирующее банкомат: вход по карте и PIN, снятие и внесение наличных, переводы, история операций и панель администратора для управления кассетами.
Написано на **C# / .NET 10** по принципам **DDD** с использованием Result pattern и PostgreSQL.

> Учебный проект для портфолио. Цель — на небольшом, но реалистичном предметном примере отработать богатую доменную модель, транзакции, тестирование и CI/CD.

<!-- Добавьте скриншоты или GIF в папку docs/ и раскомментируйте:
![Главное меню](docs/main-menu.png)
![Снятие наличных](docs/withdraw.png)
![Админ-панель](docs/admin.png)
-->

---

## Возможности

**Клиент** (вход по номеру карты и PIN-коду)
- Проверка баланса
- Снятие наличных: банкомат подбирает набор с минимальным числом купюр с учётом остатков в кассетах
- Внесение наличных по номиналам
- Переводы на другую карту
- История операций
- Смена PIN-кода
- Информация о банкомате
**Администратор** (логин и пароль)
- Состояние кассет по номиналам
- Пополнение кассет
- Расчёт выдачи для заданной суммы
---

## Бизнес-правила

| Правило | Значение |
|---------|----------|
| Номер карты | 16 цифр (пробелы и дефисы допускаются) |
| PIN-код | 4 цифры |
| Блокировка карты | после 3 неверных вводов PIN подряд |
| Снятие | сумма кратна 100, только номиналы, которые есть в кассетах |
| Лимит снятия за операцию | 20 000 ₽ |
| Лимит снятия за 24 часа | 50 000 ₽ |
| Лимит внесения за операцию | 50 000 ₽ |
| Лимит перевода за операцию | 100 000 ₽ |
| Номиналы кассет | 5000, 2000, 1000, 500, 100 ₽ |
| Перевод на свою карту | запрещён |
| Внесение | сумма считается банкоматом по купюрам, отрицательные количества и неизвестные номиналы отклоняются |
| Неверный текущий PIN при смене PIN | засчитывается как неудачная попытка (блокировка после 3) |
| Вход администратора | блокировка на 5 минут после 5 неверных паролей подряд |

Лимиты настраиваются в `appsettings.json` (секция `ATM`).

---

## Технологии

| Область | Стек |
|---------|------|
| Язык и платформа | C#, .NET 10 |
| База данных | PostgreSQL, Entity Framework Core 10 (Npgsql), миграции |
| Консольный интерфейс | Spectre.Console |
| Безопасность | BCrypt для PIN-кодов и пароля администратора |
| Логирование | Serilog (консоль и файл с ежедневной ротацией) |
| Тесты | xUnit, FluentAssertions, Moq, coverlet |
| DevOps | GitHub Actions: CI с отчётом о покрытии и релизы self-contained бинарников |

---

## Архитектура

```
ATM/
├── Bankomat/
│   ├── Domain/              # Ядро, не зависит от остальных слоёв
│   │   ├── Accounts/        #   BankAccount (агрегат): баланс, PIN-хеш, блокировка
│   │   ├── Atms/            #   Atm (агрегат), Cassette, план выдачи купюр
│   │   ├── Transactions/    #   Журнал операций
│   │   ├── ValueObjects/    #   Money, CardNumber, PinCode, Denomination
│   │   └── Common/          #   Entity, AggregateRoot, ValueObject, Result, Error
│   ├── Application/         # Сценарии (по одному обработчику на операцию)
│   │   ├── Accounts/        #   AccountSummary (DTO для UI), GetAccountSummaryHandler
│   │   ├── Authentication/  #   LoginHandler, ChangePinHandler
│   │   ├── Operations/      #   WithdrawHandler, DepositHandler, TransferHandler
│   │   ├── AtmManagement/   #   RefillHandler
│   │   ├── Seed/            #   Начальное заполнение БД
│   │   ├── Common/          #   Маскирование карт для логов, обработка конфликтов сохранения
│   │   └── Abstractions/    #   IUnitOfWork, IPinHasher, IBankConfig...
│   ├── Infrastructure/      # EF Core, репозитории, UnitOfWork, BCrypt, конфигурация
│   ├── Presentation/        # Консольные меню (Spectre.Console), точка входа
│   └── Migrations/
├── Bankomat.Tests/          # Unit-тесты + интеграционные тесты с PostgreSQL (Integration/)
├── docs/AUDIT.md            # Отчёт о найденных проблемах и исправлениях
├── docker-compose.yml       # PostgreSQL для локального запуска
└── .github/workflows/       # CI и CD
```

Ключевые решения:

- **Богатая доменная модель.** Агрегаты сами охраняют инварианты. Например, `BankAccount.Debit` не даст списать больше баланса или списать с заблокированной карты, а изменить кассету банкомата можно только через `Atm`.
- **Value objects** (`Money`, `CardNumber`, `PinCode`, `Denomination`) валидируют данные при создании, поэтому невалидное значение невозможно создать.
- **Result pattern.** Бизнес-ошибки (`Validation`, `Business`, `NotFound`, `Forbidden`, `Conflict`) возвращаются как значения, а исключения оставлены для настоящих сбоев.
- **Транзакционность.** Каждая операция (снятие, внесение, перевод) сохраняет все изменения одним `SaveChanges` — это одна транзакция БД.
- **Конкурентность.** Счёт и кассеты защищены оптимистической блокировкой по системной колонке PostgreSQL `xmin`: два банкомата не смогут одновременно списать одни и те же деньги (конфликт возвращается клиенту как ошибка «повторите попытку»).
- **Алгоритм выдачи.** Динамическое программирование (ограниченный размен) вместо жадного алгоритма: нет ложных отказов, когда сумму можно выдать (например, 6000 ₽ при кассетах 5000×1 и 2000×3).
- **UI не держит сущности.** Слой представления получает снимок счёта (`AccountSummary`), а не EF-сущность, и обновляет баланс только из результатов обработчиков.
- **Безопасное логирование.** Номера карт маскируются, PIN в логи не попадает.
---

## Быстрый старт

Полная пошаговая инструкция — ниже. Если всё уже установлено, достаточно трёх команд из корня репозитория:

```bash
docker compose up -d          # PostgreSQL
dotnet run --project Bankomat # миграции и тестовые данные применятся сами
```

### 1. Что установить

| Компонент | Зачем | Проверка |
|-----------|-------|----------|
| [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) | сборка и запуск | `dotnet --version` → `10.0.x` |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | PostgreSQL в контейнере | `docker --version` |
| или PostgreSQL 14+ локально | вместо Docker | `psql --version` |

### 2. Перейти в папку проекта

Windows (PowerShell):

```powershell
cd "C:\Users\HONOR\OneDrive\Desktop\ATM-main\ATM"
```

Linux / macOS:

```bash
cd ~/ATM
```

### 3. Запустить PostgreSQL

**Вариант А — Docker (рекомендуется).** В репозитории есть `docker-compose.yml`:

```bash
docker compose up -d
docker compose ps        # статус должен быть healthy
```

Порт или пароль можно поменять переменными `POSTGRES_PORT` / `POSTGRES_PASSWORD` (тогда поменяйте и строку подключения, см. шаг 4).

**Вариант Б — свой PostgreSQL.** Создавать базу вручную не нужно — приложение создаст её само, если у пользователя есть права. Достаточно знать хост, порт, логин и пароль.

### 4. Строка подключения (если отличается от стандартной)

По умолчанию: `Host=localhost;Port=5432;Database=ATM;Username=postgres;Password=root`.
Если у вас другой пароль, **не правьте `appsettings.json`** (он в git), а задайте переменную окружения:

Windows (PowerShell, только для текущего окна):

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=ATM;Username=postgres;Password=ВАШ_ПАРОЛЬ"
```

Windows (cmd.exe):

```bat
set "ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=ATM;Username=postgres;Password=ВАШ_ПАРОЛЬ"
```

Linux / macOS:

```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=ATM;Username=postgres;Password=ВАШ_ПАРОЛЬ"
```

Или скопируйте шаблон в локальный файл (он в `.gitignore`):

```powershell
Copy-Item Bankomat\appsettings.Development.example.json Bankomat\appsettings.Development.json
```

`appsettings.Development.json` подхватывается, когда `DOTNET_ENVIRONMENT=Development` (так запускают Visual Studio / Rider через `launchSettings.json`). Файл `appsettings.local.json` подхватывается всегда.

### 5. Сборка и запуск

```bash
dotnet restore
dotnet build
dotnet run --project Bankomat
```

При первом запуске автоматически:
1. создаётся база `ATM` и применяются миграции EF Core;
2. создаются банкомат с кассетами и тестовые счета (см. «Тестовые данные»).

Управление — стрелки ↑/↓ и Enter. Дробные суммы вводятся через **запятую** (`1234,56`), так как используется культура ru-RU. Выход — пункт «Выход» или `Ctrl+C`.

### 6. Тесты

```bash
dotnet test
```

Интеграционные тесты с настоящим PostgreSQL (миграции, токены конкурентности, репозитории) запускаются, если задана переменная `BANKOMAT_TEST_CONNECTION` — строка подключения к **серверу** (тест сам создаёт и удаляет временную БД):

```powershell
$env:BANKOMAT_TEST_CONNECTION = "Host=localhost;Port=5432;Username=postgres;Password=root"
dotnet test
```

```bash
BANKOMAT_TEST_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=root" dotnet test
```

Без переменной они помечаются как Skipped, остальные тесты работают без БД.

С отчётом о покрытии:

```bash
dotnet test Bankomat.Tests/Bankomat.Tests.csproj --collect:"XPlat Code Coverage" --settings Bankomat.Tests/coverlet.runsettings
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"Bankomat.Tests/TestResults/**/coverage.cobertura.xml" -targetdir:coveragereport -reporttypes:Html
```

Откройте `coveragereport/index.html`. Текущее покрытие кода (без меню, миграций и EF-конфигураций) — около 88%.

### 7. Сборка готового exe (без установленного .NET у пользователя)

```bash
dotnet publish Bankomat -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
dotnet publish Bankomat -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
```

Рядом с `Bankomat.exe` лежит `appsettings.json` — в нём (или переменной окружения) задаётся строка подключения. Логи пишутся в папку `logs/` в **текущей рабочей папке**.

### 8. Миграции БД (для разработчика)

```bash
dotnet tool install --global dotnet-ef        # один раз
dotnet ef migrations add ИмяМиграции --project Bankomat
dotnet ef database update --project Bankomat  # применить вручную
dotnet ef migrations script --project Bankomat --idempotent -o migrate.sql
```

Автоприменение миграций при старте можно отключить: `Database__ApplyMigrationsOnStartup=false`. Создание тестовых данных: `Seed__Enabled=false`.

### 9. Частые проблемы

| Симптом | Причина и решение |
|---------|-------------------|
| `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine` | Не запущен Docker Desktop. Запустите его из меню «Пуск» (или `docker desktop start`), дождитесь статуса **Engine running**, проверьте `docker version` (должен быть блок `Server`) и повторите `docker compose up -d` |
| `Failed to connect to 127.0.0.1:5432` / «PostgreSQL недоступен» | Не запущен контейнер: `docker compose up -d`, проверить `docker compose ps` |
| `password authentication failed for user "postgres"` | Пароль в строке подключения не совпадает с паролем БД — см. шаг 4 |
| `port is already allocated` при `docker compose up` | Порт 5432 занят локальным PostgreSQL: остановите его или `POSTGRES_PORT=5433` и `Port=5433` в строке подключения |
| `Банкомат с Id=X не найден` | В `ATM:DefaultATMId` указан несуществующий банкомат; по умолчанию должен быть `1` |
| Кракозябры вместо русских букв (старая консоль Windows) | Используйте Windows Terminal или выполните `chcp 65001` перед запуском |
| «Вход временно заблокирован» в админ-панели | 5 неверных паролей подряд → блокировка на 5 минут (или перезапуск приложения) |
| Нужно сбросить БД к исходному состоянию | `docker compose down -v` и `docker compose up -d` — данные и тестовые счета создадутся заново |

---

## Тестовые данные

**Клиентские карты**

| Владелец | Номер карты | PIN | Баланс |
|----------|-------------|-----|-------:|
| Иван Иванов | 1111 2222 3333 4444 | 4321 | 50 000 ₽ |
| Мария Петрова | 5555 6666 7777 8888 | 2006 | 75 000 ₽ |
| Алексей Сидоров | 9999 0000 1111 2222 | 9999 | 120 000 ₽ |

**Начальное состояние банкомата**

| Номинал | Купюр | Вместимость |
|--------:|------:|------------:|
| 5000 ₽ | 100 | 200 |
| 2000 ₽ | 150 | 250 |
| 1000 ₽ | 200 | 300 |
| 500 ₽ | 300 | 400 |
| 100 ₽ | 500 | 1000 |

**Администратор:** логин `admin`, пароль `admin123`.

> Это демонстрационные данные. Для любого реального использования смените PIN-коды и пароль администратора.

### Смена пароля администратора

В конфигурации хранится только BCrypt-хеш. Чтобы получить хеш нового пароля:

```bash
dotnet run --project Bankomat -- --hash "мой-новый-пароль"
```

Вставьте результат в `Admin:PasswordHash` (лучше в `appsettings.local.json` или переменную окружения `Admin__PasswordHash`, чем в `appsettings.json` под git).

---

## Конфигурация

Файл `Bankomat/appsettings.json`:

| Секция | Параметры |
|--------|-----------|
| `ConnectionStrings:DefaultConnection` | Строка подключения к PostgreSQL |
| `Database:ApplyMigrationsOnStartup` | Применять миграции при старте (по умолчанию `true`) |
| `ATM` | `MaxWithdrawalPerTransaction`, `MaxWithdrawalPerDay`, `MaxDepositPerTransaction`, `MaxTransferPerTransaction`, `DefaultATMId` |
| `Seed:Enabled`, `Seed:TestAccounts` | Создавать ли тестовые данные; тестовые карты (номер, PIN, владелец, баланс) |
| `Admin` | `Username`, `PasswordHash` |
| `Serilog` | Логи: в файл `logs/` — всё от Information, в консоль — только ошибки (чтобы не мешать интерфейсу) |

Порядок источников (последующий переопределяет предыдущий): `appsettings.json` → `appsettings.{DOTNET_ENVIRONMENT}.json` → `appsettings.local.json` → переменные окружения (`Секция__Ключ`, например `ConnectionStrings__DefaultConnection`, `ATM__MaxWithdrawalPerDay`). Некорректные лимиты (≤ 0, лимит за операцию больше дневного) останавливают запуск с понятной ошибкой.

---

## CI/CD

| Workflow | Триггер | Что делает |
|----------|---------|------------|
| `ci.yml` | push в `main`, `develop`, pull request | Сборка, unit- и интеграционные тесты (PostgreSQL в service-контейнере), отчёт о покрытии |
| `cd.yml` | тег `v*` | Публикация self-contained single-file сборок (win-x64, linux-x64) и создание GitHub Release |

Чтобы выпустить релиз:

```bash
git tag v1.0.0
git push origin v1.0.0
```

---

## Планы по развитию

- [x] Алгоритм выдачи с учётом ограниченного числа купюр (без ложных отказов)
- [x] Защита от одновременных изменений счёта и кассет (токен конкурентности `xmin`)
- [x] Интеграционные тесты с реальной БД (запускаются при заданной `BANKOMAT_TEST_CONNECTION`, в CI — всегда)
- [x] `docker-compose.yml` для PostgreSQL
- [x] Секреты через переменные окружения / `appsettings.local.json`
- [ ] Администратор: разблокировка карт и управление счетами
- [ ] Журнал действий администратора (кто и когда пополнял кассеты)
- [ ] Разделить журнал входов (аудит) и финансовые операции на разные таблицы

Подробный разбор найденных проблем и внесённых исправлений — в [docs/AUDIT.md](docs/AUDIT.md).
