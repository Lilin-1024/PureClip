namespace PureClip
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            StartPosition = FormStartPosition.Manual;

            Width = 300;
            Height = 300;

            AllowDrop = true;
            DoubleBuffered = true;

            CenterWindow();
        }

        public void CenterWindow()
        {
            var currentScreen = Screen.FromControl(this);
            var workingArea = currentScreen.WorkingArea;

            Left = (workingArea.Left + (workingArea.Width - Width) / 2);
            Top = (workingArea.Top + (workingArea.Height - Height) / 2);

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
            foreach (string path in files)
            {
                try
                {
                    var newItem = new ClipItem
                    {
                        ImageData = new Bitmap(path),
                        X = 0,
                        Y = 0,
                        Scale = 1.0f
                    };

                    if (newItem.ImageData.Width > 800)
                        newItem.Scale = 800f / newItem.ImageData.Width;

                    _items.Add(newItem);
                }
                catch { }
            }

            UpdateWindowLayout();
        }

        private List<ClipItem> _items = new List<ClipItem>();

        private int _padding = 40;

        private float _contentOffsetX = 0;
        private float _contentOffsetY = 0;
        private void UpdateWindowLayout()
        {
            if (_items.Count == 0) return;

            float minX = _items.Min(i => i.X);
            float minY = _items.Min(i => i.Y);
            float maxX = _items.Max(i => i.X + i.DisplayWidth);
            float maxY = _items.Max(i => i.Y + i.DisplayHeight);

            _contentOffsetX = minX;
            _contentOffsetY = minY;

            int contentWidth = (int)(maxX - minX);
            int contentHeight = (int)(maxY - minY);

            Width = contentWidth + (_padding * 2);
            Height = contentHeight + (_padding * 2);

            CenterWindow();

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            foreach (var item in _items)
            {

                float drawX = item.X - _contentOffsetX + _padding;
                float drawY = item.Y - _contentOffsetY + _padding;

                g.DrawImage(item.ImageData, drawX, drawY, item.DisplayWidth, item.DisplayHeight);
            }
        }

        private ClipItem _draggingItem = null;
        private Point _lastMousePos;

        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            float adjustedX = e.X - _padding + _contentOffsetX;
            float adjustedY = e.Y - _padding + _contentOffsetY;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].Contains(adjustedX, adjustedY))
                {
                    _draggingItem = _items[i];
                    _lastMousePos = e.Location;

                    _items.RemoveAt(i);
                    _items.Add(_draggingItem);
                    break;
                }
            }

            Invalidate();
        }

        private void Form1_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingItem != null && e.Button == MouseButtons.Left)
            {
                float dx = e.X - _lastMousePos.X;
                float dy = e.Y - _lastMousePos.Y;

                _draggingItem.X += dx;
                _draggingItem.Y += dy;

                _lastMousePos = e.Location;

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
