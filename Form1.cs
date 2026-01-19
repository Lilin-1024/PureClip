namespace PureClip
{
    public partial class Form1 : Form
    {
        private Rectangle _minCanvas;

        public int _CanvasSize = 300;
        public Form1()
        {
            InitializeComponent();

            this.Text = "";
            this.ShowIcon = false;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = false;

            this.Bounds = Screen.PrimaryScreen.Bounds;

            var screen = Screen.PrimaryScreen.WorkingArea;

            _minCanvas = new Rectangle((screen.Width - _CanvasSize) / 2, (screen.Height - _CanvasSize) / 2, _CanvasSize, _CanvasSize);

            Color ghostColor = Color.FromArgb(255, 1, 1, 1); 
            this.BackColor = ghostColor;
            this.TransparencyKey = ghostColor;

            this.AllowDrop = true;
            this.DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                //允许最小化
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00020000;
                return cp;
            }
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

                    if (_items.Count == 0)
                    {
                        Rectangle currentBox = GetCurrentCanvasBounds();
                        newItem.X = currentBox.Left + (currentBox.Width - newItem.DisplayWidth) / 2;
                        newItem.Y = currentBox.Top + (currentBox.Height - newItem.DisplayHeight) / 2;
                    }
                    else
                    {
                        newItem.X = dropPoint.X - (newItem.DisplayWidth / 2);
                        newItem.Y = dropPoint.Y - (newItem.DisplayHeight / 2);
                    }
                    _items.Add(newItem);
                }
                catch { }
            }

            Invalidate();
        }

        private List<ClipItem> _items = new List<ClipItem>();

        private float _canvasPadding = 20f;
        private const float MIN_PADDING = 20f;
        private Rectangle GetCurrentCanvasBounds()
        {
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            int margin = (int)_canvasPadding;

            if (_items.Count == 0)
            {
                return _minCanvas;
            }

            float minX = _items.Min(i => i.X);
            float minY = _items.Min(i => i.Y);
            float maxX = _items.Max(i => i.X + i.DisplayWidth);
            float maxY = _items.Max(i => i.Y + i.DisplayHeight);

            int targetW = (int)(maxX - minX + margin * 2);
            int targetH = (int)(maxY - minY + margin * 2);

            if (targetW > screen.Width) targetW = screen.Width;
            if (targetH > screen.Height) targetH = screen.Height;

            int centerX = (int)((minX + maxX) / 2);
            int centerY = (int)((minY + maxY) / 2);

            int x = centerX - targetW / 2;
            int y = centerY - targetH / 2;

            if (x < screen.Left) x = screen.Left;
            if (y < screen.Top) y = screen.Top;
            if (x + targetW > screen.Right) x = screen.Right - targetW;
            if (y + targetH > screen.Bottom) y = screen.Bottom - targetH;

            return new Rectangle(x, y, targetW, targetH);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            Rectangle activeCanvas = GetCurrentCanvasBounds();

            using (SolidBrush brush = new SolidBrush(Color.FromArgb(45, 45, 45)))
            {
                g.FillRectangle(brush, activeCanvas);
            }

            using (Pen borderPen = new Pen(Color.Black, 1))
            {
                g.DrawRectangle(borderPen,
                    activeCanvas.X,
                    activeCanvas.Y,
                    activeCanvas.Width - 1,
                    activeCanvas.Height - 1);
            }

            foreach (var item in _items)
            {
                g.DrawImage(item.ImageData, item.X, item.Y, item.DisplayWidth, item.DisplayHeight);
                if (item == _selectedItem)
                {
                    using (Pen p = new Pen(Color.FromArgb(150, 100, 100, 255), 2))
                    {
                        g.DrawRectangle(p, item.X - 1, item.Y - 1, item.DisplayWidth + 2, item.DisplayHeight + 2);
                    }
                }
            }
        }

        private ClipItem _draggingItem = null;
        private ClipItem _selectedItem = null;
        private Point _lastMousePos;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _selectedItem = null;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].Contains(e.X, e.Y))
                {
                    _selectedItem = _items[i];
                    _draggingItem = _items[i];
                    _lastMousePos = Cursor.Position;

                    _items.RemoveAt(i);
                    _items.Add(_selectedItem);

                    Invalidate();
                    return;
                }
            }
            if (GetCurrentCanvasBounds().Contains(e.Location))
            {
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_draggingItem != null && e.Button == MouseButtons.Left)
            {
                Point currentMousePos = Cursor.Position;
                float dx = currentMousePos.X - _lastMousePos.X;
                float dy = currentMousePos.Y - _lastMousePos.Y;

                float nextX = _draggingItem.X + dx;
                float nextY = _draggingItem.Y + dy;

                Rectangle screen = Screen.PrimaryScreen.WorkingArea;

                int wallMargin = 10;

                if (nextX < screen.Left + wallMargin) nextX = screen.Left +wallMargin;
                if (nextX + _draggingItem.DisplayWidth + wallMargin > screen.Right)
                    nextX = screen.Right - _draggingItem.DisplayWidth - wallMargin;

                if (nextY < screen.Top + wallMargin) nextY = screen.Top + wallMargin;

                if (nextY + _draggingItem.DisplayHeight + wallMargin > screen.Bottom)
                    nextY = screen.Bottom - _draggingItem.DisplayHeight - wallMargin;

                _draggingItem.X = nextX;
                _draggingItem.Y = nextY;

                _lastMousePos = currentMousePos;

                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _draggingItem = null;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float zoomFactor = e.Delta > 0 ? 1.1f : 0.9f;

            if (_selectedItem != null)
            {
                float oldWidth = _selectedItem.DisplayWidth;
                float oldHeight = _selectedItem.DisplayHeight;

                _selectedItem.Scale *= zoomFactor;

                if (_selectedItem.Scale < 0.01f) _selectedItem.Scale = 0.01f;

                _selectedItem.X -= (_selectedItem.DisplayWidth - oldWidth) / 2f;
                _selectedItem.Y -= (_selectedItem.DisplayHeight - oldHeight) / 2f;
            }
            else
            {
                float oldPadding = _canvasPadding;
                _canvasPadding *= zoomFactor>1f? (zoomFactor + 0.1f): (zoomFactor - 0.1f);

                if (_canvasPadding < MIN_PADDING) _canvasPadding = MIN_PADDING;

                int oldW = _minCanvas.Width;
                int oldH = _minCanvas.Height;

                int newW = (int)(oldW * zoomFactor);
                int newH = (int)(oldH * zoomFactor);

                if (newW > _CanvasSize && newH > _CanvasSize)
                {
                    int dx = (newW - oldW) / 2;
                    int dy = (newH - oldH) / 2;

                    _minCanvas = new Rectangle(_minCanvas.X - dx, _minCanvas.Y - dy, newW, newH);
                }
            }

            Invalidate();
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
