using System.Globalization;
using System.Windows.Data;
using System.Windows.Documents;
using Markdig;

namespace GatherWin.Converters;

/// <summary>
/// Converts a markdown string to a WPF FlowDocument using a safe Markdig pipeline
/// that explicitly excludes math rendering (which would produce Greek/special characters).
/// </summary>
public class MarkdownFlowDocConverter : IValueConverter
{
    // Build once: safe pipeline without mathematics extension
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .UsePipeTables()
            .UseGridTables()
            .UseListExtras()
            .UseTaskLists()
            .UseAutoIdentifiers()
            .UseSoftlineBreakAsHardlineBreak()
            .Build();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string md || string.IsNullOrEmpty(md))
            return new FlowDocument();
        try
        {
            return Markdig.Wpf.Markdown.ToFlowDocument(md, Pipeline);
        }
        catch
        {
            // Fallback to plain text if markdown rendering fails
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run(md)));
            return doc;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
