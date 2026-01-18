namespace PureClip
{
    public partial class Form1 : Form
    {
        private Rectangle _minCanvas;
        public Form1()
        {
            InitializeComponent();
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;

            var screen = Screen.PrimaryScreen.WorkingArea;
            _minCanvas = new Rectangle((screen.Width - 300) / 2, (screen.Height - 300) / 2, 300, 300);

            Bounds = _minCanvas;

            AllowDrop = true;
            DoubleBuffered = true;
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
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
                catch {}
            }

            UpdateWindowLayout();
        }

        private List<ClipItem> _items = new List<ClipItem>();

        private int _padding = 40;
        private void UpdateWindowLayout()
        {
            if (_items.Count == 0) return;

            float minX = _minCanvas.Left;
            float minY = _minCanvas.Top;
            float maxX = _minCanvas.Right;
            float maxY = _minCanvas.Bottom;

            foreach (var item in _items)
            {
                minX = Math.Min(minX, item.X - _padding);
                minY = Math.Min(minY, item.Y - _padding);
                maxX = Math.Max(maxX, item.X + item.DisplayWidth + _padding);
                maxY = Math.Max(maxY, item.Y + item.DisplayHeight + _padding);
            }

            int newX = (int)Math.Floor(minX);
            int newY = (int)Math.Floor(minY);
            int newW = (int)Math.Ceiling(maxX - minX);
            int newH = (int)Math.Ceiling(maxY - minY);

            if (newX != Left || newY != Top || newW != Width || newH != Height)
            {
                this.SetBounds(newX, newY, newW, newH);
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            int curLeft = this.Left;
            int curTop = this.Top;

            foreach (var item in _items)
            {
                float drawX = item.X - curLeft;
                float drawY = item.Y - curTop;
                g.DrawImage(item.ImageData, drawX, drawY, item.DisplayWidth, item.DisplayHeight);
            }
        }

        private ClipItem _draggingItem = null;
        private Point _lastMousePos;

        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            float screenMouseX = e.X + this.Left;
            float screenMouseY = e.Y + this.Top;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].Contains(screenMouseX, screenMouseY))
                {
                    _draggingItem = _items[i];
                    _lastMousePos = Cursor.Position;

                    _items.RemoveAt(i);
                    _items.Add(_draggingItem);
                    break;
                }
            }
        }

        private void Form1_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingItem != null && e.Button == MouseButtons.Left)
            {
                Point currentMousePos = Cursor.Position;
                float dx = currentMousePos.X - _lastMousePos.X;
                float dy = currentMousePos.Y - _lastMousePos.Y;

                _draggingItem.X += dx;
                _draggingItem.Y += dy;

                _lastMousePos = currentMousePos;

                UpdateWindowLayout();
            }
        }

        private void Form1_MouseUp(object sender, MouseEventArgs e)
        {
            _draggingItem = null;
        }
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
