# MappFyren

MappFyren är en Windows-applikation (WPF/.NET 8) för att övervaka **antal undermappar** i flera konfigurerade sökvägar (inkl. nätverksstigar/UNC), visa status i ett GUI och indikera larm via gränsvärden. All konfiguration sker i `settings.json`.

## Funktioner

- Övervakar antal **undermappar** i varje sökväg
- Status per mapp:
  - `Ok`, `TooLow`, `TooHigh`, `Error`
- **Färgkodad** status i listan + statusindikator
- **Klickbar mapp-ikon** per rad som öppnar sökvägen i Windows/Explorer
- Gruppering av mappar:
  - Namngivna grupper (`Groups`)
  - (Valfritt) gemensam grupp (`Shared`)
- Nätverksvänlig monitorering:
  - Begränsad parallellism (skonar filservern)
  - Per-mapp timeout (UI/tick blockeras inte)
  - In-flight-skydd (startar inte nya jobb om en mapp redan räknas)
  - Exponentiell backoff vid fel/timeout (undviker att “hamra” trasiga shares)

## Teknik

- .NET 8 (Windows)
- WPF (GUI)
- MVVM via `CommunityToolkit.Mvvm`
- DI + konfiguration via `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.Configuration.Json`
- Konfig reload: `settings.json` läses med `reloadOnChange`

### Settings
settings.json behöver ligga i samma mapp som MappFyren.exe
Exempel:
```json
{
  "MappFyren": {
    "Monitoring": {
      "IntervalSeconds": 10,
      "Recursive": false,
      "MaxParallelism": 3,
      "PerFolderTimeoutSeconds": 5,
      "ErrorBackoffSeconds": 30
    },
    "Shared": {
      "Name": "Gemensam",
      "Folders": [
        {
          "Id": "shared-inbox",
          "Name": "Gemensam Inbox",
          "Description": "Allmänt inflöde",
          "Path": "\\\\fileserver\\share\\Inbox",
          "Thresholds": { "Min": 0, "Max": 50 }
        }
      ]
    },
    "Groups": [
      {
        "Name": "Ekonomi",
        "Folders": [
          {
            "Id": "ekonomi-1",
            "Name": "Fakturor",
            "Description": "Antal undermappar övervakas",
            "Path": "C:\\Temp\\Ekonomi\\Fakturor",
            "Thresholds": { "Min": 0, "Max": 200 }
          }
        ]
      },
      {
        "Name": "IT",
        "Folders": [
          {
            "Id": "it-logs",
            "Name": "Loggar",
            "Description": "UNC-exempel",
            "Path": "\\\\fileserver\\share\\IT\\Logs",
            "Thresholds": { "Min": 0, "Max": 500 }
          }
        ]
      }
    ]
  }
}
```
### Settings-fält

#### `MappFyren:Monitoring`

| Nyckel | Typ | Standard | Beskrivning |
|---|---:|---:|---|
| `IntervalSeconds` | int | 10 | Hur ofta (sekunder) kontrollen körs. Minst 1 sekund. |
| `Recursive` | bool | false | `false` = räkna undermappar på första nivån. `true` = räkna rekursivt (kan bli tungt på nätverk). |
| `MaxParallelism` | int | 4 | Max antal mappar som räknas samtidigt. Viktigt för nätverk/filservrar. |
| `PerFolderTimeoutSeconds` | int | 5 | Timeout per mapp-räkning. Förhindrar att en seg share blockerar. |
| `ErrorBackoffSeconds` | int | 30 | Basfördröjning vid fel/timeout. Appen använder exponentiell backoff. |

#### `MappFyren:Shared` (valfri)

Gemensam grupp. Om `Shared` saknas kan appen fortfarande fungera (implementationen kan visa en tom grupp eller dölja den).

- `Name` (string): Visningsnamn på gruppen
- `Folders` (array): Lista av mappar (se **Folders** nedan)

#### `MappFyren:Groups`

Lista av namngivna grupper.

- `Name` (string): Gruppens namn
- `Folders` (array): Lista av mappar (se **Folders** nedan)

#### Folders

Varje folder-objekt kan innehålla:

- `Id` (string, **unik**): Identifierare för mappen
- `Name` (string): Visningsnamn
- `Description` (string, valfri): Beskrivning i UI
- `Path` (string): Windows-sökväg eller UNC (t.ex. `\\server\share\...`)
- `Thresholds.Min` (int, valfri): Lägsta gräns för antal undermappar
- `Thresholds.Max` (int, valfri): Högsta gräns för antal undermappar

---

### Hur övervakningen funkar

- Varje intervall räknas undermappar via `Directory.EnumerateDirectories(...)`.
- Appen håller inte permanenta filhandtag/”pekare” öppna mot mapparna (polling, inte watcher).
- Vid nätverksstrul/behörighet/saknad sökväg visas status `Error` med felmeddelande.
- För nätverk används skydd: parallellism, timeout, in-flight och backoff.

> **Not:** `Directory.EnumerateDirectories` kan ibland blocka på OS-nivå vid SMB-problem. Timeouten förhindrar att UI/tick blockeras, men därför är parallellism + in-flight + backoff viktiga för att undvika tråd-/resursproblem.

---

### Rekommendationer för nätverk (UNC)

- Föredra UNC (`\\server\share\...`) framför mappade enheter (`Z:\...`) i installerade/schemalagda scenarion.
- Håll `Recursive=false` på nätverksstigar om möjligt.
- Sätt `MaxParallelism` lågt (t.ex. 2–4) om många shares övervakas.


## Projektstruktur

- `MappFyren.App/` – WPF-applikationen (GUI, DI, ViewModels, Views, Services)
- `MappFyren.Core/` – Domän/konfiguration + monitorering (räkning, status)

### Krav
- Windows 10/11
- .NET 8 SDK (för att bygga/köra lokalt)

### Bygg & kör (utveckling)

Från repo-roten:

```powershell
dotnet build
dotnet run --project .\MappFyren.App\MappFyren.App.csproj
