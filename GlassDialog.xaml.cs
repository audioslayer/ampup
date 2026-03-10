using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WolfMixer;

public partial class GlassDialog : Window
{
    public enum GlassResult { OK, Yes, No, Cancel }

    public GlassResult Result { get; private set; } = GlassResult.Cancel;

    private GlassDialog()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, _) => DragMove();
        Loaded += (_, _) =>
        {
            var fadeIn = (Storyboard)FindResource("FadeIn");
            fadeIn.Begin(this);
        };
    }

    private static Style GlassButtonStyle(bool isPrimary, bool isDanger = false)
    {
        var style = new Style(typeof(Button));
        var bg = isPrimary ? "#00E676" : isDanger ? "#FF4444" : "#1C1C1C";
        var fg = isPrimary ? "#0F0F0F" : isDanger ? "#FFFFFF" : "#E8E8E8";
        var hoverBg = isPrimary ? "#00FF88" : isDanger ? "#FF6666" : "#2A2A2A";
        var borderColor = isPrimary ? "#00E676" : isDanger ? "#FF4444" : "#2A2A2A";

        style.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg))));
        style.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg))));
        style.Setters.Add(new Setter(BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderColor))));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(FontFamilyProperty, new FontFamily("Segoe UI")));
        style.Setters.Add(new Setter(FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(FontWeightProperty, isPrimary ? FontWeights.SemiBold : FontWeights.Regular));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(20, 8, 20, 8)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(6, 0, 0, 0)));
        style.Setters.Add(new Setter(CursorProperty, Cursors.Hand));

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderColor)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(20, 8, 20, 8));

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);

        template.VisualTree = border;

        // Hover trigger
        var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hoverBg)), "Bd"));
        template.Triggers.Add(hoverTrigger);

        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }

    /// <summary>
    /// Show a glass-themed info/warning dialog with a single OK button.
    /// </summary>
    public static void ShowInfo(string message, string title = "AMP UP", Window? owner = null)
    {
        var dlg = new GlassDialog();
        dlg.TitleText.Text = title.ToUpperInvariant();
        dlg.MessageText.Text = message;
        if (owner != null) dlg.Owner = owner;

        var ok = new Button { Content = "OK", Style = GlassButtonStyle(true) };
        ok.Click += (_, _) => { dlg.Result = GlassResult.OK; dlg.Close(); };
        dlg.ButtonPanel.Children.Add(ok);

        dlg.ShowDialog();
    }

    /// <summary>
    /// Show a glass-themed warning dialog with a single OK button.
    /// </summary>
    public static void ShowWarning(string message, string title = "AMP UP", Window? owner = null)
    {
        var dlg = new GlassDialog();
        dlg.TitleText.Text = title.ToUpperInvariant();
        dlg.TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66FFB800"));
        dlg.MessageText.Text = message;
        if (owner != null) dlg.Owner = owner;

        var ok = new Button { Content = "OK", Style = GlassButtonStyle(true) };
        ok.Click += (_, _) => { dlg.Result = GlassResult.OK; dlg.Close(); };
        dlg.ButtonPanel.Children.Add(ok);

        dlg.ShowDialog();
    }

    /// <summary>
    /// Show a glass-themed Yes/No confirmation dialog.
    /// </summary>
    public static bool Confirm(string message, string title = "AMP UP", bool dangerYes = false, Window? owner = null)
    {
        var dlg = new GlassDialog();
        dlg.TitleText.Text = title.ToUpperInvariant();
        if (dangerYes)
            dlg.TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66FF4444"));
        dlg.MessageText.Text = message;
        if (owner != null) dlg.Owner = owner;

        var no = new Button { Content = "No", Style = GlassButtonStyle(false) };
        no.Click += (_, _) => { dlg.Result = GlassResult.No; dlg.Close(); };
        dlg.ButtonPanel.Children.Add(no);

        var yes = new Button { Content = "Yes", Style = GlassButtonStyle(true, dangerYes) };
        if (dangerYes)
        {
            yes.Style = GlassButtonStyle(false, true);
        }
        yes.Click += (_, _) => { dlg.Result = GlassResult.Yes; dlg.Close(); };
        dlg.ButtonPanel.Children.Add(yes);

        dlg.ShowDialog();
        return dlg.Result == GlassResult.Yes;
    }

    /// <summary>
    /// Show a glass-themed input prompt. Returns null if cancelled.
    /// </summary>
    public static string? Prompt(string message, string title = "AMP UP", Window? owner = null)
    {
        var dlg = new GlassDialog();
        dlg.TitleText.Text = title.ToUpperInvariant();
        dlg.MessageText.Text = message;
        dlg.InputBorder.Visibility = Visibility.Visible;
        if (owner != null) dlg.Owner = owner;

        dlg.Loaded += (_, _) => dlg.InputBox.Focus();

        var cancel = new Button { Content = "Cancel", Style = GlassButtonStyle(false) };
        cancel.Click += (_, _) => { dlg.Result = GlassResult.Cancel; dlg.Close(); };
        dlg.ButtonPanel.Children.Add(cancel);

        var ok = new Button { Content = "OK", Style = GlassButtonStyle(true), IsDefault = true };
        ok.Click += (_, _) => { dlg.Result = GlassResult.OK; dlg.Close(); };
        dlg.ButtonPanel.Children.Add(ok);

        dlg.ShowDialog();
        return dlg.Result == GlassResult.OK ? dlg.InputBox.Text : null;
    }
}
