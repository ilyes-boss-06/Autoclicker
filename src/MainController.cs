using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Keys = System.Windows.Forms.Keys;

namespace AutoClicker
{
    internal class MainController
    {
        private enum Mode { Idle, Clicking, Recording, Playing }

        private const int HkClickerId = 1, HkRecordId = 2, HkPlayId = 3;
        private static readonly double[] Speeds = { 0.5, 1, 2, 4 };

        private class ClickJob
        {
            public double IntervalMs;
            public MouseButton Button;
            public bool DoubleClick;
            public long Limit; // 0 = until stopped
            public bool FixedPosition;
            public int X, Y;
        }

        private class PlayJob
        {
            public MacroEvent[] Events;
            public long Repeat; // 0 = loop forever
            public double Speed;
        }

        public readonly Window Window;
        private readonly Settings settings;
        private IntPtr hwnd;
        private Mode mode = Mode.Idle;

        // Background worker (clicker or playback)
        private Thread worker;
        private volatile bool running;
        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        private long counter;       // clicks done, or playback repetitions completed
        private long counterLimit;
        private string lastMessage;

        private readonly MacroRecorder recorder = new MacroRecorder();
        private List<MacroEvent> macro = new List<MacroEvent>();
        private bool macroRefreshPending;

        private readonly Dictionary<TextBox, int[]> numRanges = new Dictionary<TextBox, int[]>();
        private readonly List<string> hotkeyFailures = new List<string>();
        private int capturingHotkey; // id of the hotkey being edited, 0 = none
        private Button captureChip;
        private readonly DispatcherTimer uiTimer = new DispatcherTimer();
        private int blinkTick;

        private static readonly Brush DotIdle = Frozen(0x6B, 0x6B, 0x78);
        private static readonly Brush DotGreen = Frozen(0x3D, 0xD6, 0x8C);
        private static readonly Brush DotRed = Frozen(0xEE, 0x4F, 0x5A);
        private static readonly Brush DotBlue = Frozen(0x5B, 0x7C, 0xFF);
        private static readonly Brush Warn = Frozen(0xF5, 0xB8, 0x41);
        private static readonly Brush SubText = Frozen(0x98, 0x98, 0xA6);

        // Named elements from MainWindow.xaml
        private RadioButton tabClicker, tabMacro, tabSettings;
        private FrameworkElement panelClicker, panelMacro, panelSettings, clickerOptions, playbackCard;
        private TextBox txtHours, txtMinutes, txtSeconds, txtMs, txtRepeat, txtX, txtY, txtMacroRepeat;
        private RadioButton rbLeft, rbRight, rbMiddle, rbSingle, rbDouble, rbForever, rbCount, rbCurrent, rbFixed;
        private RadioButton rbMacroCount, rbMacroLoop;
        private RadioButton[] rbSpeeds;
        private Button btnPick, btnStart, btnStop, btnRecord, btnPlay, btnMacroStop, btnOpen, btnSave, btnClear;
        private Button hkClicker, hkRecord, hkPlay;
        private TextBlock startKey, stopKey, recordKey, playKey, recordLabel, macroSummary, macroEmpty;
        private TextBlock hotkeyHint, statusText, dataPath;
        private CheckBox chkRecordMoves, chkTopMost;
        private ListBox macroList;
        private Ellipse statusDot;

        private static string LastMacroPath
        {
            get { return System.IO.Path.Combine(Settings.DataDir, "ultima-grabacion.acmacro"); }
        }

        public MainController()
        {
            settings = Settings.Load();
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoClicker.MainWindow.xaml"))
                Window = (Window)XamlReader.Load(s);

            FindElements();
            LoadLogo();
            WireEvents();
            ApplySettings();
            LoadLastMacro();

            uiTimer.Interval = TimeSpan.FromMilliseconds(100);
            uiTimer.Tick += delegate { UpdateStatus(); };

            Window.SourceInitialized += OnSourceInitialized;
            Window.Loaded += delegate { KeepOnScreen(); };
            Window.Closing += OnClosing;
            Window.PreviewKeyDown += OnPreviewKeyDown;
            Window.Deactivated += delegate { if (capturingHotkey != 0) EndCapture(); };

            SetMode(Mode.Idle);
        }

        // ═══════════════════════════ setup ═══════════════════════════

