using Wired.Core.Models;

namespace Wired.Presentation.Messages;

public sealed record TrackRemovedMessage(LocalTrack Track);

/// <summary>The interface language changed: texts built by view models must be read again.</summary>
public sealed record LanguageChangedMessage;
