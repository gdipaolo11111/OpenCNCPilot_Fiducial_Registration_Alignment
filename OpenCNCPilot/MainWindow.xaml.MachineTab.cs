using System;
using System.IO.Ports;
using System.Linq;
using System.Windows;

namespace OpenCNCPilot
{
	partial class MainWindow
	{
		private void ButtonSettings_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode != Communication.Machine.OperatingMode.Disconnected)
				return;

			new SettingsWindow().ShowDialog();
			this.settingsWindow.ResetAll();
		}

		private void ButtonConnect_Click(object sender, RoutedEventArgs e)
		{
			if (Properties.Settings.Default.ConnectionType == Communication.ConnectionType.Serial && !SelectSerialPort())
				return;

			try
			{
				machine.Connect();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

		/// <summary>
		/// If no serial port is selected (or the selected one no longer exists) and only one is available, selects that one.
		/// Returns false if there is no usable port.
		/// </summary>
		private bool SelectSerialPort()
		{
			string[] ports = SerialPort.GetPortNames().Distinct().ToArray();
			string selected = Properties.Settings.Default.SerialPortName;

			if (!string.IsNullOrEmpty(selected) && ports.Contains(selected))
				return true;

			if (ports.Length == 1)
			{
				Properties.Settings.Default.SerialPortName = ports[0];
				Machine_Info($"Serial port {ports[0]} selected (the only one available)");
				return true;
			}

			if (ports.Length == 0)
				MessageBox.Show("No serial port found. Is the machine plugged in?");
			else
				MessageBox.Show($"Select the serial port in the settings, there are several available: {string.Join(", ", ports)}");

			return false;
		}

		private void ButtonDisconnect_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				machine.Disconnect();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			Properties.Settings.Default.Save();

			if (machine.Connected)
			{
				MessageBox.Show("Can't close while connected!");
				e.Cancel = true;
				return;
			}

			settingsWindow.Close();
			Application.Current.Shutdown();
		}

		private void ButtonSyncBuffer_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode != Communication.Machine.OperatingMode.Manual)
				return;

			machine.SyncBuffer = true;
		}

		private void ShowGrblSettings_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode != Communication.Machine.OperatingMode.Manual)
				return;

			machine.SendLine("$$");
			settingsWindow.ShowDialog();
		}

		private void ButtonMachineHome_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode != Communication.Machine.OperatingMode.Manual)
				return;

			machine.SendLine("$H");
		}
	}
}
