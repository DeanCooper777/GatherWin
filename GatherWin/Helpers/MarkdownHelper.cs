using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace GatherWin.Helpers;

/// <summary>
/// Attached properties for rendering basic markdown in a RichTextBox.
/// Supports: # headings, **bold**, *italic*, `code`, [text](url), and line breaks.
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

        // Split by double newlines for paragraphs
        var paragraphs = Regex.Split(markdown, @"\r?\n\r?\n");

        bool isFirst = true;
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

        if (doc.Blocks.Count == 0)
            doc.Blocks.Add(new Paragraph());

        return doc;
    }

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
                    FontFamily = new FontFamily("Consolas"),
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
                catch
                {
                    // Invalid URI — show as plain text
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
        catch { /* ignore launch failures */ }
        e.Handled = true;
    }
}
