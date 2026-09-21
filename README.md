# AoE2 Minimal Overlay

Overlay nativo de Windows, minimalista y semitransparente para **Age of Empires II: Definitive Edition**. Es una modernización del proyecto original [Aoe2DEOverlay](https://github.com/kickass-panda/Aoe2DEOverlay), cuya licencia MIT y atribución se conservan en [`LICENSE`](LICENSE).

## Qué muestra

Por cada jugador detectado automáticamente en el último `.aoe2record`:

- nick;
- winrate y victorias/derrotas (1v1 RM; Team RM como fallback);
- Elo 1v1 Random Map;
- Elo Team Random Map;
- las últimas cinco civilizaciones (chips de texto).
- slot de partida y color real de AoE2.

En partidas por equipos el listado se agrupa por el team extraído del replay y, dentro de cada grupo, se ordena por slot. El badge `[slot]` usa el color real del jugador (Blue, Red, Green, Yellow, Cyan, Purple, Gray u Orange), sin inferir el team a partir del color.

Los campos no disponibles se muestran como `—`. El overlay no necesita conocer manualmente el nick del rival.

## Layout HUD compacto y equipos

Cada jugador usa una unidad de dos líneas de aproximadamente **45 px**: una fila principal y una segunda línea mínima para los cinco chips de civilización, que se colapsa por completo cuando no hay historial. El badge, nick, `1v1`, `TG` y `WR · W/L` comparten la misma fila; el nick se recorta con elipsis cuando hace falta, sin ensanchar la ventana. El ancho se ajusta al contenido dentro de un rango de **450–540 px** (antes era fijo en 570 px).

Los jugadores se agrupan explícitamente por el valor de `team` extraído del replay. Cada sección muestra un header discreto `TEAM N · X players`, con una línea tenue y un gap de 5 px entre equipos; dentro de cada sección el orden siempre es por slot ascendente. No se infiere equipo por color y no se etiqueta ally/enemy porque el perfil local no se identifica de forma fiable. En FFA (`team = 0`) se usa el label neutral `PLAYERS`, no el engañoso `TEAM 0`. Un mock 4v4 de ocho jugadores muestra `TEAM 1` (slots 1/3/5/7) y `TEAM 2` (2/4/6/8) para revisar la agrupación con `--mock`.

## Cómo funciona

1. `WatchRecordService` descubre las carpetas `%USERPROFILE%\Games\Age of Empires 2 DE\<id>\savegame\`.
2. Observa `Created`, `Renamed` y `Changed`, elige el replay más reciente y aplica debounce.
3. Reutiliza el lector DEFLATE y la estructura del parser original. El lector focalizado del header extrae nick, profile ID, civilización, slot, color y team sin reescribir el formato completo.
4. Un replay parseado correctamente no vuelve a procesarse por cada escritura. Solo se procesa otro path de replay o un refresh manual. Un archivo aún incompleto tiene cinco intentos acotados (0/1/2/4/8 s).
5. `PlayerStatsService` consulta infraestructura oficial de World's Edge:
   - ratings/W-L: `aoe-api.worldsedgelink.com/.../getPersonalStat`, leaderboards 3 y 4;
   - civilizaciones recientes: `api.ageofempires.com/api/GameStats/AgeII/GetMatchList`.

Estas APIs son públicas pero no documentadas y no ofrecen SLA. No se usa `aoe2.net`; sus endpoints antiguos devolvían 404 durante la migración. El cliente tiene timeout de 10 s, retry solo para timeout/408/429/5xx, caché por profile ID de 15 minutos y stale-if-error de hasta 24 horas en la sesión.

## Ejecutar

Requiere Windows 10/11 para compilar. El publish self-contained no requiere instalar .NET en la máquina de destino.

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release --project .\Aoe2DEOverlay\Aoe2DEOverlay.csproj
```

Modo visual sin AoE2 ni red:

```powershell
dotnet run -c Release --project .\Aoe2DEOverlay\Aoe2DEOverlay.csproj -- --mock
```

### Hotkeys globales

| Hotkey | Acción |
|---|---|
| `Ctrl + Shift + O` | Lock / unlock. Locked activa click-through Win32 real. |
| `Ctrl + Shift + H` | Mostrar / ocultar. |
| `Ctrl + Shift + R` | Releer el último replay y forzar refresh de stats. |
| `Ctrl + Shift + Q` | Cerrar completamente el overlay. |

Al desbloquear aparece un indicador discreto `UNLOCKED`, un botón `×` para cerrar y se puede arrastrar la ventana desde la barra superior. El botón de cierre no inicia un drag. Al cerrar, se guardan settings, se detienen watchers y requests, y se liberan hotkeys. Posición, opacidad, estado locked y hidden se guardan en:

`%LOCALAPPDATA%\AoE2MinimalOverlay\settings.json`

Logs acotados:

`%LOCALAPPDATA%\AoE2MinimalOverlay\logs\overlay-YYYYMMDD.log`

## Publicar

```powershell
dotnet publish .\Aoe2DEOverlay\Aoe2DEOverlay.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

Ejecutable esperado:

`D:\projects\Aoe2DEOverlay\publish\AoE2MinimalOverlay.exe`

Se prioriza el publish multi-file por confiabilidad con WPF. Copiá toda la carpeta `publish`, no solo el `.exe`.

## Validar el parser

El probe usa un replay local; no incluye ni sube replays del usuario:

```powershell
dotnet run -c Release --project .\tools\ReplayProbe\ReplayProbe.csproj
```

Para validar también las APIs:

```powershell
dotnet run -c Release --project .\tools\ReplayProbe\ReplayProbe.csproj -- --stats
```

Para probar que un refresh parcial no borre ratings/civilizaciones stale válidas:

```powershell
dotnet run -c Release --project .\tools\ReplayProbe\ReplayProbe.csproj -- --cache-probe
```

También acepta un path específico como argumento.

## Auditoría y decisiones de migración

Se preservaron el descubrimiento automático de savegames, `FileSystemWatcher`, lectura con `FileShare.ReadWrite`, selección del archivo más reciente, descompresión del header y extracción local de jugadores/profile IDs. Se corrigió el uso original de `HOMEPATH` (podía omitir la unidad) y el offset obsoleto de jugadores para replays actuales.

Se eliminaron AppCenter Analytics/Crashes, telemetría, splash, updater/UpdateManager, scripts de release antiguos, settings de UI obsoletos, vistas originales y Newtonsoft.Json. El proyecto ahora usa .NET 8 WPF y `System.Text.Json` sin dependencias NuGet externas.

## Validación manual dentro de una partida

No se considera probado solo por compilar. En una partida real verificá:

1. que al crearse el nuevo `MP Replay ...aoe2record` pase de `Reading match…` a todos los jugadores correctos;
2. que nick/profile IDs/teams del log coincidan con el lobby;
3. que el archivo no se reprocese repetidamente mientras crece;
4. que `Ctrl+Shift+O` deje pasar el mouse al juego cuando está locked y permita arrastrar cuando está unlocked;
5. que los slots, colores y agrupación por team coincidan con el lobby;
6. que `Ctrl+Shift+H` no robe el foco de AoE2, `Ctrl+Shift+R` actualice una sola vez y `Ctrl+Shift+Q` cierre el proceso;
7. que posición y estados sobrevivan al reinicio y el scaling sea legible a 1080p/4K.

## Limitaciones conocidas

- Las APIs oficiales usadas son endpoints no documentados; ante caída se conservan jugadores y se muestran `—`.
- Los chips usan texto, no emblemas. Agregar assets pequeños de civ es el siguiente paso visual natural.
- Replays single-player/formatos históricos muy antiguos pueden no contener el patrón duplicado de identidad usado por el parser focalizado moderno.
- Hotkeys, foco y click-through requieren validación interactiva dentro de AoE2; el probe no puede probar comportamiento Win32 de usuario final.
