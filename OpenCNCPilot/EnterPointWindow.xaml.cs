using OpenCNCPilot.Util;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace OpenCNCPilot
{
	/// <summary>
	/// Asks for an X;Y point in one text box (';' separates the coordinates, so ',' can be the decimal separator)
	/// </summary>
	public partial class EnterPointWindow : Window
	{
		public Vector2 Value { get; private set; }
		public bool Ok { get; private set; } = false;

		public EnterPointWindow(string prompt, Vector2 defaultValue)
		{
			InitializeComponent();

			textBlockPrompt.Text = prompt;
			Value = defaultValue;
			textBox.Text = Format(defaultValue.X) + ";" + Format(defaultValue.Y);
			textBox.SelectAll();
		}

		private static string Format(double value)
		{
			return value.ToString("0.###", CultureInfo.CurrentCulture);
		}

		private void buttonCancel_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void buttonOk_Click(object sender, RoutedEventArgs e)
		{
			string[] parts = textBox.Text.Split(';');
			double x = 0, y = 0;

			if (parts.Length != 2 || !EnterNumberWindow.TryParseNumber(parts[0], out x) || !EnterNumberWindow.TryParseNumber(parts[1], out y))
			{
				MessageBox.Show(this, $"\"{textBox.Text}\" is not valid.\n\nEnter X and Y separated by ';', for example: -35,524;0");
				textBox.Focus();
				textBox.SelectAll();
				return;
			}

			Value = new Vector2(x, y);
			Ok = true;
			Close();
		}

		private void textBox_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				e.Handled = true;
				buttonOk_Click(null, null);
			}
			else if (e.Key == Key.Escape)
			{
				e.Handled = true;
				buttonCancel_Click(null, null);
			}
		}
	}
}
