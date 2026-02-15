using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using GatherWin.Services;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace GatherWin.Helpers;

/// <summary>
/// Attached properties for rendering basic markdown in a RichTextBox.
/// Supports: # headings, **bold**, *italic*, `code`, ```code blocks```, [text](url), and line breaks.
/// </summary>
public static class MarkdownHelper
{
    // ── MarkdownText attached property ────────────────────────────

    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.RegisterAttached(
            "MarkdownText", typeof(string), typeof(MarkdownHelper),
            new PropertyMetadata(null, OnMarkdownPropertyChanged));

    public static string? GetMarkdownText(DependencyObject obj) => (string?)obj.GetValue(MarkdownTextProperty);
    public static void SetMarkdownText(DependencyObject obj, string? value) => obj.SetValue(MarkdownTextProperty, value);

    // ── AuthorPrefix attached property ────────────────────────────

    public static readonly DependencyProperty AuthorPrefixProperty =
        DependencyProperty.RegisterAttached(
            "AuthorPrefix", typeof(string), typeof(MarkdownHelper),
            new PropertyMetadata(null, OnMarkdownPropertyChanged));

    public static string? GetAuthorPrefix(DependencyObject obj) => (string?)obj.GetValue(AuthorPrefixProperty);
    public static void SetAuthorPrefix(DependencyObject obj, string? value) => obj.SetValue(AuthorPrefixProperty, value);

    // ── Rebuild document when either property changes ─────────────

