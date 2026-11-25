using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCNCPilot
{
	/// <summary>
	/// Interaction logic for EnterNumberWindow.xaml
	/// </summary>
	public partial class EnterNumberWindow : Window
	{
		public double Value { get; set; }
		public bool Ok { get; private set; } = false;
		public event Action<double> User_Ok;
		public event Action User_Cancel;

		public EnterNumberWindow(double defaultValue)
		{
			Value = defaultValue;
			InitializeComponent();
			textBox.SelectAll();
		}

		private void buttonCancel_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void buttonOk_Click(object sender, RoutedEventArgs e)
		{
			if (!ReadValue())
				return;

			Ok = true;

			if (User_Ok != null)
				User_Ok.Invoke(Value);

			Close();
		}

		/// <summary>
		/// Parses a number typed by the user, accepting both '.' and ',' as decimal separator
		/// (parsing it as en-US, like the binding did, turned "12,5" into 125)
		/// </summary>
		internal static bool TryParseNumber(string text, out double value)
		{
			return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		/// <summary>
		/// Reads the number from the text box. Returns false if it isn't a number.
		/// </summary>
		private bool ReadValue()
		{
			double value;
			if (!TryParseNumber(textBox.Text, out value))
			{
				MessageBox.Show(this, $"\"{textBox.Text}\" is not a valid number.");
				textBox.Focus();
				textBox.SelectAll();
				return false;
			}

			Value = value;
			return true;
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (!Ok && User_Cancel != null)
				User_Cancel.Invoke();
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
