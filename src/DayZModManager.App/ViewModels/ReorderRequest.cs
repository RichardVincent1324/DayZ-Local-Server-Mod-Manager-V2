namespace DayZModManager.App.ViewModels;

/// <summary>Request produced by a drag-and-drop reorder: move <see cref="SourceName"/> to <see cref="TargetIndex"/>.</summary>
public sealed record ReorderRequest(string SourceName, int TargetIndex);
