using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>
    /// Full-screen dimmed overlay across every monitor. The next click picks a screen point.
    /// </summary>
    internal class PickerForm : Form
    {
        private readonly InfoTip tip = new InfoTip();

        // Esc is polled instead of relying on keyboard focus: Windows does not always
        // let the overlay become the foreground window, and then it never sees key presses.
        private readonly Timer escPoll = new Timer();

        public Point PickedPoint { get; private set; }

        public PickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.25;
            Cursor = Cursors.Cross;
            KeyPreview = true;

            escPoll.Interval = 30;
            escPoll.Tick += delegate
            {
                if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) // VK_ESCAPE held
                    DialogResult = DialogResult.Cancel;
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            tip.Show(this);
            UpdateTip();
            escPoll.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateTip();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                PickedPoint = Cursor.Position;
                DialogResult = DialogResult.OK;
            }
            else
            {
                DialogResult = DialogResult.Cancel;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
                DialogResult = DialogResult.Cancel;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            escPoll.Stop();
            escPoll.Dispose();
            tip.Close();
            base.OnFormClosed(e);
        }

        private void UpdateTip()
        {
            Point p = Cursor.Position;
            tip.SetText("X: " + p.X + "    Y: " + p.Y + "\nClic izquierdo para elegir · Esc para cancelar");

            Rectangle area = Screen.FromPoint(p).Bounds;
            int x = p.X + 18;
            int y = p.Y + 22;
            if (x + tip.Width > area.Right) x = p.X - tip.Width - 12;
            if (y + tip.Height > area.Bottom) y = p.Y - tip.Height - 12;
            tip.Location = new Point(x, y);
        }

        /// <summary>Small opaque label that follows the cursor and lets clicks pass through.</summary>
        private class InfoTip : Form
        {
            private readonly Label label = new Label();

            public InfoTip()
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                BackColor = Color.FromArgb(32, 32, 32);
                Padding = new Padding(1);

                label.AutoSize = true;
                label.ForeColor = Color.White;
                label.Font = new Font("Segoe UI", 9.5F);
                label.Padding = new Padding(8, 6, 8, 6);
                Controls.Add(label);
            }

            public void SetText(string text)
            {
                label.Text = text;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED |
                                  Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                    return cp;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Opacity = 0.95; // makes the layered window visible
            }
        }
    }
}