        private T F<T>(string name) where T : class
        {
            T element = Window.FindName(name) as T;
            if (element == null) throw new InvalidOperationException("Falta el elemento '" + name + "' en MainWindow.xaml");
            return element;
        }

        private void FindElements()
        {
            tabClicker = F<RadioButton>("TabClicker");
            tabMacro = F<RadioButton>("TabMacro");
            tabSettings = F<RadioButton>("TabSettings");
            panelClicker = F<FrameworkElement>("PanelClicker");
            panelMacro = F<FrameworkElement>("PanelMacro");
            panelSettings = F<FrameworkElement>("PanelSettings");
            clickerOptions = F<FrameworkElement>("ClickerOptions");
            playbackCard = F<FrameworkElement>("PlaybackCard");

            txtHours = F<TextBox>("TxtHours");
            txtMinutes = F<TextBox>("TxtMinutes");
            txtSeconds = F<TextBox>("TxtSeconds");
            txtMs = F<TextBox>("TxtMs");
            txtRepeat = F<TextBox>("TxtRepeat");
            txtX = F<TextBox>("TxtX");
            txtY = F<TextBox>("TxtY");
            txtMacroRepeat = F<TextBox>("TxtMacroRepeat");

            rbLeft = F<RadioButton>("RbLeft");
            rbRight = F<RadioButton>("RbRight");
            rbMiddle = F<RadioButton>("RbMiddle");
            rbSingle = F<RadioButton>("RbSingle");
            rbDouble = F<RadioButton>("RbDouble");
            rbForever = F<RadioButton>("RbForever");
            rbCount = F<RadioButton>("RbCount");
            rbCurrent = F<RadioButton>("RbCurrent");
            rbFixed = F<RadioButton>("RbFixed");
            rbMacroCount = F<RadioButton>("RbMacroCount");
            rbMacroLoop = F<RadioButton>("RbMacroLoop");
            rbSpeeds = new[] { F<RadioButton>("RbSpeed05"), F<RadioButton>("RbSpeed1"), F<RadioButton>("RbSpeed2"), F<RadioButton>("RbSpeed4") };

            btnPick = F<Button>("BtnPick");
            btnStart = F<Button>("BtnStart");
            btnStop = F<Button>("BtnStop");
            btnRecord = F<Button>("BtnRecord");
            btnPlay = F<Button>("BtnPlay");
            btnMacroStop = F<Button>("BtnMacroStop");
            btnOpen = F<Button>("BtnOpen");
            btnSave = F<Button>("BtnSave");
            btnClear = F<Button>("BtnClear");
            hkClicker = F<Button>("HkClicker");
            hkRecord = F<Button>("HkRecord");
            hkPlay = F<Button>("HkPlay");

            startKey = F<TextBlock>("StartKey");
            stopKey = F<TextBlock>("StopKey");
            recordKey = F<TextBlock>("RecordKey");
            playKey = F<TextBlock>("PlayKey");
            recordLabel = F<TextBlock>("RecordLabel");
            macroSummary = F<TextBlock>("MacroSummary");
            macroEmpty = F<TextBlock>("MacroEmpty");
            hotkeyHint = F<TextBlock>("HotkeyHint");
            statusText = F<TextBlock>("StatusText");
            dataPath = F<TextBlock>("DataPath");

            chkRecordMoves = F<CheckBox>("ChkRecordMoves");
            chkTopMost = F<CheckBox>("ChkTopMost");
            macroList = F<ListBox>("MacroList");
            statusDot = F<Ellipse>("StatusDot");
        }

        private void LoadLogo()
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoClicker.logo.png");
            if (s == null) return;
            System.Windows.Media.Imaging.BitmapImage img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.StreamSource = s;
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            s.Dispose();
            F<Image>("Logo").Source = img;
        }

