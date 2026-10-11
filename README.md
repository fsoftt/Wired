# Wired 🎧

Reproductor de música multiplataforma hecho con **.NET MAUI** y **MVVM**.
Desde el buscador eliges una **fuente (plugin)**, buscas canciones y las descargas
a tu biblioteca para escucharlas sin conexión.

Wired es un **reproductor basado en plugins**: la app no trae ninguna fuente de música incluida. Las fuentes
se instalan como plugins ([Crear un plugin](#crear-un-plugin-nuevo), [Plugins externos](#plugins-externos)).
Sin fuentes instaladas la app funciona igual como reproductor de tu biblioteca local; el buscador y el Inicio
muestran un aviso en lugar de resultados.

## Funcionalidades

- 🏠 **Inicio**: *Tendencias* (los tops de las fuentes que los ofrecen) y *Para ti* (música parecida a partir de tus
  Me gusta y tus últimas canciones). Se guardan en el teléfono: no se vuelven a pedir hasta pasadas 6 h
  (12 h *Para ti*) o hasta que deslices para actualizar, y si la fuente pide frenar sigues viendo lo guardado.
- 🔎 **Buscar** con la fuente elegida (selector de plugins en forma de chips).
- 📡 **Streaming**: toca un resultado para escucharlo al instante sin guardarlo
  (si la fuente lo permite; plugins que implementan `IStreamableMusicSource`).
- ⬇️ **Cola de descargas** en segundo plano (2 en paralelo), con progreso, cancelar y reintentar.
- 📚 **Biblioteca** local con filtro, orden (recientes, título o artista), índice de letras para
  saltar rápido en orden alfabético, reproducir todo, aleatorio y borrado.
- 💾 **La música sobrevive a la desinstalación**: se guarda en `Música/Wired` (Android 11+)
  o `Música\Wired` (Windows) con nombres `Artista - Título [fuente-id].m4a`; tras reinstalar,
  "Recuperar música" reconstruye la biblioteca desde esos archivos. Las listas y los "Me gusta" se
  reflejan como listas M3U estándar en `Música/Wired/Playlists` y también se recuperan.
- 📥 **Importar desde Spotify** con el CSV de [Exportify](https://exportify.net): cada canción se
  busca en la fuente elegida (puntuando duración, título, artista y descartando versiones en vivo,
  covers, karaoke…), se descarga y se agrega a una lista nueva y/o a "Me gusta" en el orden del
  archivo. La importación es reanudable y muestra las canciones no encontradas.
- 🚦 **Peticiones con límite**: el trabajo en segundo plano (importaciones, descargas) va a ~1
  petición cada 2 s por fuente y, si la fuente pide frenar (HTTP 429, verificación anti-bot), se
  pausa con backoff exponencial (1 → 15 min) y reintenta solo. Lo que el usuario toca no espera.
- 📶 **Descargas en segundo plano** (Android): servicio en primer plano con notificación de
  progreso, wake lock y Wi‑Fi lock para no perder la red con la pantalla apagada; si la conexión se
  corta, las descargas esperan a que vuelva y reintentan.
- ❤️ **Me gusta** y 🎶 **listas de reproducción** (crear, renombrar, borrar, agregar/quitar canciones).
- 🎤 **Letras sincronizadas** de [LRCLIB](https://lrclib.net): la línea actual se resalta y
  centra, tocar una línea salta a ese momento, ajuste fino de ±0,5 s y caché para verlas sin conexión.
- ➕ **Cola**: desliza una canción hacia cualquier lado para agregarla (con Deshacer), mantén presionado
  para el menú (reproducir a continuación, agregar a la cola, me gusta, listas…) y ordena la
  pantalla **"A continuación"** arrastrando.
- 🖼️ **Carátulas y etiquetas**: cada canción guarda su carátula en el teléfono (se ve sin conexión)
  y lleva título, artistas, álbum y carátula dentro del archivo; las importadas usan los datos
  reales de Spotify y los títulos de las descargadas se limpian ("(Official Video)", "VEVO"…).
- 🔗 **Compartir enlaces**: "Compartir → Wired" en un enlace que reconozca alguna fuente lo descarga.
- 🚗 **Android Auto**: navega Biblioteca, Me gusta y Listas, ve la cola y pide canciones por voz.
- 🔊 **Volumen parejo**: Wired mide el volumen de cada canción descargada y lo ajusta al reproducir,
  para que no haya saltos entre canciones antiguas y modernas.
- ⏯️ **Retoma la cola**: al reabrir la app vuelve la cola, la canción y el punto donde quedaste (en pausa).
- 🎯 **Elegir la versión a mano**: en una importación, "Elegir versión" lista las canciones no
  encontradas; busca, escucha y descarga la correcta (queda en su lugar de la lista).
- 😴 **Temporizador para dormir**: 15/30/45/60 min o al terminar la canción, con desvanecido final.
- 🔀 **Aleatorio** y 🔁 **repetición** (desactivada / toda la cola / una canción).
- ▶️ **Mini reproductor** persistente y pantalla **"Reproduciendo ahora"**.
- 🔒 **Reproducción en segundo plano**: en Android con notificación multimedia y controles
  en pantalla de bloqueo / auriculares (foreground service + `MediaSession`); en iOS con
  el modo de audio en segundo plano.
- 🌌 Tema oscuro neón (violeta → rosa → cian).

## Arquitectura

```
src/
├── Wired.Core               Dominio puro (sin dependencias de UI)
│   ├── Models               RemoteTrack, LocalTrack
│   ├── Plugins              IMusicSourcePlugin, IStreamableMusicSource, IDiscoverableMusicSource, PluginCatalog
│   ├── Abstractions         ITrackLibrary, IAudioPlayer, IAppPaths, IClock
│   ├── UseCases             SearchTracks, DownloadTrack, RemoveTrack
│   ├── Playback             PlaybackQueue (aleatorio/repetición), PlaybackService, streaming
│   ├── Downloads            DownloadQueue
│   ├── Resilience           Reintentos ante cortes de red (decorator) cuando vuelve la conexión
│   ├── Throttling           Límite de peticiones por fuente (decorator), prioridad y backoff
│   ├── Enrichment           Carátulas locales, etiquetas y metadatos de Spotify
│   ├── Browsing             Árbol de navegación para Android Auto
│   ├── Import               CSV de Exportify, TrackMatcher, PlaylistImportService reanudable
│   ├── Lyrics               ILyricsProvider, parser LRC, limpieza de títulos, LyricsService
│   └── Collections          Playlists y Me gusta
├── Wired.Infrastructure     Persistencia JSON, LRCLIB, carátulas y etiquetas (TagLib#)
├── Wired.Presentation       ViewModels (CommunityToolkit.Mvvm), sin referencias a MAUI
└── Wired.App                .NET MAUI: vistas XAML, servicios de plataforma, DI
tests/
└── Wired.Tests              xUnit: dominio, infraestructura y ViewModels
```

Las dependencias apuntan siempre hacia dentro: `App → Presentation → Core ← Infrastructure / Plugins`
(los plugins viven fuera de este repositorio, ver [Plugins externos](#plugins-externos)).
Los ViewModels no conocen MAUI (navegación, diálogos y el hilo de UI se inyectan
como interfaces), por eso se pueden testear con un `dotnet test` normal.
`Wired.App/ServiceCollectionExtensions.cs` es la raíz de composición.

## Crear un plugin nuevo

1. Crea un proyecto de librería `Wired.Plugins.MiFuente` que referencie `Wired.Core`.
2. Implementa `IMusicSourcePlugin`:

   ```csharp
   public sealed class MiFuentePlugin : IMusicSourcePlugin
   {
       public PluginInfo Info { get; } = new("mifuente", "Mi Fuente", "Descripción", "#22D3EE");

       public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken ct) { ... }

       public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory,
           IProgress<double>? progress, CancellationToken ct) { ... } // devuelve la ruta del archivo
   }
   ```

3. Expón una extensión `AddMiFuenteMusicSource()` y regístrala desde el método parcial `AddExternalMusicSources`
   (ver [Plugins externos](#plugins-externos)) o directamente en `AddMusicSources()` de `Wired.App/ServiceCollectionExtensions.cs`.

Aparecerá automáticamente como chip en el buscador. No hay que tocar vistas ni ViewModels.

Si la fuente puede sugerir música (top actual, canciones parecidas), implementa además la capacidad
opcional `IDiscoverableMusicSource` (`GetTrendingAsync(region, …)` / `GetRelatedAsync`; la región es un código ISO como `CO`, o `null` para el top global), igual que
`IStreamableMusicSource` para el streaming: el Inicio la usará, siempre a través del límite de peticiones.

## Plugins externos

Un plugin no tiene que vivir en este repositorio. `Wired.App` ofrece dos puntos de enlace:

- **MSBuild**: `Wired.App.csproj` importa un archivo `.props` si existe. Su ruta está en la propiedad
  `WiredPluginsProps`, que por defecto es `Wired.Plugins.props` junto a la carpeta que contiene el repositorio
  (`../../../Wired.Plugins.props` desde `src/Wired.App`) y se cambia con `-p:WiredPluginsProps=<ruta>`.
  Si el archivo no existe, la app se compila sin plugins.
- **Código**: `ServiceCollectionExtensions` (la raíz de composición) es `partial` y llama al método parcial
  `static partial void AddExternalMusicSources(IServiceCollection services)`. El `.props` agrega un archivo que lo
  implementa y registra las fuentes.

Ejemplo de `Wired.Plugins.props` (usa `$(MSBuildThisFileDirectory)` para rutas relativas al propio archivo):

```xml
<Project>
  <ItemGroup>
    <ProjectReference Include="$(MSBuildThisFileDirectory)src/Wired.Plugins.MiFuente/Wired.Plugins.MiFuente.csproj" />
    <Compile Include="$(MSBuildThisFileDirectory)src/PluginRegistration.cs" />
  </ItemGroup>
</Project>
```

```csharp
// src/PluginRegistration.cs
using Wired.Plugins.MiFuente;

namespace Wired.App;

public static partial class ServiceCollectionExtensions
{
    static partial void AddExternalMusicSources(IServiceCollection services) =>
        services.AddMiFuenteMusicSource();
}
```

Para compilar con un plugin: `dotnet build src/Wired.App -f net10.0-android -p:WiredPluginsProps=C:/ruta/Wired.Plugins.props`.

## Ejecutar

Requisitos: .NET 10 SDK y el workload de MAUI (`dotnet workload install maui`).

```bash
# Android
dotnet build src/Wired.App -t:Run -f net10.0-android
# Windows
dotnet build src/Wired.App -t:Run -f net10.0-windows10.0.19041.0
# Tests
dotnet test tests/Wired.Tests
```

O abre `Wired.slnx` en Visual Studio 2026 / Rider y ejecuta `Wired.App`.

## Probar Android Auto

Las apps instaladas fuera de Play Store solo aparecen en Android Auto con las opciones de
desarrollador: en la app Android Auto, toca 10 veces "Versión", luego ⋮ → Configuración para
desarrolladores → activa **Fuentes desconocidas**. Para probar sin auto, usa el
[Desktop Head Unit](https://developer.android.com/training/cars/testing/dhu).

## Notas

- Android usa `MediaPlayer` nativo (wake lock, foco de audio, pausa al desconectar auriculares).
  iOS / Windows usan [Plugin.Maui.Audio](https://github.com/jfversluis/Plugin.Maui.Audio);
  ahí el streaming carga la canción completa en memoria antes de empezar.
- Los datos (biblioteca, listas, me gusta) se guardan en JSON con `System.Text.Json`
  generado en compilación (los builds Release están recortados y no admiten reflexión).

## Licencia

[MIT](LICENSE). Las dependencias conservan sus propias licencias: CommunityToolkit
y Plugin.Maui.Audio son MIT; [TagLib#](https://github.com/mono/taglib-sharp) es LGPL-2.1 y se usa
como librería sin modificar.
