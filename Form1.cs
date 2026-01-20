using System.Drawing.Printing;

namespace PureClip
{
    public partial class Form1 : Form
    {
        private Rectangle _activeCanvas;

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
            _activeCanvas = new Rectangle((screen.Width - _CanvasSize) / 2, (screen.Height - _CanvasSize) / 2, _CanvasSize, _CanvasSize);

            Color ghostColor = Color.FromArgb(255, 1, 1, 1);
            this.BackColor = ghostColor;
            this.TransparencyKey = ghostColor;
            this.AllowDrop = true;
            this.DoubleBuffered = true;

            // 虚线框
            System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
            animationTimer.Interval = 100;
            animationTimer.Tick += (s, e) => {
                if (_currentMode == ToolMode.RectSelect && !_selectionRect.IsEmpty)
                {
                    _dashOffset++;
                    if (_dashOffset > 10) _dashOffset = 0;
                    Invalidate();
                }
            };
            animationTimer.Start();
        }

        public enum ToolMode
        {
            Pointer,
            RectSelect,
            MagicWand,
            Lasso
        }

        private ToolMode _currentMode = ToolMode.Pointer;
        private RectangleF _selectionRect = RectangleF.Empty;
        private PointF _selectionStart;
        private bool _isSelecting = false;
        private float _dashOffset = 0;

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
                    using (Bitmap rawBmp = new Bitmap(path))
                    {
                        Bitmap bmp = new Bitmap(rawBmp.Width, rawBmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                        bmp.SetResolution(96, 96);

                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(rawBmp, 0, 0, rawBmp.Width, rawBmp.Height);
                        }

                        var newItem = new ClipItem(bmp);
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

                        int margin = 20;
                        Rectangle newItemRect = new Rectangle(
                            (int)newItem.X - margin,
                            (int)newItem.Y - margin,
                            (int)newItem.DisplayWidth + margin * 2,
                            (int)newItem.DisplayHeight + margin * 2
                        );

                        if (_items.Count == 1)
                        {
                            _activeCanvas = newItemRect;
                        }
                        else
                        {
                            _activeCanvas = Rectangle.Union(_activeCanvas, newItemRect);
                        }
                    }
                }
                catch { }
            }

            Invalidate();
        }

        private List<ClipItem> _items = new List<ClipItem>();
        private Rectangle GetCurrentCanvasBounds()
        {
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            return Rectangle.Intersect(_activeCanvas, screen);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;

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
                g.DrawImage(item.PreviewImage, item.X, item.Y, item.DisplayWidth, item.DisplayHeight);

                if (_selectedItems.Contains(item))
                {
                    using (Pen p = new Pen(Color.FromArgb(150, 100, 100, 255), 2))
                    {
                        g.DrawRectangle(p, item.X - 1, item.Y - 1, item.DisplayWidth + 2, item.DisplayHeight + 2);
                    }
                }
            }

            //绘制虚线框
            if (!_selectionRect.IsEmpty)
            {
                using (Pen antsPen = new Pen(Color.White, 1))
                {
                    antsPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    antsPen.DashOffset = _dashOffset;

                    e.Graphics.DrawRectangle(Pens.Black, _selectionRect.X, _selectionRect.Y, _selectionRect.Width, _selectionRect.Height);
                    e.Graphics.DrawRectangle(antsPen, _selectionRect.X, _selectionRect.Y, _selectionRect.Width, _selectionRect.Height);
                }

                /*using (SolidBrush brush = new SolidBrush(Color.FromArgb(20, 0, 100, 100)))
                {
                    e.Graphics.FillRectangle(brush, _selectionRect);
                }*/
            }
        }

        private ClipItem _draggingItem = null;
        private List<ClipItem> _selectedItems = new List<ClipItem>();
        private Point _lastMousePos;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_currentMode == ToolMode.Pointer)
            {
                ClipItem clickedItem = null;
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (_items[i].Contains(e.X, e.Y))
                    {
                        clickedItem = _items[i];
                        break;
                    }
                }

                if (clickedItem != null)
                {
                    if (ModifierKeys == Keys.Shift)
                    {
                        if (_selectedItems.Contains(clickedItem))
                            _selectedItems.Remove(clickedItem);
                        else
                            _selectedItems.Add(clickedItem);
                    }
                    else
                    {
                        if (!_selectedItems.Contains(clickedItem))
                        {
                            _selectedItems.Clear();
                            _selectedItems.Add(clickedItem);
                        }
                    }

                    _draggingItem = clickedItem;
                    _lastMousePos = Cursor.Position;

                    foreach (var item in _selectedItems.ToList())
                    {
                        _items.Remove(item);
                        _items.Add(item);
                    }
                }
                else
                {
                    if (ModifierKeys != Keys.Shift) _selectedItems.Clear();
                }
                Invalidate();
            }
            else if (_currentMode == ToolMode.RectSelect)
            {
                _isSelecting = true;
                _selectionStart = e.Location;
                _selectionRect = RectangleF.Empty;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_currentMode == ToolMode.Pointer && _draggingItem != null && e.Button == MouseButtons.Left)
            {
                Point currentMousePos = Cursor.Position;
                float dx = currentMousePos.X - _lastMousePos.X;
                float dy = currentMousePos.Y - _lastMousePos.Y;

                Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                int wallMargin = 10;

                float minX = _selectedItems.Min(i => i.X) + dx;
                float minY = _selectedItems.Min(i => i.Y) + dy;
                float maxX = _selectedItems.Max(i => i.X + i.DisplayWidth) + dx;
                float maxY = _selectedItems.Max(i => i.Y + i.DisplayHeight) + dy;

                if (minX < screen.Left + wallMargin)
                    dx = (screen.Left + wallMargin) - _selectedItems.Min(i => i.X);

                if (maxX > screen.Right - wallMargin)
                    dx = (screen.Right - wallMargin) - _selectedItems.Max(i => i.X + i.DisplayWidth);

                if (minY < screen.Top + wallMargin)
                    dy = (screen.Top + wallMargin) - _selectedItems.Min(i => i.Y);

                if (maxY > screen.Bottom - wallMargin)
                    dy = (screen.Bottom - wallMargin) - _selectedItems.Max(i => i.Y + i.DisplayHeight);

                foreach (var item in _selectedItems)
                {
                    item.X += dx;
                    item.Y += dy;
                }

                int margin = 20;
                float groupMinX = _selectedItems.Min(i => i.X) - margin;
                float groupMinY = _selectedItems.Min(i => i.Y) - margin;
                float groupMaxX = _selectedItems.Max(i => i.X + i.DisplayWidth) + margin;
                float groupMaxY = _selectedItems.Max(i => i.Y + i.DisplayHeight) + margin;

                Rectangle groupRect = new Rectangle(
                    (int)groupMinX,
                    (int)groupMinY,
                    (int)(groupMaxX - groupMinX),
                    (int)(groupMaxY - groupMinY)
                );

                _activeCanvas = Rectangle.Union(_activeCanvas, groupRect);

                _lastMousePos = currentMousePos;
                Invalidate();
            }
            else if (_currentMode == ToolMode.RectSelect && _isSelecting)
            {
                float x = Math.Min(_selectionStart.X, e.X);
                float y = Math.Min(_selectionStart.Y, e.Y);
                float width = Math.Abs(e.X - _selectionStart.X);
                float height = Math.Abs(e.Y - _selectionStart.Y);

                _selectionRect = new RectangleF(x, y, width, height);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _draggingItem = null;
            _isSelecting = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float zoomFactor = e.Delta > 0 ? 1.1f : 0.9f;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            int margin = 20;

            if (_selectedItems.Count > 0)
            {
                foreach (var item in _selectedItems)
                {
                    float oldWidth = item.DisplayWidth;
                    float oldHeight = item.DisplayHeight;

                    float nextScale = item.Scale * zoomFactor;
                    if (nextScale < 0.01f) nextScale = 0.01f;

                    // 屏幕尺寸限制
                    float nextWidth = item.ImageData.Width * nextScale;
                    float nextHeight = item.ImageData.Height * nextScale;
                    int maxAllowedW = screen.Width - margin * 2;
                    int maxAllowedH = screen.Height - margin * 2;

                    if (nextWidth > maxAllowedW || nextHeight > maxAllowedH)
                    {
                        float scaleW = (float)maxAllowedW / item.ImageData.Width;
                        float scaleH = (float)maxAllowedH / item.ImageData.Height;
                        nextScale = Math.Min(scaleW, scaleH);
                    }

                    item.Scale = nextScale;

                    item.X -= (item.DisplayWidth - oldWidth) / 2f;
                    item.Y -= (item.DisplayHeight - oldHeight) / 2f;

                    if (item.X < screen.Left + margin) item.X = screen.Left + margin;
                    if (item.Y < screen.Top + margin) item.Y = screen.Top + margin;
                    if (item.X + item.DisplayWidth > screen.Right - margin)
                        item.X = screen.Right - margin - item.DisplayWidth;
                    if (item.Y + item.DisplayHeight > screen.Bottom - margin)
                        item.Y = screen.Bottom - margin - item.DisplayHeight;

                    Rectangle itemRect = new Rectangle(
                        (int)item.X - margin,
                        (int)item.Y - margin,
                        (int)item.DisplayWidth + margin * 2,
                        (int)item.DisplayHeight + margin * 2
                    );
                    _activeCanvas = Rectangle.Union(_activeCanvas, itemRect);
                }
            }
            else
            {
                int oldW = _activeCanvas.Width;
                int oldH = _activeCanvas.Height;
                int newW = (int)(oldW * zoomFactor);
                int newH = (int)(oldH * zoomFactor);

                if (newW > screen.Width) newW = screen.Width;
                if (newH > screen.Height) newH = screen.Height;

                int cx = _activeCanvas.X + oldW / 2;
                int cy = _activeCanvas.Y + oldH / 2;
                int nextX = cx - newW / 2;
                int nextY = cy - newH / 2;

                if (_items.Count > 0)
                {
                    float minX = _items.Min(i => i.X) - margin;
                    float minY = _items.Min(i => i.Y) - margin;
                    float maxX = _items.Max(i => i.X + i.DisplayWidth) + margin;
                    float maxY = _items.Max(i => i.Y + i.DisplayHeight) + margin;

                    if (nextX > minX) nextX = (int)minX;
                    if (nextY > minY) nextY = (int)minY;
                    if (nextX + newW < maxX) newW = (int)(maxX - nextX);
                    if (nextY + newH < maxY) newH = (int)(maxY - nextY);
                }
                else
                {
                    if (newW < _CanvasSize) newW = _CanvasSize;
                    if (newH < _CanvasSize) newH = _CanvasSize;
                    nextX = cx - newW / 2;
                    nextY = cy - newH / 2;
                }

                if (nextX < screen.Left) nextX = screen.Left;
                if (nextY < screen.Top) nextY = screen.Top;
                if (nextX + newW > screen.Right) nextX = screen.Right - newW;
                if (nextY + newH > screen.Bottom) nextY = screen.Bottom - newH;

                _activeCanvas = new Rectangle(nextX, nextY, newW, newH);
            }

            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Application.Exit();

            if (e.KeyCode == Keys.W)
            {
                _currentMode = (ToolMode)(((int)_currentMode + 1) % 4);

                //_selectionRect = RectangleF.Empty;
                Invalidate();
            }

            if (e.KeyCode == Keys.D)
            {
                _selectionRect = RectangleF.Empty;
                Invalidate();
            }

            if (e.KeyCode == Keys.Enter)
            {
                if (_currentMode == ToolMode.RectSelect && !_selectionRect.IsEmpty)
                {
                    HandleSelection();
                }
                _currentMode = ToolMode.Pointer;
                Invalidate();
            }

            if (e.KeyCode == Keys.C && _currentMode == ToolMode.RectSelect)
            {
                HandleSelection(true);
                _currentMode = ToolMode.Pointer;
                Invalidate();
            }
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

        private void HandleSelection(bool isCopyOnly = false)
        {
            if (_selectionRect.IsEmpty) return;

            List<ClipItem> targets = new List<ClipItem>();
            if (_selectedItems.Count > 0)
            {
                targets.AddRange(_selectedItems);
            }
            else
            {
                foreach (var item in _items)
                {
                    RectangleF itemRect = new RectangleF(item.X, item.Y, item.DisplayWidth, item.DisplayHeight);
                    if (itemRect.IntersectsWith(_selectionRect)) targets.Add(item);
                }
            }

            List<ClipItem> newCreatedItems = new List<ClipItem>();

            foreach (var target in targets)
            {
                Rectangle sourceRect = target.GetInternalRect(_selectionRect);
                if (sourceRect.Width <= 0 || sourceRect.Height <= 0) continue;

                Bitmap croppedBmp = target.ImageData.Clone(sourceRect, target.ImageData.PixelFormat);

                if (!isCopyOnly)
                {
                    using (Graphics g = Graphics.FromImage(target.ImageData))
                    {
                        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        g.FillRectangle(Brushes.Transparent, sourceRect);
                    }
                    target.UpdatePreview();
                }

                var newItem = new ClipItem(croppedBmp);
                newItem.X = target.X + sourceRect.X * target.Scale;
                newItem.Y = target.Y + sourceRect.Y * target.Scale;
                newItem.Scale = target.Scale;

                newCreatedItems.Add(newItem);
            }

            _items.AddRange(newCreatedItems);

            _selectedItems.Clear();
            _selectedItems.AddRange(newCreatedItems);

            _selectionRect = RectangleF.Empty;
            Invalidate();
        }

    }

    public class ClipItem
    {
        public Bitmap ImageData;
        public Bitmap PreviewImage;
        public float X;
        public float Y;
        public float Scale = 1.0f;

        public float DisplayWidth => ImageData.Width * Scale;
        public float DisplayHeight => ImageData.Height * Scale;

        public ClipItem(Bitmap original)
        {
            ImageData = original;
            PreviewImage = CreatePreview(original, 2000);
        }

        private Bitmap CreatePreview(Bitmap source, int maxSide)
        {
            if (source.Width <= maxSide && source.Height <= maxSide)
                return new Bitmap(source);

            float ratio = Math.Min((float)maxSide / source.Width, (float)maxSide / source.Height);
            int newW = (int)(source.Width * ratio);
            int newH = (int)(source.Height * ratio);

            Bitmap bmp = new Bitmap(newW, newH);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, newW, newH);
            }
            return bmp;
        }

        public bool Contains(float mouseX, float mouseY)
        {
            return mouseX > X && mouseX <= X + DisplayWidth &&
                   mouseY > Y && mouseY <= Y + DisplayHeight;
        }

        public Rectangle GetInternalRect(RectangleF screenRect)
        {
            float localX = (screenRect.X - this.X) / this.Scale;
            float localY = (screenRect.Y - this.Y) / this.Scale;
            float localW = screenRect.Width / this.Scale;
            float localH = screenRect.Height / this.Scale;

            Rectangle imageBounds = new Rectangle(0, 0, ImageData.Width, ImageData.Height);
            Rectangle result = Rectangle.Intersect(imageBounds, new Rectangle((int)localX, (int)localY, (int)localW, (int)localH));

            return result;
        }

        public void UpdatePreview()
        {
            if (PreviewImage != null) PreviewImage.Dispose();
            PreviewImage = CreatePreview(ImageData, 2000);
        }
    }
}
