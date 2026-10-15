using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker
{
    public enum MacroKind
    {
        Move = 0,
        Down = 1,
        Up = 2
    }

    public class MacroEvent
    {
        public MacroKind Kind;
        public MouseButton Button;
        public int X;
        public int Y;
        public int Delay; // ms since the previous event
    }

    /// <summary>One line of the recorded-macro list in the UI (public for WPF data binding).</summary>
    public class MacroRow
    {
        public string Index { get; set; }
        public string Action { get; set; }
        public string Position { get; set; }
        public string Delay { get; set; }
    }

    internal static class MacroFile
    {
        private const string Header = "ACMACRO 1";

        public static void Save(string path, List<MacroEvent> events)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            foreach (MacroEvent e in events)
            {
                sb.Append((int)e.Kind).Append(',')
                  .Append((int)e.Button).Append(',')
                  .Append(e.X.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Y.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Delay.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, sb.ToString());
        }

        public static List<MacroEvent> Load(string path)
        {
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0 || !lines[0].StartsWith("ACMACRO"))
                throw new FormatException("No es un archivo de macro de AutoClicker.");

            List<MacroEvent> events = new List<MacroEvent>();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string[] p = line.Split(',');
                if (p.Length != 5) throw new FormatException("Línea " + (i + 1) + " no válida.");
                MacroEvent e = new MacroEvent();
                e.Kind = (MacroKind)ParseInt(p[0]);
                e.Button = (MouseButton)ParseInt(p[1]);
                e.X = ParseInt(p[2]);
                e.Y = ParseInt(p[3]);
                e.Delay = Math.Max(0, ParseInt(p[4]));
                events.Add(e);
            }
            return events;
        }

        private static int ParseInt(string s)
        {
            return int.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        public static long TotalDuration(List<MacroEvent> events)
        {
            long total = 0;
            foreach (MacroEvent e in events) total += e.Delay;
            return total;
        }

        /// <summary>Collapses down/up pairs into "click" or "drag" rows for display.</summary>
        public static List<MacroRow> BuildRows(List<MacroEvent> events)
        {
            List<MacroRow> rows = new List<MacroRow>();
            long sincePrevious = 0;
            for (int i = 0; i < events.Count; i++)
            {
                MacroEvent e = events[i];
                sincePrevious += e.Delay;
                if (e.Kind != MacroKind.Down) continue;

                MacroEvent up = null;
                for (int j = i + 1; j < events.Count; j++)
                {
                    if (events[j].Kind == MacroKind.Up && events[j].Button == e.Button)
                    {
                        up = events[j];
                        break;
                    }
                }

                bool drag = up != null && (Math.Abs(up.X - e.X) > 4 || Math.Abs(up.Y - e.Y) > 4);
                MacroRow row = new MacroRow();
                row.Index = (rows.Count + 1).ToString(CultureInfo.CurrentCulture);
                row.Action = (drag ? "Arrastre " : "Clic ") + ButtonName(e.Button);
                row.Position = drag
                    ? e.X + ", " + e.Y + " → " + up.X + ", " + up.Y
                    : e.X + ", " + e.Y;
                row.Delay = "+" + FormatDuration(sincePrevious);
                rows.Add(row);
                sincePrevious = 0;
            }
            return rows;
        }

        public static string ButtonName(MouseButton b)
        {
            return b == MouseButton.Right ? "derecho" : b == MouseButton.Middle ? "central" : "izquierdo";
        }

        public static string FormatDuration(long ms)
        {
            if (ms < 1000) return ms + " ms";
            if (ms < 60000) return (ms / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " s";
            return (ms / 60000) + " min " + ((ms / 1000) % 60) + " s";
        }
    }

    /// <summary>Records physical mouse clicks system-wide through a low-level mouse hook.</summary>
    internal class MacroRecorder
    {
        private IntPtr hook = IntPtr.Zero;
        private Native.LowLevelMouseProc proc; // kept alive while the hook is installed
        private Stopwatch clock;
        private double lastEventMs;
        private bool recordMoves;
        private readonly bool[] held = new bool[3];
        private readonly bool[] ignored = new bool[3];

        public readonly List<MacroEvent> Events = new List<MacroEvent>();

        /// <summary>Clicks that land on this window (the app itself) are not recorded.</summary>
        public IntPtr IgnoreWindow = IntPtr.Zero;

        /// <summary>Raised on the UI thread after each recorded button press/release.</summary>
        public event Action Changed;

        public bool IsRecording
        {
            get { return hook != IntPtr.Zero; }
        }

        public bool Start(bool withMoves)
        {
            if (IsRecording) return true;
            Events.Clear();
            Array.Clear(held, 0, 3);
            Array.Clear(ignored, 0, 3);
            recordMoves = withMoves;
            clock = Stopwatch.StartNew();
            lastEventMs = 0;
            proc = HookProc;
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
            return hook != IntPtr.Zero;
        }

        public void Stop()
        {
            if (!IsRecording) return;
            Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;

            // Never leave a button "pressed" at the end of a recording.
            for (int b = 0; b < 3; b++)
            {
                if (!held[b]) continue;
                MacroEvent last = Events[Events.Count - 1];
                MacroEvent up = new MacroEvent();
                up.Kind = MacroKind.Up;
                up.Button = (MouseButton)b;
                up.X = last.X;
                up.Y = last.Y;
                Events.Add(up);
                held[b] = false;
            }
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.MSLLHOOKSTRUCT info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                if ((info.flags & Native.LLMHF_INJECTED) == 0) // skip our own synthetic clicks
                    Handle(wParam.ToInt32(), info.pt);
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private void Handle(int msg, Native.POINT pt)
        {
            MacroKind kind;
            MouseButton button = MouseButton.Left;
            switch (msg)
            {
                case Native.WM_MOUSEMOVE: kind = MacroKind.Move; break;
                case Native.WM_LBUTTONDOWN: kind = MacroKind.Down; break;
                case Native.WM_LBUTTONUP: kind = MacroKind.Up; break;
                case Native.WM_RBUTTONDOWN: kind = MacroKind.Down; button = MouseButton.Right; break;
                case Native.WM_RBUTTONUP: kind = MacroKind.Up; button = MouseButton.Right; break;
                case Native.WM_MBUTTONDOWN: kind = MacroKind.Down; button = MouseButton.Middle; break;
                case Native.WM_MBUTTONUP: kind = MacroKind.Up; button = MouseButton.Middle; break;
                default: return;
            }

            int b = (int)button;
            double now = clock.Elapsed.TotalMilliseconds;

            if (kind == MacroKind.Down)
            {
                if (IsOverOwnWindow(pt)) { ignored[b] = true; return; }
                held[b] = true;
            }
            else if (kind == MacroKind.Up)
            {
                if (ignored[b]) { ignored[b] = false; return; }
                if (!held[b]) return; // press happened before recording started
                held[b] = false;
            }
            else
            {
                bool dragging = held[0] || held[1] || held[2];
                if (!dragging && !recordMoves) return;
                if (now - lastEventMs < 15) return; // ~60 samples per second is plenty
            }

            MacroEvent e = new MacroEvent();
            e.Kind = kind;
            e.Button = button;
            e.X = pt.X;
            e.Y = pt.Y;
            e.Delay = (int)Math.Round(now - lastEventMs);
            Events.Add(e);
            lastEventMs = now;

            if (kind != MacroKind.Move && Changed != null)
                Changed();
        }

        private bool IsOverOwnWindow(Native.POINT pt)
        {
            if (IgnoreWindow == IntPtr.Zero) return false;
            IntPtr root = Native.GetAncestor(Native.WindowFromPoint(pt), Native.GA_ROOT);
            return root == IgnoreWindow;
        }
    }
}
