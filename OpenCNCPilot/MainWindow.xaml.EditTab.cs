using OpenCNCPilot.Communication;
using System;
using System.Windows;

namespace OpenCNCPilot
{
	partial class MainWindow
	{
		private void ButtonEditSimplify_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			machine.SetFile(ToolPath.GetGCode().ToArray());
		}

		private void ButtonEditApplyHeightMap_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			if (
				HeightMapApplied &&
				MessageBox.Show("Height Map was already applied once - apply again?", "Height Map was already applied", MessageBoxButton.YesNo) != MessageBoxResult.Yes
				)
			{
				return;
			}

			if (Map == null || Map.NotProbed.Count > 0)
			{
				MessageBox.Show("HeightMap is not ready");
				return;
			}

			// how far the cutting moves go outside the probed area (there the height of the nearest edge is used)
			double outside = Math.Max(
				Math.Max(Map.Min.X - ToolPath.MinFeed.X, ToolPath.MaxFeed.X - Map.Max.X),
				Math.Max(Map.Min.Y - ToolPath.MinFeed.Y, ToolPath.MaxFeed.Y - Map.Max.Y));

			if (
				outside > 0.01 &&
				MessageBox.Show($"The toolpath goes up to {outside:0.##} mm outside the probed area.\n\nOutside the area, the height of the nearest edge of the height map will be used. Apply the height map anyway?",
					"Toolpath outside the height map", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes
				)
			{
				return;
			}

			try
			{
				machine.SetFile(ToolPath.ApplyHeightMap(Map).GetGCode());
				Machine_OperatingMode_Changed();
			}
			catch (IndexOutOfRangeException)
			{
				MessageBox.Show("The Toolpath is not contained in the HeightMap");
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}

			HeightMapApplied = true;
		}

		private void ButtonEditArcToLines_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			EnterNumberWindow enw = new EnterNumberWindow(Properties.Settings.Default.ArcToLineSegmentLength);
			enw.Title = "Arc Segment Length";
			enw.Owner = this;
			enw.User_Ok += Enw_User_Ok_ArcToLines;
			enw.Show();
		}

		private void Enw_User_Ok_ArcToLines(double value)
		{
			Properties.Settings.Default.ArcToLineSegmentLength = value;

			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			machine.SetFile(ToolPath.ArcsToLines(value).GetGCode());
		}

		private void ButtonEditSplit_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			EnterNumberWindow enw = new EnterNumberWindow(Properties.Settings.Default.SplitSegmentLength);
			enw.Title = "Segment Length";
			enw.Owner = this;
			enw.User_Ok += Enw_User_Ok_Split;
			enw.Show();
		}

		private void ButtonEditRotateCW_Click(object sender, RoutedEventArgs e)
		{
			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			try
			{
				machine.SetFile(ToolPath.RotateCW().GetGCode());
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

		private void Enw_User_Ok_Split(double value)
		{
			Properties.Settings.Default.SplitSegmentLength = value;

			if (machine.Mode == Machine.OperatingMode.SendFile)
				return;

			machine.SetFile(ToolPath.Split(value).GetGCode());
		}
	}
}
