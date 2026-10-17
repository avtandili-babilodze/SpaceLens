using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceLens.Controls;

/// <summary>
/// Renders the assistant's lightweight formatting into a TextBlock: "- " bullets, **bold** and [[path]] links.
/// Links run <see cref="LinkCommandProperty"/> with the path; everything else stays plain text, so names
/// that look like markup can't inject anything.
/// </summary>
public static partial class ChatText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(ChatText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty LinkCommandProperty = DependencyProperty.RegisterAttached(
        "LinkCommand", typeof(ICommand), typeof(ChatText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty LinkBrushProperty = DependencyProperty.RegisterAttached(
        "LinkBrush", typeof(Brush), typeof(ChatText), new PropertyMetadata(Brushes.DodgerBlue, OnChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    public static ICommand? GetLinkCommand(DependencyObject element) => (ICommand?)element.GetValue(LinkCommandProperty);

    public static void SetLinkCommand(DependencyObject element, ICommand? value) => element.SetValue(LinkCommandProperty, value);

    public static Brush GetLinkBrush(DependencyObject element) => (Brush)element.GetValue(LinkBrushProperty);

    public static void SetLinkBrush(DependencyObject element, Brush value) => element.SetValue(LinkBrushProperty, value);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();
        var text = GetText(textBlock);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                textBlock.Inlines.Add(new Run("  •  "));
                line = trimmed[2..];
            }

            AddFormatted(textBlock, line);
            if (i < lines.Length - 1)
            {
                textBlock.Inlines.Add(new LineBreak());
            }
        }
    }

    private static void AddFormatted(TextBlock textBlock, string line)
    {
        var position = 0;
        foreach (Match match in TokenPattern().Matches(line))
        {
            if (match.Index > position)
            {
                textBlock.Inlines.Add(new Run(line[position..match.Index]));
            }

            if (match.Groups["bold"].Success)
            {
                textBlock.Inlines.Add(new Bold(new Run(match.Groups["bold"].Value)));
            }
            else
            {
                textBlock.Inlines.Add(CreateLink(textBlock, match.Groups["path"].Value));
            }

            position = match.Index + match.Length;
        }

        if (position < line.Length)
        {
            textBlock.Inlines.Add(new Run(line[position..]));
        }
    }

    private static Inline CreateLink(TextBlock owner, string path)
    {
        var label = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        var link = new Hyperlink(new Run(string.IsNullOrEmpty(label) ? path : label))
        {
            Command = GetLinkCommand(owner),
            CommandParameter = path,
            Foreground = GetLinkBrush(owner),
            TextDecorations = null,
            ToolTip = path,
            Cursor = Cursors.Hand
        };
        return link;
    }

    [GeneratedRegex(@"\*\*(?<bold>[^*\n]+)\*\*|\[\[(?<path>[^\]\n]+)\]\]")]
    private static partial Regex TokenPattern();
}
