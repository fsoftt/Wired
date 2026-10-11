using System.Text.Json.Serialization;
using Wired.Core.Collections;
using Wired.Core.Models;

namespace Wired.Infrastructure.Storage;

/// <summary>
/// Compile-time JSON metadata. Release mobile builds are trimmed and disable reflection-based
/// serialization, so every persisted type must be listed here.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<LocalTrack>))]
[JsonSerializable(typeof(List<Playlist>))]
[JsonSerializable(typeof(List<Guid>))]
[JsonSerializable(typeof(Core.Lyrics.Lyrics))]
[JsonSerializable(typeof(HomeFeedEntry))]
[JsonSerializable(typeof(List<Core.Import.PlaylistImportSession>))]
[JsonSerializable(typeof(List<Core.Playback.PlaybackSnapshot>))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