        private void WireEvents()
        {
            tabClicker.Checked += delegate { ShowTab(0); };
            tabMacro.Checked += delegate { ShowTab(1); };
            tabSettings.Checked += delegate { ShowTab(2); };

            SetupNumber(txtHours, 0, 999);
            SetupNumber(txtMinutes, 0, 59);
            SetupNumber(txtSeconds, 0, 59);
            SetupNumber(txtMs, 0, 999);
            SetupNumber(txtRepeat, 1, 999999999);
            SetupNumber(txtX, -32768, 32767);
            SetupNumber(txtY, -32768, 32767);
            SetupNumber(txtMacroRepeat, 1, 999999);

            txtRepeat.GotKeyboardFocus += delegate { rbCount.IsChecked = true; };
            txtX.GotKeyboardFocus += delegate { rbFixed.IsChecked = true; };
            txtY.GotKeyboardFocus += delegate { rbFixed.IsChecked = true; };
            txtMacroRepeat.GotKeyboardFocus += delegate { rbMacroCount.IsChecked = true; };

            btnPick.Click += delegate { PickLocation(); };
            btnStart.Click += delegate { StartClicking(); };
            btnStop.Click += delegate { StopWorker(); };
            btnRecord.Click += delegate { if (mode == Mode.Recording) StopRecording(); else StartRecording(); };
            btnPlay.Click += delegate { StartPlayback(); };
            btnMacroStop.Click += delegate { if (mode == Mode.Recording) StopRecording(); else StopWorker(); };
            btnOpen.Click += delegate { OpenMacro(); };
            btnSave.Click += delegate { SaveMacro(); };
            btnClear.Click += delegate { ClearMacro(); };

            hkClicker.Click += delegate { BeginCapture(HkClickerId, hkClicker); };
            hkRecord.Click += delegate { BeginCapture(HkRecordId, hkRecord); };
            hkPlay.Click += delegate { BeginCapture(HkPlayId, hkPlay); };

            chkTopMost.Checked += delegate { Window.Topmost = true; };
            chkTopMost.Unchecked += delegate { Window.Topmost = false; };

            recorder.Changed += delegate
            {
                // Called from inside the mouse hook: keep it cheap and refresh the list afterwards.
                if (macroRefreshPending) return;
                macroRefreshPending = true;
                Window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    macroRefreshPending = false;
                    RefreshMacroList(recorder.Events, true);
                }), DispatcherPriority.Background);
            };
        }

        private void SetupNumber(TextBox box, int min, int max)
        {
            numRanges[box] = new[] { min, max };
            box.PreviewTextInput += delegate(object s, TextCompositionEventArgs e)
            {
                foreach (char c in e.Text)
                {
                    if (!char.IsDigit(c) && !(c == '-' && min < 0))
                    {
                        e.Handled = true;
                        return;
                    }
                }
            };
            box.LostKeyboardFocus += delegate { SetNumber(box, GetNumber(box)); };
            box.GotKeyboardFocus += delegate { box.SelectAll(); };
            box.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (!box.IsKeyboardFocusWithin)
                {
                    box.Focus();
                    e.Handled = true;
                }
            };
            box.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                SetNumber(box, GetNumber(box) + (e.Delta > 0 ? 1 : -1));
                e.Handled = true;
            };
            DataObject.AddPastingHandler(box, delegate(object s, DataObjectPastingEventArgs e)
            {
                string text = e.DataObject.GetData(typeof(string)) as string;
                int dummy;
                if (text == null || !int.TryParse(text.Trim(), out dummy)) e.CancelCommand();
            });
        }

        private int GetNumber(TextBox box)
        {
            int[] range = numRanges[box];
            int value;
            if (!int.TryParse(box.Text.Trim(), out value)) value = range[0];
            return Math.Max(range[0], Math.Min(range[1], value));
        }

        private void SetNumber(TextBox box, long value)
        {
            int[] range = numRanges[box];
            box.Text = Math.Max(range[0], Math.Min(range[1], value)).ToString();
        }

        private void ApplySettings()
        {
            SetNumber(txtHours, settings.Hours);
            SetNumber(txtMinutes, settings.Minutes);
            SetNumber(txtSeconds, settings.Seconds);
            SetNumber(txtMs, settings.Milliseconds);
            SetNumber(txtRepeat, settings.RepeatCount);
            SetNumber(txtX, settings.X);
            SetNumber(txtY, settings.Y);
            SetNumber(txtMacroRepeat, settings.MacroRepeatCount);

            (settings.Button == 1 ? rbRight : settings.Button == 2 ? rbMiddle : rbLeft).IsChecked = true;
            (settings.DoubleClick ? rbDouble : rbSingle).IsChecked = true;
            (settings.RepeatForever ? rbForever : rbCount).IsChecked = true;
            (settings.FixedPosition ? rbFixed : rbCurrent).IsChecked = true;
            (settings.MacroLoop ? rbMacroLoop : rbMacroCount).IsChecked = true;
            rbSpeeds[Math.Max(0, Math.Min(3, settings.MacroSpeed))].IsChecked = true;
            chkRecordMoves.IsChecked = settings.RecordMoves;
            chkTopMost.IsChecked = settings.TopMost;
            dataPath.Text = "Ajustes y grabaciones: " + Settings.DataDir;

            if (settings.LastTab == 1) tabMacro.IsChecked = true;
            else if (settings.LastTab == 2) tabSettings.IsChecked = true;

            double x = settings.WindowX, y = settings.WindowY;
            if (x >= SystemParameters.VirtualScreenLeft && y >= SystemParameters.VirtualScreenTop &&
                x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
                y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
            {
                Window.WindowStartupLocation = WindowStartupLocation.Manual;
                Window.Left = x;
                Window.Top = y;
            }

            UpdateHotkeyLabels();
        }

        private void ReadSettingsFromUi()
        {
            settings.Hours = GetNumber(txtHours);
            settings.Minutes = GetNumber(txtMinutes);
            settings.Seconds = GetNumber(txtSeconds);
            settings.Milliseconds = GetNumber(txtMs);
            settings.RepeatCount = GetNumber(txtRepeat);
            settings.X = GetNumber(txtX);
            settings.Y = GetNumber(txtY);
            settings.MacroRepeatCount = GetNumber(txtMacroRepeat);
            settings.Button = rbRight.IsChecked == true ? 1 : rbMiddle.IsChecked == true ? 2 : 0;
            settings.DoubleClick = rbDouble.IsChecked == true;
            settings.RepeatForever = rbForever.IsChecked == true;
            settings.FixedPosition = rbFixed.IsChecked == true;
            settings.MacroLoop = rbMacroLoop.IsChecked == true;
            settings.MacroSpeed = 1;
            for (int i = 0; i < rbSpeeds.Length; i++)
                if (rbSpeeds[i].IsChecked == true) settings.MacroSpeed = i;
            settings.RecordMoves = chkRecordMoves.IsChecked == true;
            settings.TopMost = chkTopMost.IsChecked == true;
            settings.LastTab = tabMacro.IsChecked == true ? 1 : tabSettings.IsChecked == true ? 2 : 0;
            if (Window.WindowState == WindowState.Normal)
            {
                settings.WindowX = (int)Window.Left;
                settings.WindowY = (int)Window.Top;
            }
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(Window).Handle;
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            recorder.IgnoreWindow = hwnd;
            ApplyDarkTitleBar();
            RegisterHotkeys();
            UpdateStatus();
        }

        private void KeepOnScreen()
        {
            Rect area = SystemParameters.WorkArea;
            if (Window.Top + Window.ActualHeight > area.Bottom)
                Window.Top = Math.Max(area.Top, area.Bottom - Window.ActualHeight);
            if (Window.Left + Window.ActualWidth > area.Right)
                Window.Left = Math.Max(area.Left, area.Right - Window.ActualWidth);
        }

        private void ApplyDarkTitleBar()
        {
            // Windows 11: dark caption matching the window background, rounded corners.
            // These calls are silently ignored on older versions of Windows.
            int on = 1, caption = 0x001B1717, text = 0x00F0EDED, border = 0x00342C2C, round = 2;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_CAPTION_COLOR, ref caption, 4);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_TEXT_COLOR, ref text, 4);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref border, 4);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (recorder.IsRecording) StopRecording();
            StopWorker();
            ReadSettingsFromUi();
            settings.Save();
            UnregisterHotkeys();
        }

        private void ShowTab(int index)
        {
            panelClicker.Visibility = index == 0 ? Visibility.Visible : Visibility.Hidden;
            panelMacro.Visibility = index == 1 ? Visibility.Visible : Visibility.Hidden;
            panelSettings.Visibility = index == 2 ? Visibility.Visible : Visibility.Hidden;
            if (index != 2 && capturingHotkey != 0) EndCapture();
        }

        // ═══════════════════════════ modes & status ═══════════════════════════

        private void SetMode(Mode newMode)
        {
            mode = newMode;
            bool idle = mode == Mode.Idle;

            clickerOptions.IsEnabled = idle;
            btnStart.IsEnabled = idle;
            btnStop.IsEnabled = mode == Mode.Clicking;

            chkRecordMoves.IsEnabled = idle;
            playbackCard.IsEnabled = idle;
            btnRecord.IsEnabled = idle || mode == Mode.Recording;
            recordLabel.Text = mode == Mode.Recording ? "Terminar" : "Grabar";
            btnPlay.IsEnabled = idle && macro.Count > 0;
            btnMacroStop.IsEnabled = mode == Mode.Recording || mode == Mode.Playing;
            btnOpen.IsEnabled = idle;
            btnSave.IsEnabled = idle && macro.Count > 0;
            btnClear.IsEnabled = idle && macro.Count > 0;

            hkClicker.IsEnabled = idle;
            hkRecord.IsEnabled = idle;
            hkPlay.IsEnabled = idle;

            uiTimer.IsEnabled = !idle;
            Window.Title = mode == Mode.Clicking ? "AutoClicker — haciendo clic"
                         : mode == Mode.Recording ? "AutoClicker — grabando"
                         : mode == Mode.Playing ? "AutoClicker — reproduciendo"
                         : "AutoClicker";
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            long n = Interlocked.Read(ref counter);
            statusDot.Opacity = 1;

            switch (mode)
            {
                case Mode.Clicking:
                    statusDot.Fill = DotGreen;
                    statusText.Text = "Haciendo clic · " + n.ToString("N0") +
                                      (counterLimit > 0 ? " de " + counterLimit.ToString("N0") : "") +
                                      " clics — " + settings.ClickerHotkey.Text + " para detener";
                    return;

                case Mode.Recording:
                    statusDot.Fill = DotRed;
                    statusDot.Opacity = (++blinkTick / 5) % 2 == 0 ? 1 : 0.25;
                    int actions = MacroFile.BuildRows(recorder.Events).Count;
                    statusText.Text = "Grabando · " + actions + (actions == 1 ? " acción" : " acciones") +
                                      " — " + settings.RecordHotkey.Text + " para terminar";
                    return;

                case Mode.Playing:
                    statusDot.Fill = DotBlue;
                    statusText.Text = "Reproduciendo · repetición " + (n + 1).ToString("N0") +
                                      (counterLimit > 0 ? " de " + counterLimit.ToString("N0") : " (en bucle)") +
                                      " — " + settings.PlayHotkey.Text + " para detener";
                    return;
            }

            if (hotkeyFailures.Count > 0)
            {
                statusDot.Fill = Warn;
                statusText.Text = "No se pudo registrar " + string.Join(", ", hotkeyFailures) + " (¿lo usa otra aplicación?)";
            }
            else if (lastMessage != null)
            {
                statusDot.Fill = DotIdle;
                statusText.Text = lastMessage;
            }
            else
            {
                statusDot.Fill = DotIdle;
                statusText.Text = "Listo · " + settings.ClickerHotkey.Text + " clicker · " +
                                  settings.RecordHotkey.Text + " grabar · " + settings.PlayHotkey.Text + " reproducir";
            }
        }

        // ═══════════════════════════ auto clicker ═══════════════════════════

        private void StartClicking()
        {
            if (mode != Mode.Idle) return;
            ReadSettingsFromUi();

            double interval = settings.Hours * 3600000.0 + settings.Minutes * 60000.0 +
                              settings.Seconds * 1000.0 + settings.Milliseconds;
            ClickJob job = new ClickJob
            {
                IntervalMs = Math.Max(1.0, interval),
                Button = (MouseButton)settings.Button,
                DoubleClick = settings.DoubleClick,
                Limit = settings.RepeatForever ? 0 : settings.RepeatCount,
                FixedPosition = settings.FixedPosition,
                X = settings.X,
                Y = settings.Y
            };
            counterLimit = job.Limit;
            StartWorker(ClickLoop, job, Mode.Clicking);
        }

        private void ClickLoop(object state)
        {
            ClickJob job = (ClickJob)state;
            Native.timeBeginPeriod(1);
            try
            {
                Stopwatch clock = Stopwatch.StartNew();
                double next = 0;
                long count = 0;
                while (running)
                {
                    if (job.FixedPosition) Native.SetCursorPos(job.X, job.Y);
                    Native.Click(job.Button, job.DoubleClick);

                    count++;
                    Interlocked.Exchange(ref counter, count);
                    if (job.Limit > 0 && count >= job.Limit) break;

                    // Absolute timeline so the rate doesn't drift, but no catch-up burst after a stall.
                    next += job.IntervalMs;
                    double now = clock.Elapsed.TotalMilliseconds;
                    if (now - next > 250) next = now;
                    if (!WaitUntil(clock, next)) break;
                }
            }
            finally
            {
                Native.timeEndPeriod(1);
                FinishWorker();
            }
        }

        // ═══════════════════════════ record & playback ═══════════════════════════

        private void StartRecording()
        {
            if (mode != Mode.Idle) return;
            tabMacro.IsChecked = true;
            if (!recorder.Start(chkRecordMoves.IsChecked == true))
            {
                lastMessage = "No se pudo iniciar la grabación (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
                UpdateStatus();
                return;
            }
            blinkTick = 0;
            SetMode(Mode.Recording);
            RefreshMacroList(recorder.Events, true);
        }

        private void StopRecording()
        {
            if (mode != Mode.Recording) return;
            recorder.Stop();
            macro = new List<MacroEvent>(recorder.Events);
            try { MacroFile.Save(LastMacroPath, macro); }
            catch (Exception) { }

            int actions = MacroFile.BuildRows(macro).Count;
            lastMessage = "Grabación terminada · " + actions + (actions == 1 ? " acción" : " acciones") +
                          " — " + settings.PlayHotkey.Text + " para reproducir";
            RefreshMacroList(macro, false);
            SetMode(Mode.Idle);
        }

        private void StartPlayback()
        {
            if (mode != Mode.Idle || macro.Count == 0) return;
            ReadSettingsFromUi();
            PlayJob job = new PlayJob
            {
                Events = macro.ToArray(),
                Repeat = settings.MacroLoop ? 0 : settings.MacroRepeatCount,
                Speed = Speeds[Math.Max(0, Math.Min(3, settings.MacroSpeed))]
            };
            counterLimit = job.Repeat;
            StartWorker(PlayLoop, job, Mode.Playing);
        }

        private void PlayLoop(object state)
        {
            PlayJob job = (PlayJob)state;
            bool[] held = new bool[3];
            Native.timeBeginPeriod(1);
            try
            {
                long reps = 0;
                while (running && (job.Repeat == 0 || reps < job.Repeat))
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    double t = 0;
                    foreach (MacroEvent e in job.Events)
                    {
                        t += e.Delay / job.Speed;
                        if (!WaitUntil(clock, t)) break;

                        Native.SetCursorPos(e.X, e.Y);
                        if (e.Kind == MacroKind.Down)
                        {
                            Native.SendButton(e.Button, true);
                            held[(int)e.Button] = true;
                        }
                        else if (e.Kind == MacroKind.Up)
                        {
                            Native.SendButton(e.Button, false);
                            held[(int)e.Button] = false;
                        }
                    }
                    if (!running) break;
                    reps++;
                    Interlocked.Exchange(ref counter, reps);
                    if (t < 20 && !WaitUntil(clock, 20)) break; // never spin on an (almost) empty macro
                }
            }
            finally
            {
                // A stop in the middle of a drag must not leave a button pressed.
                for (int b = 0; b < 3; b++)
                    if (held[b]) Native.SendButton((MouseButton)b, false);
                Native.timeEndPeriod(1);
                FinishWorker();
            }
        }

        private void RefreshMacroList(List<MacroEvent> events, bool scrollToEnd)
        {
            List<MacroRow> rows = MacroFile.BuildRows(events);
            macroList.ItemsSource = rows;
            macroEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            macroSummary.Text = rows.Count == 0 ? ""
                : rows.Count + (rows.Count == 1 ? " acción · " : " acciones · ") +
                  MacroFile.FormatDuration(MacroFile.TotalDuration(events));
            if (scrollToEnd && rows.Count > 0) macroList.ScrollIntoView(rows[rows.Count - 1]);
        }

        private void LoadLastMacro()
        {
            try
            {
                if (File.Exists(LastMacroPath)) macro = MacroFile.Load(LastMacroPath);
            }
            catch (Exception)
            {
                macro = new List<MacroEvent>();
            }
            RefreshMacroList(macro, false);
        }

        private void OpenMacro()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Macro de AutoClicker (*.acmacro)|*.acmacro|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(Window) != true) return;
            try
            {
                macro = MacroFile.Load(dlg.FileName);
                MacroFile.Save(LastMacroPath, macro);
                lastMessage = "Macro cargada · " + System.IO.Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window, "No se pudo abrir la macro:\n" + ex.Message, "AutoClicker",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RefreshMacroList(macro, false);
            SetMode(Mode.Idle);
        }

        private void SaveMacro()
        {
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Filter = "Macro de AutoClicker (*.acmacro)|*.acmacro";
            dlg.DefaultExt = ".acmacro";
            dlg.FileName = "macro.acmacro";
            if (dlg.ShowDialog(Window) != true) return;
            try
            {
                MacroFile.Save(dlg.FileName, macro);
                lastMessage = "Macro guardada · " + System.IO.Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window, "No se pudo guardar la macro:\n" + ex.Message, "AutoClicker",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateStatus();
        }

        private void ClearMacro()
        {
            macro = new List<MacroEvent>();
            try { File.Delete(LastMacroPath); }
            catch (Exception) { }
            lastMessage = "Grabación borrada";
            RefreshMacroList(macro, false);
            SetMode(Mode.Idle);
        }

        // ═══════════════════════════ worker plumbing ═══════════════════════════

        private void StartWorker(ParameterizedThreadStart body, object job, Mode workerMode)
        {
            Interlocked.Exchange(ref counter, 0);
            running = true;
            stopEvent.Reset();
            worker = new Thread(body);
            worker.IsBackground = true;
            worker.Priority = ThreadPriority.AboveNormal;
            worker.Start(job);
            SetMode(workerMode);
        }

        private void StopWorker()
        {
            running = false;
            stopEvent.Set();
        }

        private void FinishWorker()
        {
            running = false;
            try { Window.Dispatcher.BeginInvoke(new Action(OnWorkerFinished)); }
            catch (Exception) { }
        }

        private void OnWorkerFinished()
        {
            long n = Interlocked.Read(ref counter);
            if (mode == Mode.Clicking)
                lastMessage = "Detenido · " + n.ToString("N0") + (n == 1 ? " clic" : " clics");
            else if (mode == Mode.Playing)
                lastMessage = "Reproducción terminada · " + n.ToString("N0") + (n == 1 ? " repetición" : " repeticiones");
            worker = null;
            SetMode(Mode.Idle);
        }

        /// <summary>Waits until the clock reaches targetMs. Returns false if stopped meanwhile.</summary>
        private bool WaitUntil(Stopwatch clock, double targetMs)
        {
            while (running)
            {
                double remaining = targetMs - clock.Elapsed.TotalMilliseconds;
                if (remaining <= 0) return true;
                if (remaining > 20) stopEvent.WaitOne((int)Math.Min(remaining - 10, 200));
                else if (remaining > 1.5) Thread.Sleep(1);
                else Thread.Yield();
            }
            return false;
        }

        // ═══════════════════════════ pick location ═══════════════════════════

        private void PickLocation()
        {
            UnregisterHotkeys();
            try
            {
                using (PickerForm picker = new PickerForm())
                {
                    // Hide only once the overlay is up, so focus passes to it instead of another app.
                    picker.Shown += delegate { Window.Hide(); };
                    if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        SetNumber(txtX, picker.PickedPoint.X);
                        SetNumber(txtY, picker.PickedPoint.Y);
                        rbFixed.IsChecked = true;
                    }
                }
            }
            finally
            {
                Window.Show();
                Window.Activate();
                RegisterHotkeys();
            }
        }

        // ═══════════════════════════ hotkeys ═══════════════════════════

        private IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                OnHotkey(wParam.ToInt32());
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnHotkey(int id)
        {
            if (capturingHotkey != 0) return;
            if (id == HkClickerId)
            {
                if (mode == Mode.Clicking) StopWorker();
                else if (mode == Mode.Idle) StartClicking();
            }
            else if (id == HkRecordId)
            {
                if (mode == Mode.Recording) StopRecording();
                else if (mode == Mode.Idle) StartRecording();
            }
            else if (id == HkPlayId)
            {
                if (mode == Mode.Playing) StopWorker();
                else if (mode == Mode.Idle) StartPlayback();
            }
        }

        private Hotkey HotkeyById(int id)
        {
            return id == HkClickerId ? settings.ClickerHotkey : id == HkRecordId ? settings.RecordHotkey : settings.PlayHotkey;
        }

        private void RegisterHotkeys()
        {
            if (hwnd == IntPtr.Zero) return;
            UnregisterHotkeys();
            hotkeyFailures.Clear();
            foreach (int id in new[] { HkClickerId, HkRecordId, HkPlayId })
            {
                Hotkey hk = HotkeyById(id);
                uint mods = Native.MOD_NOREPEAT;
                if ((hk.Modifiers & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
                if ((hk.Modifiers & Keys.Alt) != 0) mods |= Native.MOD_ALT;
                if ((hk.Modifiers & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
                if (!Native.RegisterHotKey(hwnd, id, mods, (uint)hk.Key))
                    hotkeyFailures.Add(hk.Text);
            }
            UpdateStatus();
        }

        private void UnregisterHotkeys()
        {
            if (hwnd == IntPtr.Zero) return;
            Native.UnregisterHotKey(hwnd, HkClickerId);
            Native.UnregisterHotKey(hwnd, HkRecordId);
            Native.UnregisterHotKey(hwnd, HkPlayId);
        }

        private void UpdateHotkeyLabels()
        {
            startKey.Text = settings.ClickerHotkey.Text;
            stopKey.Text = settings.ClickerHotkey.Text;
            recordKey.Text = settings.RecordHotkey.Text;
            playKey.Text = settings.PlayHotkey.Text;
            hkClicker.Content = settings.ClickerHotkey.Text;
            hkRecord.Content = settings.RecordHotkey.Text;
            hkPlay.Content = settings.PlayHotkey.Text;
            macroEmpty.Text = "Aún no hay nada grabado.\nPulsa Grabar (" + settings.RecordHotkey.Text +
                              ") y haz clic donde quieras;\nvuelve a pulsar " + settings.RecordHotkey.Text + " para terminar.";
            hotkeyHint.Foreground = SubText;
            hotkeyHint.Text = "Haz clic en un atajo y pulsa la nueva tecla o combinación (Ctrl, Alt, Shift + tecla). " +
                              "Funcionan aunque la ventana no esté activa.";
            UpdateStatus();
        }

        private void BeginCapture(int id, Button chip)
        {
            if (capturingHotkey != 0) EndCapture();
            capturingHotkey = id;
            captureChip = chip;
            UnregisterHotkeys(); // otherwise the current hotkey would be swallowed by the system
            chip.Content = "Pulsa una tecla…";
            chip.Background = DotBlue;
            hotkeyHint.Foreground = SubText;
            hotkeyHint.Text = "Pulsa la nueva combinación. Esc para cancelar.";
        }

        private void EndCapture()
        {
            capturingHotkey = 0;
            if (captureChip != null) captureChip.ClearValue(Control.BackgroundProperty);
            captureChip = null;
            RegisterHotkeys();
            UpdateHotkeyLabels();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (capturingHotkey == 0) return;
            e.Handled = true;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                EndCapture();
                return;
            }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin ||
                key == Key.ImeProcessed || key == Key.None)
                return;

            Keys mods = Keys.None;
            ModifierKeys m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Control) != 0) mods |= Keys.Control;
            if ((m & ModifierKeys.Alt) != 0) mods |= Keys.Alt;
            if ((m & ModifierKeys.Shift) != 0) mods |= Keys.Shift;
            Hotkey hk = new Hotkey((Keys)KeyInterop.VirtualKeyFromKey(key), mods);

            string[] names = { null, "el clicker", "grabar", "reproducir" };
            for (int id = 1; id <= 3; id++)
            {
                if (id != capturingHotkey && HotkeyById(id).SameAs(hk))
                {
                    hotkeyHint.Foreground = Warn;
                    hotkeyHint.Text = hk.Text + " ya está asignado a " + names[id] + ". Prueba con otra tecla o pulsa Esc.";
                    return;
                }
            }

            if (capturingHotkey == HkClickerId) settings.ClickerHotkey = hk;
            else if (capturingHotkey == HkRecordId) settings.RecordHotkey = hk;
            else settings.PlayHotkey = hk;
            lastMessage = null;
            EndCapture();
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
