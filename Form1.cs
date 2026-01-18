namespace PureClip
{
    public partial class Form1 : Form
    {
        private Rectangle _minCanvas;
        public Form1()
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = false;

            this.Bounds = Screen.PrimaryScreen.Bounds;

            var screen = Screen.PrimaryScreen.WorkingArea;
            _minCanvas = new Rectangle((screen.Width - 300) / 2, (screen.Height - 300) / 2, 300, 300);

            Color ghostColor = Color.FromArgb(255, 1, 1, 1); 
            this.BackColor = ghostColor;
            this.TransparencyKey = ghostColor;

            this.AllowDrop = true;
            this.DoubleBuffered = true;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            Point dropPoint = new Point(e.X, e.Y);

            foreach (string path in files)
            {
                try
                {
                    Bitmap bmp = new Bitmap(path);
                    var newItem = new ClipItem { ImageData = bmp };

                    if (bmp.Width > 800) newItem.Scale = 800f / bmp.Width;

                    newItem.X = dropPoint.X - (newItem.DisplayWidth / 2);
                    newItem.Y = dropPoint.Y - (newItem.DisplayHeight / 2);

                    _items.Add(newItem);
                }
                catch { }
            }

            Invalidate();
        }

        private List<ClipItem> _items = new List<ClipItem>();

        private Rectangle GetCurrentCanvasBounds()
        {
            float minX = _minCanvas.Left;
            float minY = _minCanvas.Top;
            float maxX = _minCanvas.Right;
            float maxY = _minCanvas.Bottom;

            foreach (var item in _items)
            {
                if (item.X < minX) minX = item.X;
                if (item.Y < minY) minY = item.Y;
                if (item.X + item.DisplayWidth > maxX) maxX = item.X + item.DisplayWidth;
                if (item.Y + item.DisplayHeight > maxY) maxY = item.Y + item.DisplayHeight;
            }

            int margin = 20; 
            return new Rectangle(
                (int)minX - margin,
                (int)minY - margin,
                (int)(maxX - minX) + margin * 2,
                (int)(maxY - minY) + margin * 2
            );
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            Rectangle activeCanvas = GetCurrentCanvasBounds();

            using (SolidBrush brush = new SolidBrush(Color.FromArgb(45, 45, 45)))
            {
                g.FillRectangle(brush, activeCanvas);
            }

            foreach (var item in _items)
            {
                g.DrawImage(item.ImageData, item.X, item.Y, item.DisplayWidth, item.DisplayHeight);
            }
        }

        private ClipItem _draggingItem = null;
        private Point _lastMousePos;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].Contains(e.X, e.Y))
                {
                    _draggingItem = _items[i];
                    _lastMousePos = Cursor.Position;

                    var item = _items[i];
                    _items.RemoveAt(i);
                    _items.Add(item);

                    Invalidate();
                    return;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_draggingItem != null && e.Button == MouseButtons.Left)
            {
                Point currentPos = Cursor.Position;
                _draggingItem.X += (currentPos.X - _lastMousePos.X);
                _draggingItem.Y += (currentPos.Y - _lastMousePos.Y);

                _lastMousePos = currentPos;
                Invalidate(); // ÖØÐÂ»­Í¼
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _draggingItem = null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Application.Exit();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            const int HTCLIENT = 1;

            if (m.Msg == WM_NCHITTEST)
            {
                int x = (int)(m.LParam.ToInt32() & 0xFFFF);
                int y = (int)((m.LParam.ToInt32() >> 16) & 0xFFFF);
                Point clientPoint = this.PointToClient(new Point(x, y));

                Rectangle activeCanvas = GetCurrentCanvasBounds();
                if (activeCanvas.Contains(clientPoint))
                {
                    m.Result = (IntPtr)HTCLIENT;
                    return;
                }
                else
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        public class ClipItem
        {
            public Bitmap ImageData;
            public float X;
            public float Y;
            public float Scale = 1.0f;

            public float DisplayWidth => ImageData.Width * Scale;
            public float DisplayHeight => ImageData.Height * Scale;
            public bool Contains(float mouseX, float mouseY)
            {
                return mouseX > X && mouseX <= X + DisplayWidth &&
                       mouseY > Y && mouseY <= Y + DisplayHeight;
            }
        }
    }
}
