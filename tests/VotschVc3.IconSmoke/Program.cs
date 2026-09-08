using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VotschVc3.App.Views;
class Program
{
 [STAThread] static void Main()
 {
  var app = new Application();
  foreach (string file in new[] { "Styles", "Icons", "CrispButtonStyles" })
   app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/VotschVc3.App;component/Themes/"+file+".xaml", UriKind.Relative) });
  var logo = new BitmapImage(new Uri("pack://application:,,,/VotschVc3.App;component/Assets/sylex-logo-red.png"));
  if (logo.PixelWidth <= 0 || logo.PixelHeight <= 0) throw new Exception("Sylex logo resource failed to decode");
  Console.WriteLine($"PASS: packaged Sylex logo {logo.PixelWidth} x {logo.PixelHeight}");
  StatusCheckIcons.Initialize();
  var source = new TextBox { Text = "✓ Splnené   ⚠ Upozornenie   ❌ Chyba   ℹ Informácia" };
  var text = new TextBlock { FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(12) };
  text.SetBinding(StatusCheckIcons.SourceProperty, new Binding("Text") { Source = source });
  if (text.Inlines.OfType<InlineUIContainer>().Count() != 4) throw new Exception("Missing icons");
  source.Text = "▶ Spustiť   ⏸ Čaká   ⏹ Stop   ✎ Upraviť   ↻ Obnoviť";
  if (text.Inlines.OfType<InlineUIContainer>().Count() != 5) throw new Exception("Live update failed");
  var panel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(23,27,43)), Width = 820 };
  var status = new TextBlock { FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(12) };
  StatusCheckIcons.SetSource(status, "✓ Splnené   ⚠ Upozornenie   ❌ Chyba   ℹ Informácia");
  panel.Children.Add(status); panel.Children.Add(text);
  var dynamicText = new TextBlock { FontSize = 14, Foreground = Brushes.White, Margin = new Thickness(12) };
  dynamicText.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = source });
  panel.Children.Add(dynamicText);
  var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
  foreach (string key in new[] { "Save", "Edit", "Gear", "Delete", "Refresh", "Export" })
  {
   var icon = new System.Windows.Shapes.Path { Data = (Geometry)app.Resources["Icon."+key], Style = (Style)app.Resources["IconPath"] };
   actions.Children.Add(new Button { Content = icon, Margin = new Thickness(4), Padding = new Thickness(10) });
  }
  panel.Children.Add(actions);
  panel.Children.Add(new CheckBox { Content = "Operátorský dohľad zapnutý", IsChecked = true, Margin = new Thickness(16) });
  var host = new Window { Content = panel, Width = 840, Height = 260, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
  host.Show();
  StatusCheckIcons.RefreshOpenWindows();
  source.Text = "✔ Aktualizované po načítaní";
  System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  if (dynamicText.Inlines.OfType<InlineUIContainer>().Count() != 1) throw new Exception("Automatic live rendering failed: " + dynamicText.Inlines.OfType<InlineUIContainer>().Count() + " / " + new TextRange(dynamicText.ContentStart,dynamicText.ContentEnd).Text + " source " + StatusCheckIcons.GetSource(dynamicText));
  panel.UpdateLayout();
  var bitmap = new RenderTargetBitmap(820, 240, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
  var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
  using (var stream = System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fbg284-icons.png"))) png.Save(stream);
  host.Close();
  Console.WriteLine("PASS: theme resources, vector colors, direct and automatic bindings, live updates.");
 }
}
