using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VotschVc3.App.Views;

/// <summary>
/// Scalable vector graphic of a Vötsch test chamber with a continuously
/// rotating interior fan. Used both on the home page cards and the chamber
/// detail header.
/// </summary>
public partial class ChamberGraphic : UserControl
{
    public ChamberGraphic() => InitializeComponent();

    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(ChamberGraphic),
        new PropertyMetadata("VT³ 7060"));

    /// <summary>Model label shown on the cabinet.</summary>
    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(ChamberGraphic),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x2B))));

    /// <summary>Accent colour for the door frame, louvers and brand text.</summary>
    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }
}
