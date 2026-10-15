using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutoClicker
{
    internal class Hotkey
    {
        public Keys Key;
        public Keys Modifiers;

        public Hotkey(Keys key, Keys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public string Text
        {
            get { return Settings.FormatHotkey(Key, Modifiers); }
        }

        public bool SameAs(Hotkey other)
        {
            return other != null && other.Key == Key && other.Modifiers == Modifiers;
        }
    }

    internal class Settings
    {
        public int Hours = 0;
        public int Minutes = 0;
        public int Seconds = 0;
        public int Milliseconds = 100;
        public int Button = 0;
        public bool DoubleClick = false;
        public bool RepeatForever = true;
        public int RepeatCount = 1;
        public bool FixedPosition = false;
        public int X = 0;
        public int Y = 0;

        public Hotkey ClickerHotkey = new Hotkey(Keys.Insert, Keys.None);
        public Hotkey RecordHotkey = new Hotkey(Keys.F7, Keys.None);
        public Hotkey PlayHotkey = new Hotkey(Keys.F9, Keys.None); // F8 is often taken by GPU overlays

        public bool MacroLoop = false;
        public int MacroRepeatCount = 1;
        public int MacroSpeed = 1; // index into 0.5x / 1x / 2x / 4x
        public bool RecordMoves = false;

        public bool TopMost = false;
        public int LastTab = 0;
        public int WindowX = int.MinValue;
        public int WindowY = int.MinValue;

        public static string DataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoClicker");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(DataDir, "settings.ini"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                            d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            catch (Exception) { }

            s.Hours = GetInt(d, "Hours", s.Hours);
            s.Minutes = GetInt(d, "Minutes", s.Minutes);
            s.Seconds = GetInt(d, "Seconds", s.Seconds);
            s.Milliseconds = GetInt(d, "Milliseconds", s.Milliseconds);
            s.Button = GetInt(d, "Button", s.Button);
            s.DoubleClick = GetInt(d, "DoubleClick", 0) != 0;
            s.RepeatForever = GetInt(d, "RepeatForever", 1) != 0;
            s.RepeatCount = GetInt(d, "RepeatCount", s.RepeatCount);
            s.FixedPosition = GetInt(d, "FixedPosition", 0) != 0;
            s.X = GetInt(d, "X", s.X);
            s.Y = GetInt(d, "Y", s.Y);
            s.ClickerHotkey = GetHotkey(d, "HotkeyKey", "HotkeyModifiers", s.ClickerHotkey);
            s.RecordHotkey = GetHotkey(d, "RecordKey", "RecordModifiers", s.RecordHotkey);
            s.PlayHotkey = GetHotkey(d, "PlayKey", "PlayModifiers", s.PlayHotkey);
            s.MacroLoop = GetInt(d, "MacroLoop", 0) != 0;
            s.MacroRepeatCount = GetInt(d, "MacroRepeatCount", s.MacroRepeatCount);
            s.MacroSpeed = GetInt(d, "MacroSpeed", s.MacroSpeed);
            s.RecordMoves = GetInt(d, "RecordMoves", 0) != 0;
            s.TopMost = GetInt(d, "TopMost", 0) != 0;
            s.LastTab = GetInt(d, "LastTab", s.LastTab);
            s.WindowX = GetInt(d, "WindowX", s.WindowX);
            s.WindowY = GetInt(d, "WindowY", s.WindowY);
            return s;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            Append(sb, "Hours", Hours);
            Append(sb, "Minutes", Minutes);
            Append(sb, "Seconds", Seconds);
            Append(sb, "Milliseconds", Milliseconds);
            Append(sb, "Button", Button);
            Append(sb, "DoubleClick", DoubleClick ? 1 : 0);
            Append(sb, "RepeatForever", RepeatForever ? 1 : 0);
            Append(sb, "RepeatCount", RepeatCount);
            Append(sb, "FixedPosition", FixedPosition ? 1 : 0);
            Append(sb, "X", X);
            Append(sb, "Y", Y);
            Append(sb, "HotkeyKey", (int)ClickerHotkey.Key);
            Append(sb, "HotkeyModifiers", (int)ClickerHotkey.Modifiers);
            Append(sb, "RecordKey", (int)RecordHotkey.Key);
            Append(sb, "RecordModifiers", (int)RecordHotkey.Modifiers);
            Append(sb, "PlayKey", (int)PlayHotkey.Key);
            Append(sb, "PlayModifiers", (int)PlayHotkey.Modifiers);
            Append(sb, "MacroLoop", MacroLoop ? 1 : 0);
            Append(sb, "MacroRepeatCount", MacroRepeatCount);
            Append(sb, "MacroSpeed", MacroSpeed);
            Append(sb, "RecordMoves", RecordMoves ? 1 : 0);
            Append(sb, "TopMost", TopMost ? 1 : 0);
            Append(sb, "LastTab", LastTab);
            Append(sb, "WindowX", WindowX);
            Append(sb, "WindowY", WindowY);
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception) { }
        }

        public static string FormatHotkey(Keys key, Keys modifiers)
        {
            List<string> parts = new List<string>();
            if ((modifiers & Keys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & Keys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & Keys.Shift) != 0) parts.Add("Shift");
            if (key != Keys.None) parts.Add(KeyName(key));
            return string.Join(" + ", parts);
        }

        private static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)(key - Keys.D0)).ToString(CultureInfo.InvariantCulture);
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return "Num " + (int)(key - Keys.NumPad0);
            switch (key)
            {
                case Keys.PageUp: return "RePág";
                case Keys.PageDown: return "AvPág";
                case Keys.Enter: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Space: return "Espacio";
                case Keys.Back: return "Retroceso";
                case Keys.Delete: return "Supr";
                case Keys.Insert: return "INS";
                case Keys.Home: return "Inicio";
                case Keys.End: return "Fin";
                case Keys.CapsLock: return "Bloq Mayús";
                case Keys.Scroll: return "Bloq Despl";
                case Keys.Pause: return "Pausa";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
            }
            return key.ToString();
        }

        private static Hotkey GetHotkey(Dictionary<string, string> d, string keyName, string modName, Hotkey fallback)
        {
            Keys key = (Keys)GetInt(d, keyName, (int)fallback.Key);
            Keys mods = (Keys)GetInt(d, modName, (int)fallback.Modifiers);
            return key == Keys.None ? fallback : new Hotkey(key, mods);
        }

        private static int GetInt(Dictionary<string, string> d, string key, int fallback)
        {
            string v;
            int result;
            if (d.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                return result;
            return fallback;
        }

        private static void Append(StringBuilder sb, string key, int value)
        {
            sb.Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
    }
}
