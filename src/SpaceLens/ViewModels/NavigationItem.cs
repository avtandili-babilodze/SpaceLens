namespace SpaceLens.ViewModels;

/// <summary>One entry in the left navigation. <see cref="Glyph"/> is a Segoe Fluent Icons code point.</summary>
public sealed record NavigationItem(AppPage Page, string Label, string Glyph);
