using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace VotschVc3.App.Views;

/// <summary>
/// Attached property that fills a <see cref="TextBlock"/> with inline runs parsed
/// from a small subset of markdown – just <c>**bold**</c> – so changelog bullets
/// can show the feature name in bold without a full markdown engine.
/// </summary>
public static class MarkdownText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(MarkdownText),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);

    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();
        string text = e.NewValue as string ?? string.Empty;

        int i = 0;
        bool bold = false;
        var buffer = new System.Text.StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var run = new Run(buffer.ToString());
            if (bold)
            {
                run.FontWeight = FontWeights.SemiBold;
            }

            textBlock.Inlines.Add(run);
            buffer.Clear();
        }

        while (i < text.Length)
        {
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i += 2;
                continue;
            }

            buffer.Append(text[i]);
            i++;
        }

        Flush();
    }
}
