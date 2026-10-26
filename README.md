# UAI.ZeroVP – ukázkové API pro výuku Dockeru

Jednoduché REST API pro správu úkolů (ToDo), rozdělené do tří projektů.
Slouží jako výchozí bod pro navazující lekce o kontejnerizaci.

## Struktura repozitáře

```text
UAI.ZeroVP.sln                 solution – zůstává v kořeni
src/                           zdrojový kód aplikace
├─ UAI.ZeroVP.DTO/             kontrakt API
├─ UAI.ZeroVP.Data/            entita a paměťové úložiště
└─ UAI.ZeroVP.API/             webové API, kontrolery, mapování
case-study-a-dockerfile/       výuka – vrstvení Dockerfile
case-study-b-tooling/          výuka – nástroje a workflow
case-study-c-configuration/    výuka – konfigurace aplikace
```

Aplikace i výukové materiály sdílejí jeden kontext, proto se **sestavuje vždy
z kořene repozitáře** – jak `dotnet build`, tak `docker build`. Podrobnosti
v sekci [Výukové materiály k Dockeru](#výukové-materiály-k-dockeru).

| Projekt | Typ | Obsah | Závislosti |
|---|---|---|---|
| `UAI.ZeroVP.DTO` | Class library | Kontrakt API – `TodoItemDto`, `CreateTodoItemRequest`, `CreateRepeatingTodoItemRequest`, `UpdateTodoItemRequest`, `InstanceInfoDto`, výčet `TodoPriority` | žádné |
| `UAI.ZeroVP.Data` | Class library | Entita `TodoItem`, rozhraní `ITodoRepository`, paměťová implementace `InMemoryTodoRepository`, ukládání do souboru JSON, registrace do DI | `DTO` |
| `UAI.ZeroVP.API` | ASP.NET Core Web API | Kontrolery, mapování entita ↔ DTO, konfigurace pipeline | `DTO`, `Data` |

Závislosti tvoří jednosměrný řetěz **DTO → Data → API**. Kontroler zná jen rozhraní
`ITodoRepository`, takže výměna paměťového úložiště za databázi se ho nedotkne.

## Úložiště dat

Za běhu drží API úkoly v operační paměti procesu (`ConcurrentDictionary`).
Při zastavení aplikace je uloží do souboru JSON a při dalším startu je z něj znovu načte,
takže restart přežijí. Výchozí soubor je `data/todos.json` vůči kořeni aplikace;
cestu mění nastavení `Todo:Storage:FilePath` (v kontejneru `Todo__Storage__FilePath`).
Prázdná cesta ukládání vypne a data žijí jen v paměti.
Podrobnosti v [docs/persistence.md](docs/persistence.md).

Soubor si drží každá instance zvlášť, takže v kontejneru bez svazku se data
s odstraněním kontejneru ztratí a repliky nemají společná data. Navazuje se na to:
[case study A](case-study-a-dockerfile/) to ukazuje v příkladech 4 a 5 (restart
kontejneru, replikace za nginx), [case study B](case-study-b-tooling/05-compose/)
pak pojmenovaný svazek, který odstranění kontejneru přežije.

Když soubor ještě neexistuje (nebo je poškozený), vkládají se při startu čtyři ukázkové úkoly. Vypnout je lze nastavením:

```jsonc
// appsettings.json
"Todo": { "SeedSampleData": false }
```

nebo proměnnou prostředí `Todo__SeedSampleData=false` (dvojité podtržítko = oddělovač sekcí).

## Spuštění

```bash
dotnet run --project src/UAI.ZeroVP.API
```

API naslouchá na `http://localhost:5258`. Kořen `/` přesměruje na Swagger UI
(nebo na seznam úkolů, pokud je Swagger vypnutý).

## Endpointy

| Metoda | Cesta | Popis |
|---|---|---|
| `GET` | `/api/todos` | Seznam úkolů; volitelný filtr `?isCompleted=true\|false` |
| `GET` | `/api/todos/{id}` | Detail úkolu |
| `POST` | `/api/todos` | Založení úkolu → `201 Created` + hlavička `Location` |
| `POST` | `/api/todos/repeat` | Založení týdně se opakujícího úkolu → `201 Created` se všemi výskyty |
| `PUT` | `/api/todos/{id}` | Úplná aktualizace úkolu |
| `POST` | `/api/todos/{id}/complete` | Označení úkolu za dokončený |
| `DELETE` | `/api/todos/{id}` | Smazání úkolu → `204 No Content` |
| `GET` | `/api/info` | Hostname instance, prostředí, doba běhu, počet úkolů v paměti |
| `GET` | `/health` | Kontrola zdraví – použije ji `HEALTHCHECK` v Dockerfile |
| `GET` | `/swagger` | Interaktivní dokumentace Swagger UI |
| `GET` | `/openapi/v1.json` | Popis API ve formátu OpenAPI |
| `GET` | `/openapi/v1.yaml` | Stejný popis API ve formátu YAML |

Chybové odpovědi mají jednotný tvar `ProblemDetails` (RFC 9457).
Připravené požadavky k odzkoušení jsou v souboru `src/UAI.ZeroVP.API/UAI.ZeroVP.API.http`.

### Týdně se opakující úkol

`POST /api/todos/repeat` přijme běžná pole úkolu (`title`, `description`, `priority`) a navíc:

- `firstDueAtUtc` – termín prvního výskytu ve formátu ISO 8601, např. `2026-10-26T09:00:00Z`;
  hodnota bez časové zóny se bere jako UTC,
- `repeatCount` – počet **dalších** opakování po prvním výskytu, 1–52.

`repeatCount: 3` tedy vytvoří čtyři úkoly, každý o sedm dní později. Odpověď `201 Created`
obsahuje všechny výskyty seřazené podle termínu, hlavička `Location` odkazuje na první z nich.
Při chybě ve vstupu vrátí API `400 Bad Request` a nevytvoří žádný úkol.

Výskyty jsou samostatné úkoly, ne trvalé pravidlo opakování: dokončení nebo smazání jednoho
se ostatních nedotkne. Každý úkol má v odpovědi pole `dueAtUtc`; úkoly založené přes
`POST /api/todos` termín nemají (`null`).


## Swagger

Dokument OpenAPI 3.1 generuje vestavěná knihovna .NET 10 (`Microsoft.AspNetCore.OpenApi`)
a vystavuje ho na `/openapi/v1.json` a ve formátu YAML na `/openapi/v1.yaml`.
Balíček `Swashbuckle.AspNetCore.SwaggerUI` nad ním přidává jen interaktivní uživatelské
rozhraní na `/swagger` – druhý generátor dokumentu se tedy nepoužívá.

Popisy operací a vlastností ve Swaggeru pocházejí z XML komentářů v kódu;
proto mají všechny tři projekty zapnuté `<GenerateDocumentationFile>`.
Jak se dokument generuje, na co si dát pozor a co ověřují testy, popisuje
[docs/openapi.md](docs/openapi.md).

Swagger je zapnutý v prostředí Development a vypnutý v Production.
Přepnout ho lze nastavením nebo proměnnou prostředí:

```jsonc
// appsettings.json
"Swagger": { "Enabled": true }
```

```bash
# například při spuštění kontejneru
ASPNETCORE_ENVIRONMENT=Production Swagger__Enabled=true dotnet UAI.ZeroVP.API.dll
```

### Rychlý test

```bash
curl http://localhost:5258/api/todos
curl -X POST http://localhost:5258/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title":"Napsat Dockerfile","priority":"High"}'
curl -X POST http://localhost:5258/api/todos/repeat \
  -H "Content-Type: application/json" \
  -d '{"title":"Týdenní report","firstDueAtUtc":"2026-10-26T09:00:00Z","repeatCount":3}'
curl http://localhost:5258/api/info
```

## Poznámka ke kódování

Zdrojové soubory jsou uloženy jako **UTF-8 s BOM**. Bez BOM dekóduje kompilátor C#
na Windows zdrojový kód systémovou ANSI kódovou stránkou a diakritika v řetězcích
se rozbije. Pravidlo hlídá `.editorconfig` (`charset = utf-8-bom`).

## Výukové materiály k Dockeru

Nad touto aplikací jsou postavené tři nezávislé case studies, každá o pěti příkladech.
Každý příklad má zadání pro studenta a komentované řešení. Náročnost stoupá
od case study A k case study C.

| Case study | Úroveň | Téma |
|---|---|---|
| [A – vrstvení Dockerfile](case-study-a-dockerfile/) | začátečníci | jeden Dockerfile, který se postupně zlepšuje: od 1,34 GB k zpevněnému image za 352 MB |
| [B – nástroje a workflow](case-study-b-tooling/) | mírně pokročilí | životní cyklus, diagnostika rozbitých kontejnerů, vrstvy, registry |
| [C – konfigurace aplikace](case-study-c-configuration/) | pokročilí | jeden image, mnoho prostředí: proměnné, soubory, priorita zdrojů, tajemství |

Materiály case studies jsou psané **anglicky**; samotná aplikace a tento soubor zůstávají v češtině.

Každá case study končí příkladem s Docker Compose. Všechny tři jsou samostatné –
lze je probírat v libovolném pořadí nebo jen některý.

### Společné pro všechny case studies

Sestavuje se vždy **z kořene projektu**, Dockerfile se uvádí přepínačem `-f`:

```bash
docker build -f case-study-a-dockerfile/02-multi-stage-build/Dockerfile -t zerovp:a02 .
```

Ignorovací soubory jsou uložené vedle jednotlivých Dockerfile jako
`<jméno Dockerfile>.dockerignore`, protože všechny příklady sdílejí jeden kontext.
V běžném projektu patří `.dockerignore` do kořene repozitáře.
