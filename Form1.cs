using System.Drawing.Printing;
using System.Drawing.Drawing2D;

namespace PureClip
{
    public partial class Form1 : Form
    {
        private Rectangle _activeCanvas;

        private LinkedList<HistoryState> _undoStack = new LinkedList<HistoryState>();
        private LinkedList<HistoryState> _redoStack = new LinkedList<HistoryState>();
        private const int MAX_HISTORY = 30;

        private bool _isProcessingUndoRedo = false;

        public int _CanvasSize = 300;
        private int _CanvasMargin = 20;

        private Color _canvasColor = Color.FromArgb(45, 45, 45);
        private ContextMenuStrip _contextMenu;
        private ContextMenuStrip _contextMenuSelection;
        private ContextMenuStrip _contextMenuItem;
        private ToolStripMenuItem _menuItemMode;

        private bool _isRotating = false;
        private PointF _rotationCenter;
        private float _startMouseAngle;
        private Dictionary<ClipItem, float> _initialItemRotations = new Dictionary<ClipItem, float>();

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
                if (_selectionPath.PointCount > 0)
                {
                    _dashOffset++;
                    if (_dashOffset > 10) _dashOffset = 0;
                    Invalidate();
                }
            };
            animationTimer.Start();

            InitializeContextMenu();
            UpdateCursor();
        }

        public enum ToolMode
        {
            Pointer,
            RectSelect,
            Lasso
        }

        private ToolMode _currentMode = ToolMode.Pointer;
        private PointF _selectionStart;
        private bool _isSelecting = false;
        private float _dashOffset = 0;

        private GraphicsPath _selectionPath = new GraphicsPath();
        private List<PointF> _currentLassoPoints = new List<PointF>();
        private RectangleF _tempRect = RectangleF.Empty;

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

        private void InitializeContextMenu()
        {
            //画布菜单
            _contextMenu = new ContextMenuStrip();
            _contextMenu.RenderMode = ToolStripRenderMode.System;

            _menuItemMode = new ToolStripMenuItem("Mode (W)");
            var subPointer = new ToolStripMenuItem("Pointer", null, (s, e) => SwitchMode(ToolMode.Pointer));
            var subRect = new ToolStripMenuItem("Rectangle Select", null, (s, e) => SwitchMode(ToolMode.RectSelect));
            var subLasso = new ToolStripMenuItem("Lasso", null, (s, e) => SwitchMode(ToolMode.Lasso));
            _menuItemMode.DropDownItems.Add(subPointer);
            _menuItemMode.DropDownItems.Add(subRect);
            _menuItemMode.DropDownItems.Add(subLasso);

            var itemSettings = new ToolStripMenuItem("Canvas Settings");

            var subColor = new ToolStripMenuItem("Canvas Color...", null, (s, e) => {
                ColorDialog cd = new ColorDialog();
                cd.Color = _canvasColor;
                cd.AllowFullOpen = true;
                if (cd.ShowDialog() == DialogResult.OK) { _canvasColor = cd.Color; Invalidate(); }
            });

            var subMargin = new ToolStripMenuItem("Canvas Margin...", null, (s, e) => {
                using (Form inputForm = new Form())
                {
                    inputForm.Width = 250;
                    inputForm.Height = 180;
                    inputForm.Text = "Set Margin";
                    inputForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                    inputForm.StartPosition = FormStartPosition.CenterScreen;
                    inputForm.MaximizeBox = false;
                    inputForm.MinimizeBox = false;

                    inputForm.TopMost = this.TopMost;

                    Label lbl = new Label() { Left = 20, Top = 10, Text = "Pixel Amount:", AutoSize = true };
                    NumericUpDown input = new NumericUpDown() { Left = 20, Top = 40, Width = 190, Minimum = 1, Maximum = 500, Value = _CanvasMargin };
                    Button btnOk = new Button() { Text = "OK", Left = 140, Top = 82, Height = 28,Width = 70, DialogResult = DialogResult.OK};

                    inputForm.Controls.Add(lbl);
                    inputForm.Controls.Add(input);
                    inputForm.Controls.Add(btnOk);
                    inputForm.AcceptButton = btnOk;

                    if (inputForm.ShowDialog() == DialogResult.OK)
                    {
                        _CanvasMargin = (int)input.Value;
                        Invalidate();
                    }
                }
            });

            var subTopMost = new ToolStripMenuItem("Always on Top");
            subTopMost.Checked = this.TopMost;
            subTopMost.Click += (s, e) => {
                this.TopMost = !this.TopMost;
                subTopMost.Checked = this.TopMost;
            };

            itemSettings.DropDownItems.Add(subColor);
            itemSettings.DropDownItems.Add(new ToolStripSeparator());
            itemSettings.DropDownItems.Add(subMargin);
            itemSettings.DropDownItems.Add(new ToolStripSeparator());
            itemSettings.DropDownItems.Add(subTopMost);

            var itemExport = new ToolStripMenuItem("Export Image...", null, (s, e) => {
                if (ShowExportDialog(out ExportOptions options, out string path))
                {
                    RenderExportImage(_activeCanvas, _items, null, options, path);
                }
            });

            var itemExit = new ToolStripMenuItem("Exit", null, (s, e) => {
                var result = MessageBox.Show(
                    this,
                    "Are you sure you want to exit?",
                    "Exit Confirmation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    Application.Exit();
                }
            });

            _contextMenu.Items.Add(_menuItemMode);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(itemSettings);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(itemExport);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(itemExit);

            //选区菜单
            _contextMenuSelection = new ContextMenuStrip();
            _contextMenuSelection.RenderMode = ToolStripRenderMode.System;

            var itemCopy = new ToolStripMenuItem("Copy Selection (C)", null, (s, e) => {
                HandleSelection(true);
                AfterSelectionAction();
            });

            var itemCut = new ToolStripMenuItem("Cut (Enter)", null, (s, e) => {
                HandleSelection(false);
                AfterSelectionAction();
            });

            var itemDelete = new ToolStripMenuItem("Delete (Del)", null, (s, e) => {
                DeleteInsideSelection();
                AfterSelectionAction();
            });

            var itemCrop = new ToolStripMenuItem("Crop (K)", null, (s, e) => {
                KeepOnlySelection();
                AfterSelectionAction();
            });

            var itemDeselect = new ToolStripMenuItem("Deselect (D)", null, (s, e) => {
                _selectionPath.Reset();
                _tempRect = RectangleF.Empty;
                _currentLassoPoints.Clear();
                Invalidate();
                Invalidate();
            });

            var itemExportSelection = new ToolStripMenuItem("Export Selection...", null, (s, e) => {
                RectangleF bounds = _selectionPath.GetBounds();

                if (ShowExportDialog(out ExportOptions options, out string path))
                {
                    RenderExportImage(bounds, _items, _selectionPath, options, path);
                    AfterSelectionAction();
                }
            });

            _contextMenuSelection.Items.Add(itemCopy);
            _contextMenuSelection.Items.Add(new ToolStripSeparator());
            _contextMenuSelection.Items.Add(itemCut);
            _contextMenuSelection.Items.Add(new ToolStripSeparator());
            _contextMenuSelection.Items.Add(itemDelete);
            _contextMenuSelection.Items.Add(new ToolStripSeparator());
            _contextMenuSelection.Items.Add(itemCrop);
            _contextMenuSelection.Items.Add(new ToolStripSeparator());
            _contextMenuSelection.Items.Add(itemDeselect);
            _contextMenuSelection.Items.Add(new ToolStripSeparator());
            _contextMenuSelection.Items.Add(itemExportSelection);

            //图片菜单
            _contextMenuItem = new ContextMenuStrip();
            _contextMenuItem.RenderMode = ToolStripRenderMode.System;

            var itemCopyItem = new ToolStripMenuItem("Copy (C)", null, (s, e) => {
                DuplicateSelectedItems();
            });

            var itemRotate = new ToolStripMenuItem("Rotate (R)", null, (s, e) => {
                StartRotation();
            });

            var itemMirror = new ToolStripMenuItem("Mirror");
            itemMirror.DropDownItems.Add("Horizontal", null, (s, e) => RotateSelectedItems(RotateFlipType.RotateNoneFlipX));
            itemMirror.DropDownItems.Add("Vertical", null, (s, e) => RotateSelectedItems(RotateFlipType.RotateNoneFlipY));

            var itemExportItem = new ToolStripMenuItem("Export Image...", null, (s, e) => {
                if (_selectedItems.Count == 0) return;
                RectangleF unionBounds = _selectedItems[0].GetRotatedBounds();
                for (int i = 1; i < _selectedItems.Count; i++)
                {
                    unionBounds = RectangleF.Union(unionBounds, _selectedItems[i].GetRotatedBounds());
                }

                if (ShowExportDialog(out ExportOptions options, out string path))
                {
                    RenderExportImage(unionBounds, _selectedItems, null, options, path);
                }
            });

            var itemDeleteItem = new ToolStripMenuItem("Delete", null, (s, e) => {
                DeleteSelectedItems();
                Invalidate();
            });

            _contextMenuItem.Items.Add(itemCopyItem);
            _contextMenuItem.Items.Add(new ToolStripSeparator());
            _contextMenuItem.Items.Add(itemRotate);
            _contextMenuItem.Items.Add(new ToolStripSeparator());
            _contextMenuItem.Items.Add(itemMirror);
            _contextMenuItem.Items.Add(new ToolStripSeparator());
            _contextMenuItem.Items.Add(itemExportItem);
            _contextMenuItem.Items.Add(new ToolStripSeparator());
            _contextMenuItem.Items.Add(itemDeleteItem);
        }

        private void AfterSelectionAction()
        {
            _currentMode = ToolMode.Pointer;
            UpdateCursor();
            Invalidate();
        }
        private void SwitchMode(ToolMode mode)
        {
            _currentMode = mode;
            UpdateCursor();
            Invalidate();
        }

        private void UpdateMenuCheckState()
        {
            foreach (ToolStripMenuItem item in _menuItemMode.DropDownItems)
            {
                item.Checked = false;
            }

            switch (_currentMode)
            {
                case ToolMode.Pointer:
                    ((ToolStripMenuItem)_menuItemMode.DropDownItems[0]).Checked = true;
                    break;
                case ToolMode.RectSelect:
                    ((ToolStripMenuItem)_menuItemMode.DropDownItems[1]).Checked = true;
                    break;
                case ToolMode.Lasso:
                    ((ToolStripMenuItem)_menuItemMode.DropDownItems[2]).Checked = true;
                    break;
            }
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            SaveState();

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

                        int margin = _CanvasMargin;
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
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle activeCanvas = GetCurrentCanvasBounds();

            using (SolidBrush brush = new SolidBrush(_canvasColor))
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
                GraphicsState state = g.Save();

                PointF center = item.Center;
                g.TranslateTransform(center.X, center.Y);

                g.RotateTransform(item.Rotation);

                g.TranslateTransform(-item.DisplayWidth / 2f, -item.DisplayHeight / 2f);

                g.DrawImage(item.PreviewImage, 0, 0, item.DisplayWidth, item.DisplayHeight);

                if (_selectedItems.Contains(item))
                {
                    using (Pen p = new Pen(Color.FromArgb(150, 100, 100, 255), 2))
                    {
                        g.DrawRectangle(p, -1, -1, item.DisplayWidth + 2, item.DisplayHeight + 2);
                    }
                }
                g.Restore(state);
            }

            //绘制虚线框
            if (_selectionPath.PointCount > 0)
            {
                using (Pen antsPen = new Pen(Color.White, 1))
                {
                    antsPen.DashStyle = DashStyle.Dash;
                    antsPen.DashOffset = _dashOffset;

                    g.DrawPath(Pens.Black, _selectionPath);
                    g.DrawPath(antsPen, _selectionPath);
                }

                using (SolidBrush brush = new SolidBrush(Color.FromArgb(20, 100, 149, 237)))
                {
                    g.FillPath(brush, _selectionPath);
                }
            }

            if (_isSelecting)
            {
                if (_currentMode == ToolMode.RectSelect && !_tempRect.IsEmpty)
                {
                    using (Pen tempPen = new Pen(Color.White, 1))
                    {
                        tempPen.DashStyle = DashStyle.Dot;
                        g.DrawRectangle(Pens.Black, _tempRect.X, _tempRect.Y, _tempRect.Width, _tempRect.Height);
                        g.DrawRectangle(tempPen, _tempRect.X, _tempRect.Y, _tempRect.Width, _tempRect.Height);
                    }
                }
                else if (_currentMode == ToolMode.Lasso && _currentLassoPoints.Count > 1)
                {
                    using (Pen lassoPen = new Pen(Color.Yellow, 1))
                    {
                        g.DrawLines(lassoPen, _currentLassoPoints.ToArray());
                    }
                }
            }

            if (_isRotating)
            {
                Point mousePos = this.PointToClient(Cursor.Position);

                using (Pen guidePen = new Pen(Color.White, 1))
                {
                    guidePen.DashStyle = DashStyle.Dash;
                    g.DrawLine(guidePen, _rotationCenter, mousePos);

                    /*int r = 5;
                    g.DrawLine(Pens.White, _rotationCenter.X - r, _rotationCenter.Y, _rotationCenter.X + r, _rotationCenter.Y);
                    g.DrawLine(Pens.White, _rotationCenter.X, _rotationCenter.Y - r, _rotationCenter.X, _rotationCenter.Y + r);*/
                }
            }
        }

        private ClipItem _draggingItem = null;
        private List<ClipItem> _selectedItems = new List<ClipItem>();
        private Point _lastMousePos;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_isRotating)
            {
                if (e.Button == MouseButtons.Left)
                {
                    _isRotating = false;
                    _initialItemRotations.Clear();
                    Invalidate();
                }
                else if (e.Button == MouseButtons.Right)
                {
                    foreach (var kvp in _initialItemRotations)
                    {
                        kvp.Key.Rotation = kvp.Value;
                    }
                    _isRotating = false;
                    _initialItemRotations.Clear();

                    if (_undoStack.Count > 0) _undoStack.RemoveFirst();

                    Invalidate();
                }
                return;
            }

            if (e.Button == MouseButtons.Right) return;

            if (_currentMode == ToolMode.Pointer)
            {
                base.OnMouseDown(e);
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

                    /*foreach (var item in _selectedItems.ToList())
                    {
                        _items.Remove(item);
                        _items.Add(item);
                    }*/
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
                _tempRect = RectangleF.Empty;
            }
            else if (_currentMode == ToolMode.Lasso)
            {
                _isSelecting = true;
                _currentLassoPoints.Clear();
                _currentLassoPoints.Add(e.Location);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isRotating)
            {
                float dx = e.X - _rotationCenter.X;
                float dy = e.Y - _rotationCenter.Y;

                float currentMouseAngle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);

                float angleDelta = currentMouseAngle - _startMouseAngle;

                foreach (var item in _selectedItems)
                {
                    if (_initialItemRotations.ContainsKey(item))
                    {
                        float originalAngle = _initialItemRotations[item];
                        float finalAngle = originalAngle + angleDelta;

                        if (ModifierKeys == Keys.Shift)
                        {
                            finalAngle = (float)Math.Round(finalAngle / 45.0) * 45.0f;
                        }

                        item.Rotation = finalAngle;
                    }
                }

                //扩展画布
                int margin = _CanvasMargin;
                foreach (var item in _selectedItems)
                {
                    RectangleF bounds = item.GetRotatedBounds();

                    Rectangle boundsWithMargin = new Rectangle(
                        (int)bounds.X - margin,
                        (int)bounds.Y - margin,
                        (int)bounds.Width + margin * 2,
                        (int)bounds.Height + margin * 2
                    );

                    _activeCanvas = Rectangle.Union(_activeCanvas, boundsWithMargin);
                }

                Invalidate();
                return;
            }

            if (_currentMode == ToolMode.Pointer && _draggingItem != null && e.Button == MouseButtons.Left && !_isRotating && _selectedItems.Any())
            {
                Point currentMousePos = Cursor.Position;
                float dx = currentMousePos.X - _lastMousePos.X;
                float dy = currentMousePos.Y - _lastMousePos.Y;

                Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                int wallMargin = _CanvasMargin;

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

                int margin = _CanvasMargin;
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
                _tempRect = new RectangleF(x, y, width, height);
                Invalidate();
            }
            else if (_currentMode == ToolMode.Lasso && _isSelecting)
            {
                _currentLassoPoints.Add(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _isSelecting = false;
                _tempRect = RectangleF.Empty;
                _currentLassoPoints.Clear();
                Invalidate();

                bool clickedOnSelection = false;
                if (_selectionPath.PointCount > 0)
                {
                    if (_selectionPath.IsVisible(e.Location))
                    {
                        clickedOnSelection = true;
                    }
                }

                if (clickedOnSelection)
                {
                    _contextMenuSelection.Show(this, e.Location);
                    return;
                }

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
                    if (!_selectedItems.Contains(clickedItem))
                    {
                        _selectedItems.Clear();
                        _selectedItems.Add(clickedItem);
                        Invalidate();
                    }

                    _contextMenuItem.Show(this, e.Location);
                }
                else
                {
                    UpdateMenuCheckState();
                    _contextMenu.Show(this, e.Location);
                }

                return;
            }

            GraphicsPath currentStrokePath = new GraphicsPath(FillMode.Winding);
            bool hasShape = false;

            if (_currentMode == ToolMode.RectSelect && _isSelecting)
            {
                if (_tempRect.Width > 0 && _tempRect.Height > 0)
                {
                    currentStrokePath.AddRectangle(_tempRect);
                    hasShape = true;
                }
                _tempRect = RectangleF.Empty;
            }
            else if (_currentMode == ToolMode.Lasso && _isSelecting)
            {
                if (_currentLassoPoints.Count > 2)
                {
                    currentStrokePath.AddPolygon(_currentLassoPoints.ToArray());
                    hasShape = true;
                }
                _currentLassoPoints.Clear();
            }

            if (hasShape)
            {
                _selectionPath.AddPath(currentStrokePath, false);
            }

            currentStrokePath.Dispose();
            _draggingItem = null;
            _isSelecting = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            SaveState();

            float zoomFactor = e.Delta > 0 ? 1.1f : 0.9f;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            int margin = _CanvasMargin;

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
            if (e.KeyCode == Keys.Escape)
            {
                var result = MessageBox.Show(
                    this,
                    "Are you sure you want to exit?",
                    "Exit Confirmation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    Application.Exit();
                }
            }

            if (e.KeyCode == Keys.W)
            {
                _currentMode = (ToolMode)(((int)_currentMode + 1) % 3);

                UpdateCursor();
                Invalidate();
            }

            if (e.KeyCode == Keys.D)
            {
                _selectionPath.Reset();
                _tempRect = RectangleF.Empty;
                _currentLassoPoints.Clear();
                Invalidate();
            }

            if (e.KeyCode == Keys.R && _selectedItems.Count > 0 && !_isRotating)
            {
                StartRotation();
            }

            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.X)
            {
                if (_selectionPath.PointCount > 0)
                {
                    HandleSelection();
                }
                _currentMode = ToolMode.Pointer;
                UpdateCursor();
                Invalidate();
            }

            if (e.KeyCode == Keys.C)
            {
                if (_selectionPath.PointCount > 0)
                {
                    HandleSelection(true);
                    _currentMode = ToolMode.Pointer;
                    UpdateCursor();
                    Invalidate();
                }
                else if (_selectedItems.Count > 0)
                {
                    DuplicateSelectedItems();
                }
            }

            if (e.Control && !e.Shift && e.KeyCode == Keys.Z)
            {
                Undo();
            }

            if ((e.Control && e.Shift && e.KeyCode == Keys.Z) || (e.Control && e.KeyCode == Keys.Y))
            {
                Redo();
            }

            if (e.KeyCode == Keys.Delete)
            {
                if (_selectionPath.PointCount > 0) DeleteInsideSelection();
                else DeleteSelectedItems();
            }

            if (e.KeyCode == Keys.K && _selectionPath.PointCount > 0)
            {
                KeepOnlySelection();
                _currentMode = ToolMode.Pointer;
                UpdateCursor();
            }

            if (e.KeyCode == Keys.OemOpenBrackets && _selectedItems.Count > 0)
            {
                SaveState();

                var sortedSelection = _selectedItems.OrderBy(item => _items.IndexOf(item)).ToList();

                foreach (var item in sortedSelection)
                {
                    int index = _items.IndexOf(item);
                    if (index > 0)
                    {
                        _items.RemoveAt(index);
                        _items.Insert(index - 1, item);
                    }
                }
                Invalidate();
            }

            if (e.KeyCode == Keys.OemCloseBrackets && _selectedItems.Count > 0)
            {
                SaveState();
                var sortedSelection = _selectedItems.OrderByDescending(item => _items.IndexOf(item)).ToList();

                foreach (var item in sortedSelection)
                {
                    int index = _items.IndexOf(item);
                    if (index < _items.Count - 1)
                    {
                        _items.RemoveAt(index);
                        _items.Insert(index + 1, item);
                    }
                }
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

        private void StartRotation()
        {
            _isRotating = true;
            SaveState();

            float sumX = 0, sumY = 0;
            foreach (var item in _selectedItems)
            {
                sumX += item.Center.X;
                sumY += item.Center.Y;
            }
            _rotationCenter = new PointF(sumX / _selectedItems.Count, sumY / _selectedItems.Count);

            Point mousePos = this.PointToClient(Cursor.Position);
            float dx = mousePos.X - _rotationCenter.X;
            float dy = mousePos.Y - _rotationCenter.Y;
            _startMouseAngle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);

            _initialItemRotations.Clear();
            foreach (var item in _selectedItems)
            {
                _initialItemRotations[item] = item.Rotation;
            }

            Invalidate();
        }

        private void HandleSelection(bool isCopyOnly = false)
        {
            if (_selectionPath.PointCount == 0) return;

            SaveState();

            RectangleF bounds = _selectionPath.GetBounds();

            List<ClipItem> targets = new List<ClipItem>();
            if (_selectedItems.Count > 0) targets.AddRange(_selectedItems);
            else
            {
                foreach (var item in _items)
                {
                    RectangleF itemRect = new RectangleF(item.X, item.Y, item.DisplayWidth, item.DisplayHeight);
                    if (itemRect.IntersectsWith(bounds)) targets.Add(item);
                }
            }

            List<ClipItem> newCreatedItems = new List<ClipItem>();

            foreach (var target in targets)
            {
                GraphicsPath localPath = (GraphicsPath)_selectionPath.Clone();
                Matrix matrix = new Matrix();
                matrix.Translate(-target.X, -target.Y, MatrixOrder.Append);
                matrix.Scale(1.0f / target.Scale, 1.0f / target.Scale, MatrixOrder.Append);
                localPath.Transform(matrix);

                RectangleF localPathBounds = localPath.GetBounds();
                Rectangle imageRect = new Rectangle(0, 0, target.ImageData.Width, target.ImageData.Height);
                RectangleF intersectRectF = RectangleF.Intersect(localPathBounds, imageRect);

                if (intersectRectF.Width <= 0 || intersectRectF.Height <= 0) continue;
                Rectangle cutRect = Rectangle.Round(intersectRectF);

                if (cutRect.Width < 1) cutRect.Width = 1;
                if (cutRect.Height < 1) cutRect.Height = 1;

                Bitmap croppedBmp = new Bitmap(cutRect.Width, cutRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                croppedBmp.SetResolution(target.ImageData.HorizontalResolution, target.ImageData.VerticalResolution);
                using (Graphics gNew = Graphics.FromImage(croppedBmp))
                {
                    Matrix moveBack = new Matrix();
                    moveBack.Translate(-cutRect.X, -cutRect.Y, MatrixOrder.Append);
                    localPath.Transform(moveBack);

                    gNew.SetClip(localPath);
                    gNew.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                    gNew.DrawImage(target.ImageData, new Rectangle(0, 0, cutRect.Width, cutRect.Height), cutRect, GraphicsUnit.Pixel);
                }

                if (!isCopyOnly)
                {
                    GraphicsPath erasePath = (GraphicsPath)_selectionPath.Clone();
                    Matrix m2 = new Matrix();
                    m2.Translate(-target.X, -target.Y, MatrixOrder.Append);
                    m2.Scale(1.0f / target.Scale, 1.0f / target.Scale, MatrixOrder.Append);
                    erasePath.Transform(m2);

                    Bitmap newMaster = new Bitmap(target.ImageData);
                    newMaster.SetResolution(target.ImageData.HorizontalResolution, target.ImageData.VerticalResolution);

                    using (Graphics gOld = Graphics.FromImage(newMaster))
                    {
                        gOld.SetClip(erasePath);
                        gOld.CompositingMode = CompositingMode.SourceCopy;
                        gOld.FillRectangle(Brushes.Transparent, 0, 0, newMaster.Width, newMaster.Height);
                    }
                    target.ImageData.Dispose();
                    target.ImageData = newMaster;
                    target.UpdatePreview();
                }

                var newItem = new ClipItem(croppedBmp);
                newItem.X = target.X + cutRect.X * target.Scale;
                newItem.Y = target.Y + cutRect.Y * target.Scale;
                newItem.Scale = target.Scale;

                newCreatedItems.Add(newItem);
            }

            _items.AddRange(newCreatedItems);
            _selectedItems.Clear();
            _selectedItems.AddRange(newCreatedItems);

            _selectionPath.Reset();
            Invalidate();
        }

        private void DuplicateSelectedItems()
        {
            if (_selectedItems.Count == 0) return;

            SaveState();

            List<ClipItem> newItems = new List<ClipItem>();

            foreach (var item in _selectedItems)
            {
                ClipItem clonedItem = item.Clone();

                clonedItem.X += 20;
                clonedItem.Y += 20;

                _items.Add(clonedItem);
                newItems.Add(clonedItem);
            }

            _selectedItems.Clear();
            _selectedItems.AddRange(newItems);

            //扩展画布
            int margin = _CanvasMargin;
            foreach (var item in newItems)
            {
                RectangleF bounds = item.GetRotatedBounds();

                Rectangle boundsWithMargin = new Rectangle(
                    (int)bounds.X - margin,
                    (int)bounds.Y - margin,
                    (int)bounds.Width + margin * 2,
                    (int)bounds.Height + margin * 2
                );

                _activeCanvas = Rectangle.Union(_activeCanvas, boundsWithMargin);
            }

            Invalidate();
        }

        private void SaveState()
        {
            if (_isProcessingUndoRedo) return;

            _undoStack.AddFirst(new HistoryState(_items, _activeCanvas));
            _redoStack.Clear();

            if (_undoStack.Count > MAX_HISTORY)
            {
                var oldestState = _undoStack.Last.Value;

                DisposeStateIfOrphan(oldestState);

                _undoStack.RemoveLast();
            }
        }

        private void DisposeStateIfOrphan(HistoryState state)
        {
            foreach (var historyItem in state.ItemsSnapshot)
            {
                bool isStillUsedInCurrent = _items.Any(i => i.ImageData == historyItem.ImageData);

                bool isStillInUndoStack = _undoStack.Any(s => s.ItemsSnapshot.Any(i => i.ImageData == historyItem.ImageData));

                if (!isStillUsedInCurrent && !isStillInUndoStack)
                {
                    historyItem.ImageData.Dispose();
                    historyItem.PreviewImage.Dispose();
                }
            }
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;

            _isProcessingUndoRedo = true;

            _redoStack.AddFirst(new HistoryState(_items, _activeCanvas));

            var state = _undoStack.First.Value;
            _undoStack.RemoveFirst();

            _items = state.ItemsSnapshot;
            _activeCanvas = state.CanvasSnapshot;

            _selectedItems.Clear();
            _selectionPath.Reset();
            _currentLassoPoints.Clear();
            _tempRect = RectangleF.Empty;
            _isProcessingUndoRedo = false;
            Invalidate();
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;

            _isProcessingUndoRedo = true;

            _undoStack.AddFirst(new HistoryState(_items, _activeCanvas));

            var state = _redoStack.First.Value;
            _redoStack.RemoveFirst();

            _items = state.ItemsSnapshot;
            _activeCanvas = state.CanvasSnapshot;

            _selectedItems.Clear();
            _selectionPath.Reset();
            _currentLassoPoints.Clear();
            _tempRect = RectangleF.Empty;
            _isProcessingUndoRedo = false;
            Invalidate();
        }

        private void DeleteSelectedItems()
        {
            if (_selectedItems.Count == 0) return;

            SaveState();

            foreach (var item in _selectedItems)
            {
                _items.Remove(item);
            }

            _selectedItems.Clear();

            Invalidate();
        }

        private void RotateSelectedItems(RotateFlipType type)
        {
            if (_selectedItems.Count == 0) return;

            SaveState();

            foreach (var item in _selectedItems)
            {
                item.ImageData.RotateFlip(type);
                item.UpdatePreview();
            }

            Invalidate();
        }

        private void DeleteInsideSelection()
        {
            if (_selectionPath.PointCount == 0) return;

            List<ClipItem> targets = new List<ClipItem>();
            if (_selectedItems.Count > 0) targets.AddRange(_selectedItems);
            else
            {
                RectangleF bounds = _selectionPath.GetBounds();
                foreach (var item in _items)
                {
                    RectangleF itemRect = new RectangleF(item.X, item.Y, item.DisplayWidth, item.DisplayHeight);
                    if (itemRect.IntersectsWith(bounds)) targets.Add(item);
                }
            }

            if (targets.Count == 0) return;

            SaveState();

            foreach (var target in targets)
            {
                GraphicsPath localPath = (GraphicsPath)_selectionPath.Clone();
                Matrix matrix = new Matrix();
                matrix.Translate(-target.X, -target.Y, MatrixOrder.Append);
                matrix.Scale(1.0f / target.Scale, 1.0f / target.Scale, MatrixOrder.Append);
                localPath.Transform(matrix);

                RectangleF localPathBounds = localPath.GetBounds();
                Rectangle imageRect = new Rectangle(0, 0, target.ImageData.Width, target.ImageData.Height);

                if (!localPathBounds.IntersectsWith(imageRect)) continue;

                Bitmap newBmp = new Bitmap(target.ImageData);

                newBmp.SetResolution(target.ImageData.HorizontalResolution, target.ImageData.VerticalResolution);

                using (Graphics g = Graphics.FromImage(newBmp))
                {
                    g.SetClip(localPath);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.FillRectangle(Brushes.Transparent, 0, 0, newBmp.Width, newBmp.Height);
                }

                target.ImageData = newBmp;

                target.UpdatePreview();
            }

            _selectionPath.Reset();
            Invalidate();
        }

        private void KeepOnlySelection()
        {
            if (_selectionPath.PointCount == 0) return;

            SaveState();

            List<ClipItem> targets = new List<ClipItem>();
            if (_selectedItems.Count > 0) targets.AddRange(_selectedItems);
            else targets.AddRange(_items.ToList());

            List<ClipItem> itemsToRemove = new List<ClipItem>();

            foreach (var target in targets)
            {
                GraphicsPath localPath = (GraphicsPath)_selectionPath.Clone();
                Matrix matrix = new Matrix();
                matrix.Translate(-target.X, -target.Y, MatrixOrder.Append);
                matrix.Scale(1.0f / target.Scale, 1.0f / target.Scale, MatrixOrder.Append);
                localPath.Transform(matrix);

                RectangleF localPathBounds = localPath.GetBounds();
                Rectangle imageRect = new Rectangle(0, 0, target.ImageData.Width, target.ImageData.Height);
                RectangleF intersectRectF = RectangleF.Intersect(localPathBounds, imageRect);

                if (intersectRectF.Width <= 0 || intersectRectF.Height <= 0)
                {
                    itemsToRemove.Add(target);
                    continue;
                }

                Rectangle cutRect = Rectangle.Round(intersectRectF);

                if (cutRect.Width <= 0) cutRect.Width = 1;
                if (cutRect.Height <= 0) cutRect.Height = 1;

                Bitmap croppedBmp = new Bitmap(cutRect.Width, cutRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                using (Graphics g = Graphics.FromImage(croppedBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    Matrix moveBack = new Matrix();
                    moveBack.Translate(-cutRect.X, -cutRect.Y);
                    localPath.Transform(moveBack);

                    g.SetClip(localPath);

                    g.DrawImage(target.ImageData,
                        new Rectangle(0, 0, cutRect.Width, cutRect.Height),
                        cutRect,
                        GraphicsUnit.Pixel);
                }

                target.X += cutRect.X * target.Scale;
                target.Y += cutRect.Y * target.Scale;

                target.ImageData = croppedBmp;
                target.UpdatePreview();
            }

            foreach (var item in itemsToRemove)
            {
                _items.Remove(item);
                if (_selectedItems.Contains(item)) _selectedItems.Remove(item);
            }

            _selectionPath.Reset();
            Invalidate();
        }

        private void UpdateCursor()
        {
            switch (_currentMode)
            {
                case ToolMode.Pointer:
                    this.Cursor = Cursors.Default;
                    break;
                case ToolMode.RectSelect:
                    this.Cursor = Cursors.Cross;
                    break;
                case ToolMode.Lasso:
                    this.Cursor = Cursors.Help;
                    break;
                default:
                    this.Cursor = Cursors.Default;
                    break;
            }
        }

        private bool ShowExportDialog(out ExportOptions options, out string filePath)
        {
            ExportOptions tempOptions = new ExportOptions();
            options = new ExportOptions();
            filePath = "";

            // 导出界面
            using (Form form = new Form())
            {
                form.Text = "Export Image";
                form.Size = new Size(310, 285);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = this.TopMost;

                int y = 20;
                int x = 20;
                int spacing = 35;

                // 格式
                Label lblFormat = new Label() { Text = "Format:", Location = new Point(x, y), AutoSize = true };
                RadioButton rbPng = new RadioButton() { Text = "PNG", Location = new Point(x + 80, y), Checked = true, AutoSize = true };
                RadioButton rbJpg = new RadioButton() { Text = "JPG", Location = new Point(x + 165, y), AutoSize = true };
                form.Controls.AddRange(new Control[] { lblFormat, rbPng, rbJpg });

                y += spacing;

                // 背景
                CheckBox chkBg = new CheckBox() { Text = "Include Background", Location = new Point(x, y + 3), Checked = true, AutoSize = true, Width = 300 };
                form.Controls.Add(chkBg);

                rbJpg.CheckedChanged += (s, e) => {
                    if (rbJpg.Checked) { chkBg.Checked = true; chkBg.Enabled = false; }
                    else { chkBg.Enabled = true; }
                };

                y += spacing;

                // 边框
                CheckBox chkBorder = new CheckBox() { Text = "Draw Border", Location = new Point(x, y + 3), Checked = false, AutoSize = true, Width = 300 };
                form.Controls.Add(chkBorder);

                y += spacing;

                // 缩放
                Label lblScale = new Label() { Text = "Scale:", Location = new Point(x, y + 3), AutoSize = true };

                ComboBox cmbScale = new ComboBox()
                {
                    Location = new Point(x + 60, y),
                    Width = 100,
                    DropDownStyle = ComboBoxStyle.DropDown
                };

                cmbScale.Items.AddRange(new object[] { "50%", "100%", "150%", "200%", "300%", "400%" });
                cmbScale.Text = "100%";

                form.Controls.AddRange(new Control[] { lblScale, cmbScale });
                y += spacing * 2;

                // 按钮
                Button btnSave = new Button() { Text = "Export", DialogResult = DialogResult.None, Location = new Point(170, y - 20), Width = 100, Height = 40 }; 
                Button btnCancel = new Button() { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(20, y - 20), Width = 100, Height = 40 };
                form.Controls.AddRange(new Control[] { btnSave, btnCancel });
                form.AcceptButton = btnSave;
                form.CancelButton = btnCancel;

                btnSave.Click += (s, e) => {
                    string input = cmbScale.Text.Trim().Replace("%", "");
                    if (int.TryParse(input, out int scaleVal))
                    {
                        if (scaleVal < 1 || scaleVal > 2000)
                        {
                            MessageBox.Show("Scale must be between 1% and 2000%.", "Invalid Value");
                            return;
                        }

                        tempOptions.ScalePercentage = scaleVal;
                    }
                    else
                    {
                        MessageBox.Show("Please enter a valid number for Scale.", "Invalid Input");
                        return;
                    }

                    tempOptions.IsPng = rbPng.Checked;
                    tempOptions.IncludeBackground = chkBg.Checked;
                    tempOptions.DrawBorder = chkBorder.Checked;

                    form.DialogResult = DialogResult.OK;
                };

                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        sfd.Filter = tempOptions.IsPng ? "PNG Image|*.png" : "JPEG Image|*.jpg";
                        sfd.FileName = "PureClip_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            filePath = sfd.FileName;
                            options = tempOptions;
                            return true;
                        }
                    }
                }
            }
            options = null;
            return false;
        }

        private void RenderExportImage(RectangleF sourceBounds, List<ClipItem> itemsToDraw, GraphicsPath clipPath, ExportOptions options, string filePath)
        {
            float scaleFactor = options.ScalePercentage / 100f;

            int width = (int)Math.Ceiling(sourceBounds.Width * scaleFactor);
            int height = (int)Math.Ceiling(sourceBounds.Height * scaleFactor);

            if (width < 1) width = 1;
            if (height < 1) height = 1;

            using (Bitmap exportBmp = new Bitmap(width, height))
            {
                if (options.IsPng && !options.IncludeBackground)
                {
                }
                else
                {
                    using (Graphics gBg = Graphics.FromImage(exportBmp))
                    {
                        gBg.Clear(options.IncludeBackground ? _canvasColor : Color.Black);
                    }
                }

                using (Graphics g = Graphics.FromImage(exportBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    g.TranslateTransform(-sourceBounds.X, -sourceBounds.Y, MatrixOrder.Append);

                    g.ScaleTransform(scaleFactor, scaleFactor, MatrixOrder.Append);

                    if (clipPath != null && clipPath.PointCount > 0)
                    {
                        g.SetClip(clipPath);
                    }

                    foreach (var item in itemsToDraw)
                    {
                        GraphicsState state = g.Save();

                        PointF center = item.Center;
                        g.TranslateTransform(center.X, center.Y);
                        g.RotateTransform(item.Rotation);
                        g.TranslateTransform(-item.DisplayWidth / 2f, -item.DisplayHeight / 2f);

                        g.DrawImage(item.PreviewImage, 0, 0, item.DisplayWidth, item.DisplayHeight);

                        g.Restore(state);
                    }

                    if (options.DrawBorder)
                    {
                        g.ResetClip();
                        g.ResetTransform();

                        using (Pen borderPen = new Pen(Color.Black, 1))
                        {
                            g.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
                        }
                    }
                }

                System.Drawing.Imaging.ImageFormat format = options.IsPng
                    ? System.Drawing.Imaging.ImageFormat.Png
                    : System.Drawing.Imaging.ImageFormat.Jpeg;

                exportBmp.Save(filePath, format);
            }
        }
    }

    public class ClipItem
    {
        public Bitmap ImageData;
        public Bitmap PreviewImage;
        public float X;
        public float Y;
        public float Scale = 1.0f;
        public float Rotation = 0f;

        public float DisplayWidth => ImageData.Width * Scale;
        public float DisplayHeight => ImageData.Height * Scale;

        public PointF Center => new PointF(X + DisplayWidth / 2, Y + DisplayHeight / 2);

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

            PointF center = this.Center;
            float dx = mouseX - center.X;
            float dy = mouseY - center.Y;

            double angleRad = -this.Rotation * Math.PI / 180.0;

            float localX = (float)(dx * Math.Cos(angleRad) - dy * Math.Sin(angleRad)) + center.X;
            float localY = (float)(dx * Math.Sin(angleRad) + dy * Math.Cos(angleRad)) + center.Y;

            return localX > X && localX <= X + DisplayWidth &&
                   localY > Y && localY <= Y + DisplayHeight;
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

        public RectangleF GetRotatedBounds()
        {
            if (Rotation == 0)
            {
                return new RectangleF(X, Y, DisplayWidth, DisplayHeight);
            }

            PointF center = this.Center;

            float halfW = DisplayWidth / 2f;
            float halfH = DisplayHeight / 2f;
            PointF[] corners = new PointF[]
            {
            new PointF(-halfW, -halfH),
            new PointF(halfW, -halfH),
            new PointF(halfW, halfH),
            new PointF(-halfW, halfH)
            };
            double angleRad = this.Rotation * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var p in corners)
            {
                float rotX = (float)(p.X * cos - p.Y * sin);
                float rotY = (float)(p.X * sin + p.Y * cos);

                float worldX = rotX + center.X;
                float worldY = rotY + center.Y;

                if (worldX < minX) minX = worldX;
                if (worldX > maxX) maxX = worldX;
                if (worldY < minY) minY = worldY;
                if (worldY > maxY) maxY = worldY;
            }

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public void UpdatePreview()
        {
            PreviewImage = CreatePreview(ImageData, 2000);
        }

        public ClipItem Clone()
        {
            Bitmap newBmp = new Bitmap(this.ImageData);
            newBmp.SetResolution(this.ImageData.HorizontalResolution, this.ImageData.VerticalResolution);

            ClipItem newItem = new ClipItem(newBmp);

            newItem.X = this.X;
            newItem.Y = this.Y;
            newItem.Scale = this.Scale;
            newItem.Rotation = this.Rotation;

            return newItem;
        }
    }

    public class HistoryState
    {
        public List<ClipItem> ItemsSnapshot { get; set; }
        public Rectangle CanvasSnapshot { get; set; }

        public HistoryState(List<ClipItem> currentItems, Rectangle currentCanvas)
        {
            ItemsSnapshot = currentItems.Select(item => item.Clone()).ToList();
            CanvasSnapshot = currentCanvas;
        }
    }

    public class ExportOptions
    {
        public bool IsPng { get; set; } = true;
        public bool IncludeBackground { get; set; } = true;
        public bool DrawBorder { get; set; } = false;
        public int ScalePercentage { get; set; } = 100;
    }

}
