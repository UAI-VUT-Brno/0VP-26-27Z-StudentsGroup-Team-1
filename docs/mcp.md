# MCP pro čtení úkolů

Model Context Protocol (MCP) umožňuje klientovi objevit a volat nástroje aplikace. ToDo API poskytuje dva nástroje pro čtení přes Streamable HTTP na `http://localhost:5258/mcp`. Spusťte API z kořene repozitáře příkazem `dotnet run --project src/UAI.ZeroVP.API` a použijte tuto adresu v klientovi MCP.

| Nástroj | Parametry | Výsledek |
|---|---|---|
| `list_todos` | Volitelné `isCompleted` (boolean) a `userId` (řetězec). | Pole úkolů se stejnými filtry a pořadím jako `GET /api/todos`. |
| `get_todo` | Povinné `id` (řetězec ve formátu GUID). | Jeden úkol. Neplatné nebo neznámé ID vrátí výsledek nástroje s `isError: true`. |

Úspěšný výsledek obsahuje JSON v textovém bloku MCP. Příklad pro `get_todo`:

```json
{
  "id": "b30c089b-a8cf-4891-8a20-cb8a6df04127",
  "userId": "demo",
  "title": "Vyzkoušet MCP",
  "description": null,
  "priority": "Normal",
  "isCompleted": false,
  "dueAtUtc": null,
  "createdAtUtc": "2026-11-09T08:00:00+00:00",
  "completedAtUtc": null
}
```

MCP je standardně zapnuté v prostředí Development a vypnuté v Production. Pro přepnutí použijte nastavení `Mcp:Enabled` nebo proměnnou prostředí `Mcp__Enabled`. Vypnutý endpoint vrací `404`.

**Pouze pro místní vývoj s testovacími daty.** MCP neověřuje uživatele a každý klient může vypsat všechny úkoly. Endpoint nevystavujte veřejně.
