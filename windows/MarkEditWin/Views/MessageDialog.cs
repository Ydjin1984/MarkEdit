using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MarkEditWin.Views;

/// <summary>
/// Minimal modal dialog with an arbitrary set of buttons, used by the
/// <c>api.showAlert</c> bridge method which returns the clicked button index.
/// </summary>
public static class MessageDialog
{
    public static int Show(Window? owner, string? title, string? message, IReadOnlyList<string> buttons)
    {
        var options = buttons.Count == 0 ? new[] { "OK" } : buttons.ToArray();

        var window = new Window
        {
            Title = title ?? "MarkEdit",
            SizeToContent = SizeToContent.Height,
            Width = 420,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = message ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        });

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var result = options.Length - 1;
        for (var index = 0; index < options.Length; index++)
        {
            var button = new Button
            {
                Content = options[index],
                MinWidth = 84,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = index == options.Length - 1,
                IsCancel = index == 0,
            };

            var captured = index;
            button.Click += (_, _) =>
            {
                result = captured;
                window.DialogResult = true;
            };

            buttonRow.Children.Add(button);
        }

        root.Children.Add(buttonRow);
        window.Content = root;
        window.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                result = 0;
                window.DialogResult = false;
            }
        };

        window.ShowDialog();
        return result;
    }
}

/// <summary>
/// Single line prompt, used by the <c>api.showTextBox</c> bridge method.
/// </summary>
public static class TextInputDialog
{
    public static string? Show(Window? owner, string? title, string? placeholder, string? defaultValue)
    {
        var window = new Window
        {
            Title = title ?? "MarkEdit",
            SizeToContent = SizeToContent.Height,
            Width = 420,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        var root = new StackPanel { Margin = new Thickness(20) };
        var input = new TextBox
        {
            Text = defaultValue ?? string.Empty,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 16),
        };

        if (!string.IsNullOrEmpty(placeholder))
        {
            // WPF has no placeholder on TextBox, emulate it with a hint label.
            var hint = new TextBlock
            {
                Text = placeholder,
                Foreground = Brushes.Gray,
                Margin = new Thickness(2, -14, 0, 14),
                IsHitTestVisible = false,
                FontSize = 11,
            };

            if (!string.IsNullOrEmpty(defaultValue))
            {
                hint.Visibility = Visibility.Collapsed;
            }

            input.TextChanged += (_, _) => hint.Visibility = string.IsNullOrEmpty(input.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            root.Children.Add(input);
            root.Children.Add(hint);
        }
        else
        {
            root.Children.Add(input);
        }

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        string? result = null;

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4, 10, 4),
            IsCancel = true,
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };

        okButton.Click += (_, _) =>
        {
            result = input.Text;
            window.DialogResult = true;
        };

        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(okButton);
        root.Children.Add(buttonRow);

        window.Content = root;
        window.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return window.ShowDialog() == true ? result : null;
    }
}