    private static void OnMarkdownPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBox rtb)
        {
            var markdown = GetMarkdownText(rtb) ?? "";
            var author = GetAuthorPrefix(rtb);
            rtb.Document = BuildDocument(author, markdown);
        }
    }

    private static FlowDocument BuildDocument(string? author, string markdown)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0) };

        // Split into alternating text/code segments by fenced code blocks
        var segments = SplitCodeBlocks(markdown);

        bool isFirst = true;
        foreach (var (text, isCode) in segments)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (isCode)
            {
                AddCodeBlock(doc, text);
            }
            else
            {
                AddMarkdownText(doc, text, ref isFirst, author);
            }
        }

        if (doc.Blocks.Count == 0)
            doc.Blocks.Add(new Paragraph());

        return doc;
    }

    // ── Fenced code block splitting ───────────────────────────────

    private static readonly Regex FencePattern = new(
        @"^```", RegexOptions.Multiline | RegexOptions.Compiled);

    private static List<(string Text, bool IsCode)> SplitCodeBlocks(string markdown)
    {
        var segments = new List<(string, bool)>();
        var matches = FencePattern.Matches(markdown);

        if (matches.Count == 0)
        {
            segments.Add((markdown, false));
            return segments;
        }

        int lastEnd = 0;
        bool inCode = false;

        foreach (Match match in matches)
        {
            if (!inCode)
            {
                // Text before the opening fence
                if (match.Index > lastEnd)
                    segments.Add((markdown[lastEnd..match.Index], false));

                // Skip the ``` line (including optional language tag and newline)
                var lineEnd = markdown.IndexOf('\n', match.Index);
                lastEnd = lineEnd >= 0 ? lineEnd + 1 : markdown.Length;
                inCode = true;
            }
            else
            {
                // Code content between fences
                var code = markdown[lastEnd..match.Index].TrimEnd('\r', '\n');
                if (code.Length > 0)
                    segments.Add((code, true));

                // Skip the closing ``` line
                var lineEnd = markdown.IndexOf('\n', match.Index);
                lastEnd = lineEnd >= 0 ? lineEnd + 1 : markdown.Length;
                inCode = false;
            }
        }

        // Handle remaining text after last fence
        if (lastEnd < markdown.Length)
        {
            var remaining = markdown[lastEnd..];
            if (inCode)
            {
                // Unclosed code block — treat rest as code
                segments.Add((remaining.TrimEnd('\r', '\n'), true));
            }
            else if (!string.IsNullOrWhiteSpace(remaining))
            {
                segments.Add((remaining, false));
            }
        }

        return segments;
    }

    // ── Code block rendering ──────────────────────────────────────

    private static readonly SolidColorBrush CodeBackground =
        new(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly SolidColorBrush CodeBorder =
        new(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly FontFamily ConsolasFont = new("Consolas");

    private static void AddCodeBlock(FlowDocument doc, string code)
    {
        var paragraph = new Paragraph
        {
            FontFamily = ConsolasFont,
            FontSize = 11.5,
            Background = CodeBackground,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4),
            BorderBrush = CodeBorder,
            BorderThickness = new Thickness(1)
        };

        var lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(lines[i].TrimEnd('\r')));
        }

        doc.Blocks.Add(paragraph);
    }

    // ── Normal markdown text rendering ────────────────────────────

    private static void AddMarkdownText(FlowDocument doc, string text,
        ref bool isFirst, string? author)
    {
        var paragraphs = Regex.Split(text, @"\r?\n\r?\n");

        foreach (var paraText in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paraText)) continue;

            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };

            // Prepend author as bold on the first paragraph
            if (isFirst && !string.IsNullOrEmpty(author))
            {
                paragraph.Inlines.Add(new Bold(new Run(author)));
                paragraph.Inlines.Add(new Run(" "));
                isFirst = false;
            }
            else
            {
                isFirst = false;
            }

            // Handle single line breaks within paragraph
            var lines = Regex.Split(paraText, @"\r?\n");
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var headingMatch = HeadingPattern.Match(line);
                if (headingMatch.Success)
                {
                    // Flush current paragraph if it has content
                    if (paragraph.Inlines.Count > 0)
                    {
                        doc.Blocks.Add(paragraph);
                        paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                    }

                    var level = headingMatch.Groups[1].Value.Length;
                    var headingPara = new Paragraph
                    {
                        Margin = new Thickness(0, level <= 2 ? 4 : 2, 0, 2),
                        FontSize = level switch
                        {
                            1 => 20,
                            2 => 17,
                            3 => 15,
                            _ => 13
                        },
                        FontWeight = FontWeights.Bold
                    };
                    ParseInlines(headingPara.Inlines, headingMatch.Groups[2].Value);
                    doc.Blocks.Add(headingPara);
                }
                else
                {
                    if (i > 0 && !HeadingPattern.IsMatch(lines[i - 1]))
                        paragraph.Inlines.Add(new LineBreak());
                    ParseInlines(paragraph.Inlines, line);
                }
            }

            if (paragraph.Inlines.Count > 0)
                doc.Blocks.Add(paragraph);
        }
    }

    // ── Inline patterns ───────────────────────────────────────────

    private static readonly Regex HeadingPattern = new(
        @"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex InlinePattern = new(
        @"(\*\*(.+?)\*\*)" +      // Group 1,2: **bold**
        @"|(__(.+?)__)" +           // Group 3,4: __bold__
        @"|(\*(.+?)\*)" +          // Group 5,6: *italic*
        @"|(_([^_]+?)_)" +         // Group 7,8: _italic_ (single word)
        @"|(`(.+?)`)" +            // Group 9,10: `code`
        @"|(\[(.+?)\]\((.+?)\))",  // Group 11,12,13: [text](url)
        RegexOptions.Compiled);

    private static void ParseInlines(InlineCollection inlines, string text)
    {
        int lastIndex = 0;

        foreach (Match match in InlinePattern.Matches(text))
        {
            // Add text before the match
            if (match.Index > lastIndex)
                inlines.Add(new Run(text[lastIndex..match.Index]));

            if (match.Groups[2].Success) // **bold**
            {
                inlines.Add(new Bold(new Run(match.Groups[2].Value)));
            }
            else if (match.Groups[4].Success) // __bold__
            {
                inlines.Add(new Bold(new Run(match.Groups[4].Value)));
            }
            else if (match.Groups[6].Success) // *italic*
            {
                inlines.Add(new Italic(new Run(match.Groups[6].Value)));
            }
            else if (match.Groups[8].Success) // _italic_
            {
                inlines.Add(new Italic(new Run(match.Groups[8].Value)));
            }
            else if (match.Groups[10].Success) // `code`
            {
                var run = new Run(match.Groups[10].Value)
                {
                    FontFamily = ConsolasFont,
                    Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE))
                };
                inlines.Add(run);
            }
            else if (match.Groups[12].Success && match.Groups[13].Success) // [text](url)
            {
                try
                {
                    var hyperlink = new Hyperlink(new Run(match.Groups[12].Value))
                    {
                        NavigateUri = new Uri(match.Groups[13].Value),
                        ToolTip = match.Groups[13].Value
                    };
                    hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
                    inlines.Add(hyperlink);
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Markdown", $"Invalid URI: {match.Groups[13].Value} — {ex.Message}");
                    inlines.Add(new Run(match.Value));
                }
            }

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text
        if (lastIndex < text.Length)
            inlines.Add(new Run(text[lastIndex..]));
    }

    private static void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { AppLogger.LogError("Markdown: failed to open URL", ex); }
        e.Handled = true;
    }
}
