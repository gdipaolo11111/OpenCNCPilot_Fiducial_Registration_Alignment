using Microsoft.Win32;
using OpenCNCPilot.Communication;
using OpenCNCPilot.GCode;
using OpenCNCPilot.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Drawing;
using System.Windows.Media.Imaging;

namespace OpenCNCPilot
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;
        // === PEGA AQUÍ LO NUEVO (PASO 1 de la lógica) ===
        private double cameraOffsetX = Properties.Settings.Default.CameraOffsetX;
        private double cameraOffsetY = Properties.Settings.Default.CameraOffsetY;

        // Posición real (coordenadas de trabajo, las mismas en las que se ejecuta el G-Code) y posición en el G-Code de cada punto
        private Vector2 p1Work, p1GCode;
        private Vector2 p2Work, p2GCode;
        private bool isP1Set = false;
        private bool isP2Set = false;
        // ===============================================

        // === VARIABLES CÁMARA (debajo del offset) ===


        private int cameraRotation = Properties.Settings.Default.CameraRotation;
        private bool cameraMirrored = Properties.Settings.Default.CameraMirrored;

        Machine machine = new Machine();

        OpenFileDialog openFileDialogGCode = new OpenFileDialog() { Filter = Constants.FileFilterGCode };
        SaveFileDialog saveFileDialogGCode = new SaveFileDialog() { Filter = Constants.FileFilterGCode };
        OpenFileDialog openFileDialogHeightMap = new OpenFileDialog() { Filter = Constants.FileFilterHeightMap };
        SaveFileDialog saveFileDialogHeightMap = new SaveFileDialog() { Filter = Constants.FileFilterHeightMap };

        GCodeFile ToolPath { get; set; } = GCodeFile.Empty;
        HeightMap Map { get; set; }

        bool HeightMapApplied { get; set; } = false;
        bool AlignmentApplied { get; set; } = false;

        GrblSettingsWindow settingsWindow = new GrblSettingsWindow();

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;

            InitializeComponent();

            // --- INICIO: Buscar cámaras para la alineación ---
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo device in videoDevices)
            {
                ComboBoxCameras.Items.Add(device.Name);
            }
            if (ComboBoxCameras.Items.Count > 0)
            {
                ComboBoxCameras.SelectedIndex = 0; // Seleccionar la primera cámara por defecto
            }
            // --- FIN: Buscar cámaras ---

            // --- INICIO: Apagar cámara al cerrar la ventana ---
            this.Closing += (sender, e) =>
            {
                StopCamera();
            };
            // --- FIN: Apagar cámara al cerrar la ventana ---

            openFileDialogGCode.FileOk += OpenFileDialogGCode_FileOk;
            saveFileDialogGCode.FileOk += SaveFileDialogGCode_FileOk;
            openFileDialogHeightMap.FileOk += OpenFileDialogHeightMap_FileOk;
            saveFileDialogHeightMap.FileOk += SaveFileDialogHeightMap_FileOk;

            machine.ConnectionStateChanged += Machine_ConnectionStateChanged;

            machine.NonFatalException += Machine_NonFatalException;
            machine.Info += Machine_Info;
            machine.LineReceived += Machine_LineReceived;
            machine.LineReceived += settingsWindow.LineReceived;
            machine.StatusReceived += Machine_StatusReceived;
            machine.LineSent += Machine_LineSent;

            machine.PositionUpdateReceived += Machine_PositionUpdateReceived;
            machine.StatusChanged += Machine_StatusChanged;
            machine.DistanceModeChanged += Machine_DistanceModeChanged;
            machine.UnitChanged += Machine_UnitChanged;
            machine.PlaneChanged += Machine_PlaneChanged;
            machine.BufferStateChanged += Machine_BufferStateChanged;
            machine.OperatingModeChanged += Machine_OperatingMode_Changed;
            machine.FileChanged += Machine_FileChanged;
            machine.FilePositionChanged += Machine_FilePositionChanged;
            machine.ProbeFinished += Machine_ProbeFinished;
            machine.OverrideChanged += Machine_OverrideChanged;
            machine.PinStateChanged += Machine_PinStateChanged;

            Machine_OperatingMode_Changed();
            Machine_PositionUpdateReceived();

            Properties.Settings.Default.SettingChanging += Default_SettingChanging;
            FileRuntimeTimer.Tick += FileRuntimeTimer_Tick;

            machine.ProbeFinished += Machine_ProbeFinished_UserOutput;

            LoadMacros();

            settingsWindow.SendLine += machine.SendLine;

            machine.Calculator.GetGCode += () => ToolPath;

            CheckBoxUseExpressions_Changed(null, null);
            ButtonRestoreViewport_Click(null, null);

            UpdateCheck.CheckForUpdate();

            if (App.Args.Length > 0)
            {
                if (File.Exists(App.Args[0]))
                {
                    openFileDialogGCode.FileName = App.Args[0];
                    OpenFileDialogGCode_FileOk(null, null);
                }
            }
        }

        public Vector3 LastProbePosMachine { get; set; }
        public Vector3 LastProbePosWork { get; set; }

        private void Machine_ProbeFinished_UserOutput(Vector3 position, bool success)
        {
            LastProbePosMachine = machine.LastProbePosMachine;
            LastProbePosWork = machine.LastProbePosWork;

            RaisePropertyChanged("LastProbePosMachine");
            RaisePropertyChanged("LastProbePosWork");
        }

        private void UnhandledException(object sender, UnhandledExceptionEventArgs ea)
        {
            Exception e = (Exception)ea.ExceptionObject;

            string info = "Unhandled Exception:\r\nMessage:\r\n";
            info += e.Message;
            info += "\r\nStackTrace:\r\n";
            info += e.StackTrace;
            info += "\r\nToString():\r\n";
            info += e.ToString();

            MessageBox.Show(info);
            Console.WriteLine(info);

            try
            {
                System.IO.File.WriteAllText("OpenCNCPilot_Crash_Log.txt", info);
            }
            catch { }

            Environment.Exit(1);
        }

        private void Default_SettingChanging(object sender, System.Configuration.SettingChangingEventArgs e)
        {
            if (e.SettingName.Equals("JogFeed") ||
                e.SettingName.Equals("JogDistance") ||
                e.SettingName.Equals("ProbeFeed") ||
                e.SettingName.Equals("ProbeSafeHeight") ||
                e.SettingName.Equals("ProbeMinimumHeight") ||
                e.SettingName.Equals("ProbeMaxDepth") ||
                e.SettingName.Equals("SplitSegmentLength") ||
                e.SettingName.Equals("ViewportArcSplit") ||
                e.SettingName.Equals("ArcToLineSegmentLength") ||
                e.SettingName.Equals("ProbeXAxisWeight") ||
                e.SettingName.Equals("ConsoleFadeTime"))
            {
                if (((double)e.NewValue) <= 0)
                    e.Cancel = true;
            }

            if (e.SettingName.Equals("SerialPortBaud") ||
                e.SettingName.Equals("StatusPollInterval") ||
                e.SettingName.Equals("ControllerBufferSize"))
            {
                if (((int)e.NewValue) <= 0)
                    e.Cancel = true;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(e.Uri.AbsoluteUri);
        }

        public string Version
        {
            get
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return $"{version}";
            }
        }

        public string WindowTitle
        {
            get
            {
                if (CurrentFileName.Length < 1)
                    return $"OpenCNCPilot v{Version} by martin2250";
                else
                    return $"OpenCNCPilot v{Version} by martin2250 - {CurrentFileName}";
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (files.Length > 0)
                {
                    string file = files[0];

                    if (file.EndsWith(".hmap"))
                    {
                        if (machine.Mode == Machine.OperatingMode.Probe || Map != null)
                            return;

                        OpenHeightMap(file);
                    }
                    else
                    {
                        if (machine.Mode == Machine.OperatingMode.SendFile)
                            return;

                        try
                        {
                            machine.SetFile(System.IO.File.ReadAllLines(file));
                            HeightMapApplied = false;
                            AlignmentApplied = false;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                        }
                    }
                }
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (files.Length > 0)
                {
                    string file = files[0];

                    if (file.EndsWith(".hmap"))
                    {
                        if (machine.Mode != Machine.OperatingMode.Probe && Map == null)
                        {
                            e.Effects = DragDropEffects.Copy;
                            return;
                        }
                    }
                    else
                    {
                        if (machine.Mode != Machine.OperatingMode.SendFile)
                        {
                            e.Effects = DragDropEffects.Copy;
                            return;
                        }
                    }
                }
            }

            e.Effects = DragDropEffects.None;
        }

        private void viewport_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space)
            {
                machine.FeedHold();
                e.Handled = true;
            }
        }

        private void ButtonRapidOverride_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;

            if (b == null)
                return;

            switch (b.Content as string)
            {
                case "100%":
                    machine.SendControl(0x95);
                    break;
                case "50%":
                    machine.SendControl(0x96);
                    break;
                case "25%":
                    machine.SendControl(0x97);
                    break;
            }
        }

        private void ButtonFeedOverride_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;

            if (b == null)
                return;

            switch (b.Tag as string)
            {
                case "100%":
                    machine.SendControl(0x90);
                    break;
                case "+10%":
                    machine.SendControl(0x91);
                    break;
                case "-10%":
                    machine.SendControl(0x92);
                    break;
                case "+1%":
                    machine.SendControl(0x93);
                    break;
                case "-1%":
                    machine.SendControl(0x94);
                    break;
            }
        }

        private void ButtonSpindleOverride_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;

            if (b == null)
                return;

            switch (b.Tag as string)
            {
                case "100%":
                    machine.SendControl(0x99);
                    break;
                case "+10%":
                    machine.SendControl(0x9A);
                    break;
                case "-10%":
                    machine.SendControl(0x9B);
                    break;
                case "+1%":
                    machine.SendControl(0x9C);
                    break;
                case "-1%":
                    machine.SendControl(0x9D);
                    break;
            }
        }

        private void ButtonResetViewport_Click(object sender, RoutedEventArgs e)
        {
            viewport.Camera.Position = new System.Windows.Media.Media3D.Point3D(50, -150, 250);
            viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(-50, 150, -250);
            viewport.Camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
        }

        private void ButtonLayFlatViewport_Click(object sender, RoutedEventArgs e)                  // deHarro, 2024-08-23
        {
            viewport.Camera.Position = new System.Windows.Media.Media3D.Point3D(0, 10, 250);
            viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, -250);
            viewport.Camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
        }

        private void ButtonRestoreViewport_Click(object sender, RoutedEventArgs e)
        {
            string[] scoords = Properties.Settings.Default.ViewPortPos.Split(';');

            try
            {
                IEnumerable<double> coords = scoords.Select(s => double.Parse(s));

                viewport.Camera.Position = new Vector3(coords.Take(3).ToArray()).ToPoint3D();
                viewport.Camera.LookDirection = new Vector3(coords.Skip(3).ToArray()).ToVector3D();
                viewport.Camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
            }
            catch
            {
                ButtonResetViewport_Click(null, null);
            }
        }

        private void ButtonSaveViewport_Click(object sender, RoutedEventArgs e)
        {
            List<double> coords = new List<double>();

            coords.AddRange(new Vector3(viewport.Camera.Position).Array);
            coords.AddRange(new Vector3(viewport.Camera.LookDirection).Array);

            Properties.Settings.Default.ViewPortPos = string.Join(";", coords.Select(d => d.ToString()));
        }

        private void ButtonSaveTLOPos_Click(object sender, RoutedEventArgs e)
        {
            if (machine.Mode != Machine.OperatingMode.Manual)
                return;

            double Z = (Properties.Settings.Default.TLSUseActualPos) ? machine.MachinePosition.Z : LastProbePosMachine.Z;

            Properties.Settings.Default.ToolLengthSetterPos = Z;
        }

        private void ButtonApplyTLO_Click(object sender, RoutedEventArgs e)
        {
            if (machine.Mode != Machine.OperatingMode.Manual)
                return;

            double Z = (Properties.Settings.Default.TLSUseActualPos) ? machine.MachinePosition.Z : LastProbePosMachine.Z;

            double delta = Z - Properties.Settings.Default.ToolLengthSetterPos;

            machine.SendLine($"G43.1 Z{delta.ToString(Constants.DecimalOutputFormat)}");
        }

        private void ButtonClearTLO_Click(object sender, RoutedEventArgs e)
        {
            if (machine.Mode != Machine.OperatingMode.Manual)
                return;

            machine.SendLine("G49");
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == App.WM_COPYDATA)
            {
                App.COPYDATASTRUCT _dataStruct = Marshal.PtrToStructure<App.COPYDATASTRUCT>(lParam);
                string _strMsg = Marshal.PtrToStringUni(_dataStruct.lpData, _dataStruct.cbData / 2);
                if (File.Exists(_strMsg))
                {
                    Activate();
                    openFileDialogGCode.FileName = _strMsg;
                    OpenFileDialogGCode_FileOk(null, null);
                }
            }

            return IntPtr.Zero;
        }

        // ==========================================
        // ALINEACIÓN CON CÁMARA (Nuevas funciones)
        // ==========================================

        // Función de seguridad para apagar la cámara correctamente
        private void StopCamera()
        {
            if (videoSource == null)
                return;

            if (videoSource.IsRunning)
            {
                // Le decimos a la cámara que pare
                videoSource.SignalToStop();
                // Esperamos a que se detenga físicamente (apaga el LED)
                videoSource.WaitForStop();
            }
            // Desconectamos el evento de los frames
            videoSource.NewFrame -= new AForge.Video.NewFrameEventHandler(video_NewFrame);
            videoSource = null;
            // Para que no se quede a la vista el último fotograma congelado
            CameraImage.Source = null;
        }

        // Enciende la cámara elegida en el ComboBox, solo si el panel está abierto
        private void StartCamera()
        {
            // 1. APAGAMOS LA CÁMARA ANTERIOR (si la hay)
            StopCamera();

            // 2. ENCENDEMOS LA NUEVA
            if (ExpanderCamera.IsExpanded && ComboBoxCameras.SelectedIndex >= 0 && videoDevices != null && videoDevices.Count > 0)
            {
                videoSource = new VideoCaptureDevice(videoDevices[ComboBoxCameras.SelectedIndex].MonikerString);
                videoSource.NewFrame += new NewFrameEventHandler(video_NewFrame);
                videoSource.Start();
            }
        }

        // Evento al cambiar la cámara en el ComboBox
        private void ComboBoxCameras_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            StartCamera();
        }

        // Encender la cámara al abrir (expandir) el panel
        private void ExpanderCamera_Expanded(object sender, RoutedEventArgs e)
        {
            StartCamera();
        }

        // Apagar la cámara al cerrar (colapsar) el panel
        private void ExpanderCamera_Collapsed(object sender, RoutedEventArgs e)
        {
            StopCamera();
        }


        // Esta función se ejecuta por cada fotograma (frame) que manda la cámara
        private void video_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                // using: cada fotograma copiado se libera al terminar (si no, se acumula memoria a 30 fps)
                using (Bitmap bitmap = (Bitmap)eventArgs.Frame.Clone())
                {
                    // --- INICIO NUEVO: Rotar y Espejar ---
                    RotateFlipType transform = RotateFlipType.RotateNoneFlipNone;

                    if (!cameraMirrored)
                    {
                        if (cameraRotation == 90) transform = RotateFlipType.Rotate90FlipNone;
                        else if (cameraRotation == 180) transform = RotateFlipType.Rotate180FlipNone;
                        else if (cameraRotation == 270) transform = RotateFlipType.Rotate270FlipNone;
                    }
                    else
                    {
                        if (cameraRotation == 0) transform = RotateFlipType.RotateNoneFlipX;
                        else if (cameraRotation == 90) transform = RotateFlipType.Rotate90FlipX;
                        else if (cameraRotation == 180) transform = RotateFlipType.Rotate180FlipX;
                        else if (cameraRotation == 270) transform = RotateFlipType.Rotate270FlipX;
                    }

                    // Aplicamos la transformación a los píxeles si es necesario
                    if (transform != RotateFlipType.RotateNoneFlipNone)
                    {
                        bitmap.RotateFlip(transform);
                    }
                    // --- FIN NUEVO ---

                    // Convertir la imagen de la cámara a un formato que WPF pueda mostrar
                    using (MemoryStream memory = new MemoryStream())
                    {
                        bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                        memory.Position = 0;
                        BitmapImage bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = memory;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze(); // Muy importante

                        Dispatcher.BeginInvoke(new System.Action(delegate
                        {
                            // Si mientras tanto se ha apagado o cambiado la cámara, este fotograma ya no vale
                            if (sender == videoSource)
                                CameraImage.Source = bitmapImage;
                        }));
                    }
                }
            }
            catch { }
        }

        // Para leer la posición, la máquina tiene que estar conectada y sin nada en marcha
        private bool CheckMachineReady()
        {
            if (machine.Mode == Machine.OperatingMode.Manual)
                return true;

            MessageBox.Show("The machine has to be connected and idle (no file, probing or macro running).");
            return false;
        }

        private void ButtonSetOffset_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckMachineReady())
                return;

            MessageBoxResult result = MessageBox.Show(
                "How to calibrate:\n\n" +
                "1. Make a small mark on the material with the tool.\n" +
                "2. Zero the X and Y axes on the mark ('Zero (G10)' or 'Zero (G92)' button).\n" +
                "3. Jog the machine until the red crosshair of the camera is exactly over the mark.\n\n" +
                "Done? Save the current position as the camera offset?",
                "Calibrate Camera Offset", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                // El offset será la posición actual de trabajo (lo que nos hemos movido desde el agujero)
                cameraOffsetX = machine.WorkPosition.X;
                cameraOffsetY = machine.WorkPosition.Y;

                // Se guarda en la configuración para no tener que calibrar cada vez que se abre el programa
                Properties.Settings.Default.CameraOffsetX = cameraOffsetX;
                Properties.Settings.Default.CameraOffsetY = cameraOffsetY;
                Properties.Settings.Default.Save();

                MessageBox.Show($"Offset saved:\nX = {cameraOffsetX:F3} mm\nY = {cameraOffsetY:F3} mm");
            }
        }

        // Posición real (coordenadas de trabajo) del punto que está bajo la cruz de la cámara:
        // la del husillo menos el offset de la cámara
        private Vector2 PointUnderCamera()
        {
            return new Vector2(machine.WorkPosition.X - cameraOffsetX, machine.WorkPosition.Y - cameraOffsetY);
        }

        // Pide las coordenadas que tiene el punto en el G-Code. Devuelve false si el usuario cancela
        private bool AskGCodePoint(string pointName, Vector2 defaultValue, out Vector2 point)
        {
            point = defaultValue;

            EnterNumberWindow enwX = new EnterNumberWindow(defaultValue.X);
            enwX.Title = $"{pointName}: Theoretical X coordinate (G-Code)";
            enwX.Owner = this;
            enwX.ShowDialog();
            if (!enwX.Ok)
                return false;

            EnterNumberWindow enwY = new EnterNumberWindow(defaultValue.Y);
            enwY.Title = $"{pointName}: Theoretical Y coordinate (G-Code)";
            enwY.Owner = this;
            enwY.ShowDialog();
            if (!enwY.Ok)
                return false;

            point = new Vector2(enwX.Value, enwY.Value);
            return true;
        }

        private void ButtonSetP1_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckMachineReady())
                return;

            if (cameraOffsetX == 0 && cameraOffsetY == 0)
            {
                MessageBox.Show("Careful! The spindle-camera offset is not calibrated yet.");
                return;
            }

            // Calculamos la posición real del punto usando la cámara y el offset
            // (antes de abrir los diálogos, por si la máquina se moviera)
            Vector2 work = PointUnderCamera();

            // Pedimos al usuario las coordenadas teóricas de este punto en su G-Code.
            // Si cancela, el punto anterior se queda como estaba
            Vector2 gcode;
            if (!AskGCodePoint("Point 1", p1GCode, out gcode))
                return;

            p1Work = work;
            p1GCode = gcode;
            isP1Set = true;
            MessageBox.Show($"POINT 1 SAVED\n\nReal position (work coordinates): X={p1Work.X:F3}, Y={p1Work.Y:F3}\nG-Code position: X={p1GCode.X:F3}, Y={p1GCode.Y:F3}");
        }

        private void ButtonSetP2_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckMachineReady())
                return;

            if (!isP1Set)
            {
                MessageBox.Show("Please save Point 1 first.");
                return;
            }

            Vector2 work = PointUnderCamera();

            Vector2 gcode;
            if (!AskGCodePoint("Point 2", p2GCode, out gcode))
                return;

            p2Work = work;
            p2GCode = gcode;
            isP2Set = true;
            MessageBox.Show($"POINT 2 SAVED\n\nReal position (work coordinates): X={p2Work.X:F3}, Y={p2Work.Y:F3}\nG-Code position: X={p2GCode.X:F3}, Y={p2GCode.Y:F3}");
        }

        private void ButtonApplyAlignment_Click(object sender, RoutedEventArgs e)
        {
            if (machine.Mode == Machine.OperatingMode.SendFile)
                return;

            if (!isP1Set || !isP2Set)
            {
                MessageBox.Show("Points are missing. Make sure to set Point 1 and Point 2.");
                return;
            }

            if (!ToolPath.ContainsMotion)
            {
                MessageBox.Show("Open a G-Code file first.");
                return;
            }

            // Los puntos son coordenadas del diseño original: aplicarlos a un G-Code ya alineado lo descolocaría
            if (AlignmentApplied)
            {
                MessageBox.Show("The alignment was already applied to this file.\nOpen the G-Code file again if you want to align it again.");
                return;
            }

            // El mapa de alturas corrige Z según la posición XY: si después se mueve el dibujo, cada corrección acaba en otro sitio
            if (HeightMapApplied)
            {
                MessageBox.Show("The height map was already applied to this file and would end up in the wrong place.\nOpen the G-Code file again, align it first and apply the height map afterwards.");
                return;
            }

            // 1. Vectores de P1 a P2, en el G-Code y en la realidad
            Vector2 deltaGCode = p2GCode - p1GCode;
            Vector2 deltaWork = p2Work - p1Work;

            // Con los puntos muy juntos el ángulo no es fiable (y si coinciden, la escala dividiría entre cero)
            if (deltaGCode.Magnitude < 1 || deltaWork.Magnitude < 1)
            {
                MessageBox.Show("Point 1 and Point 2 are too close together. Use two points as far apart as possible.");
                return;
            }

            // 2. La diferencia entre el ángulo real y el teórico es lo que tenemos que girar el archivo
            // (Atan2 da el ángulo en el círculo completo; se normaliza entre -180° y 180°)
            double rotation = Math.Atan2(deltaWork.Y, deltaWork.X) - Math.Atan2(deltaGCode.Y, deltaGCode.X);
            rotation = Math.Atan2(Math.Sin(rotation), Math.Cos(rotation));

            // 3. Escala (deformación del material): si es 1.0, el tamaño es exacto. Si es 1.01, la pieza real es 1% más grande.
            // Solo se corrige si se marca la casilla: con dos puntos medidos con la cámara, casi siempre es error de medida
            double measuredScale = deltaWork.Magnitude / deltaGCode.Magnitude;
            bool correctScale = CheckBoxAlignmentScale.IsChecked == true;
            double scale = correctScale ? measuredScale : 1.0;

            // 4. Se ancla en el punto medio entre P1 y P2: sin corregir la escala, la diferencia de distancia
            // se reparte a partes iguales entre los dos puntos en vez de cargarla entera en P2
            Vector2 centerGCode = (p1GCode + p2GCode) / 2;
            Vector2 centerWork = (p1Work + p2Work) / 2;
            Vector2 shift = centerWork - centerGCode;
            double distanceError = deltaWork.Magnitude - deltaGCode.Magnitude;

            string scaleInfo = correctScale
                ? $"Scale factor: {scale:F4} (corrected)\n"
                : $"Scale factor: not corrected (measured {measuredScale:F4}), each point may be off by {Math.Abs(distanceError) / 2:F3} mm\n";

            // 5. Mostrar los cálculos para confirmar
            MessageBoxResult result = MessageBox.Show(
                $"Alignment calculations:\n\n" +
                $"Rotation: {rotation * 180.0 / Math.PI:F3}°\n" +
                $"Shift: X {shift.X:F3} mm, Y {shift.Y:F3} mm\n" +
                $"Distance P1-P2: {deltaGCode.Magnitude:F3} mm in the G-Code, {deltaWork.Magnitude:F3} mm measured\n" +
                scaleInfo +
                $"\nApply this transformation to the G-Code?\n" +
                $"Don't change the X/Y work zero afterwards, the aligned G-Code depends on it.",
                "Confirm Alignment", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // 6. Girar, mover (y escalar) todas las coordenadas X/Y del G-Code
            try
            {
                machine.SetFile(ToolPath.TransformXY(centerGCode, centerWork, rotation, scale).GetGCode());
                AlignmentApplied = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ButtonRotateCamera_Click(object sender, RoutedEventArgs e)
        {
            cameraRotation = (cameraRotation + 90) % 360;
            Properties.Settings.Default.CameraRotation = cameraRotation;
            Properties.Settings.Default.Save(); // Guarda en memoria
        }

        private void ButtonMirrorCamera_Click(object sender, RoutedEventArgs e)
        {
            cameraMirrored = !cameraMirrored;
            Properties.Settings.Default.CameraMirrored = cameraMirrored;
            Properties.Settings.Default.Save(); // Guarda en memoria
        }
    }
}