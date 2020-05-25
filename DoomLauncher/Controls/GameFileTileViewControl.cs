﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DoomLauncher.Interfaces;

namespace DoomLauncher
{
    public partial class GameFileTileViewControl : UserControl, IGameFileView
    {
        public IEnumerable<IGameFile> DataSource
        {
            get => m_gameFiles;
            set => SetData(value);
        }

        public IGameFile SelectedItem
        {
            get
            {
                if (m_selectedTiles.Count > 0)
                    return m_selectedTiles[0].GameFile;
                return null;
            }
            set
            {
                SelectGameFile(value);
            }
        }

        public IGameFile[] SelectedItems => m_selectedTiles.Select(x => x.GameFile).ToArray();

        public bool MultiSelect { get; set; }
        public object DoomLauncherParent { get; set; }
        public Control ToolTipControl => flpMain;

        public event EventHandler ItemClick;
        public event EventHandler ItemDoubleClick;
        public event EventHandler SelectionChange;
        public event KeyPressEventHandler ViewKeyPress;
        public event KeyEventHandler ViewKeyDown;
        public event GameFileEventHandler GameFileEnter;
        public event GameFileEventHandler GameFileLeave;

        private List<IGameFile> m_gameFiles = new List<IGameFile>();
        private List<GameFileTile> m_selectedTiles = new List<GameFileTile>();

        private ContextMenuStrip m_menu;
        private bool m_visible;

        private readonly System.Timers.Timer m_mouseTimer;
        private readonly PagingControl m_pagingControl;
        private int m_scrollBarPos = 0;
        private GameFileTile m_lastHover;

        public GameFileTileViewControl()
        {
            InitializeComponent();

            m_pagingControl = new PagingControl();
            m_pagingControl.Anchor = AnchorStyles.None;
            m_pagingControl.PageIndexChanged += M_pagingControl_PageIndexChanged;
            tblMain.Controls.Add(m_pagingControl, 0, 0);

            m_mouseTimer = new System.Timers.Timer();
            m_mouseTimer.Interval = 100;
            m_mouseTimer.Elapsed += MouseTimer_Elapsed;
            m_mouseTimer.Start();

            InitTiles();
        }

        public void SetVisible(bool set)
        {
            m_visible = set;

            if (set)
            {
                tblMain.Controls.Remove(flpMain);
                flpMain = GameFileTileManager.Instance.FlowLayoutPanel;
                tblMain.Controls.Add(flpMain, 0, 1);

                flpMain.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                flpMain.Dock = DockStyle.Fill;
                flpMain.AutoScroll = true;
                flpMain.Click += GameFileTileViewControl_Click;
                flpMain.KeyPress += GameFileTileViewControl_KeyPress;
                flpMain.KeyDown += GameFileTileViewControl_KeyDown;

                SetPageData(m_pagingControl.PageIndex, false);
                flpMain.VerticalScroll.Value = m_scrollBarPos;
                flpMain.PerformLayout();
            }
            else
            {
                m_selectedTiles.ForEach(x => x.SetSelected(false));
                m_scrollBarPos = flpMain.VerticalScroll.Value;
                Controls.Remove(flpMain);
            }
        }

        private void InitTiles()
        {
            foreach (var tile in GameFileTileManager.Instance.Tiles)
            {
                tile.TileClick += Tile_TileClick;
                tile.TileDoubleClick += Tile_TileDoubleClick;
            }
        }

        private void M_pagingControl_PageIndexChanged(object sender, EventArgs e)
        {
            ClearSelection();
            SetPageData(m_pagingControl.PageIndex, true);
        }

        private void MouseTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (InvokeRequired)
                Invoke(new Action(MouseMoveTimer));
        }

        private void MouseMoveTimer()
        {
            if (!m_visible)
                return;

            // PointToClient will return null if the view is obscured at the current position
            Point pt = Cursor.Position;
            if (GetChildAtPoint(PointToClient(pt)) != null)
            {
                foreach (var tile in GameFileTileManager.Instance.Tiles)
                {
                    if (!tile.Visible)
                        continue;

                    var rect = tile.RectangleToScreen(tile.DisplayRectangle);
                    if (rect.Contains(pt))
                    {
                        if (m_lastHover != tile)
                        {
                            m_lastHover = tile;
                            GameFileEnter?.Invoke(this, new GameFileEventArgs(tile.GameFile));
                        }
                        return;
                    }
                }
            }

            if (m_lastHover != null)
                GameFileLeave?.Invoke(this, new GameFileEventArgs(m_lastHover.GameFile));

            m_lastHover = null;
        }

        private void GameFileTileViewControl_KeyDown(object sender, KeyEventArgs e)
        {
            ViewKeyDown?.Invoke(this, e);
        }

        private void GameFileTileViewControl_KeyPress(object sender, KeyPressEventArgs e)
        {
            ViewKeyPress?.Invoke(this, e);
        }

        private void GameFileTileViewControl_Click(object sender, EventArgs e)
        {
            ClearSelection();
            SelectionChange?.Invoke(this, EventArgs.Empty);
        }

        public IGameFile GameFileForIndex(int index)
        {
            if (index >= m_gameFiles.Count)
                return null;

            return m_gameFiles[index];
        }

        public void RefreshData()
        {
            if (!m_visible)
                return;

            var screenshots = MainForm.Instance.DataSourceAdapter.GetFiles(FileType.Screenshot);
            string screenshotPath = MainForm.Instance.AppConfiguration.ScreenshotDirectory.GetFullPath();
            foreach (var tile in GameFileTileManager.Instance.Tiles)
            {
                if (!tile.Visible)
                    break;

                SetTileData(tile.GameFile, screenshots, screenshotPath, tile);
            }
        }

        public void SetContextMenuStrip(ContextMenuStrip menu)
        {
            m_menu = menu;
        }

        public void SetDisplayText(string text)
        {

        }

        private void SetData(IEnumerable<IGameFile> gameFiles)
        {
            bool update = false;
            int saveIndex = m_pagingControl.PageIndex;

            if (gameFiles == null)
            {
                m_gameFiles.Clear();
            }
            else
            {
                // Basically a hack for when deleting a single item to keep the current page
                update = (gameFiles.Count() == m_gameFiles.Count - 1 && m_gameFiles.Contains(gameFiles.First()));
                m_gameFiles = gameFiles.OrderBy(x => x.Title).ToList();
            }

            m_pagingControl.Init(m_gameFiles.Count, GameFileTileManager.Instance.MaxItems);
            m_pagingControl.Visible = m_pagingControl.Pages > 1;

            if (!m_visible)
                return;

            if (update)
            {
                if (!m_pagingControl.SetPageIndex(saveIndex))
                    SetPageData(saveIndex, true);
            }
            else
            {
                SetPageData(0, false);
            }
        }

        private void SetPageData(int pageIndex, bool pageChange)
        {
            GameFileTileManager.Instance.Tiles.ForEach(x => x.SetSelected(false));

            var screenshots = MainForm.Instance.DataSourceAdapter.GetFiles(FileType.Screenshot);
            string screenshotPath = MainForm.Instance.AppConfiguration.ScreenshotDirectory.GetFullPath();

            var gameFiles = m_gameFiles.Skip(pageIndex * GameFileTileManager.Instance.MaxItems).Take(GameFileTileManager.Instance.MaxItems).ToList();
            SetLayout(gameFiles, screenshots, screenshotPath);

            if (pageChange)
            {
                ClearSelection();
                flpMain.VerticalScroll.Value = 0;
                flpMain.PerformLayout();
            }
            else
            {
                m_selectedTiles.ForEach(x => x.SetSelected(true));
            }
        }

        private void SetLayout(List<IGameFile> gameFiles, IEnumerable<IFileData> screenshots, string screenshotPath)
        {
            flpMain.SuspendLayout();

            var itemsEnum = GameFileTileManager.Instance.Tiles.GetEnumerator();
            itemsEnum.MoveNext();

            foreach (var gameFile in gameFiles)
            {
                SetTileData(gameFile, screenshots, screenshotPath, itemsEnum.Current);
                itemsEnum.Current.Visible = true;
                itemsEnum.MoveNext();
            }

            for (int i = gameFiles.Count; i < GameFileTileManager.Instance.Tiles.Count; i++)
            {
                GameFileTileManager.Instance.Tiles[i].SetPicture(string.Empty);
                GameFileTileManager.Instance.Tiles[i].Visible = false;
            }

            flpMain.ResumeLayout();
        }

        private static void SetTileData(IGameFile gameFile, IEnumerable<IFileData> screenshots, string screenshotPath, GameFileTile tile)
        {
            tile.GameFile = gameFile;

            if (string.IsNullOrEmpty(gameFile.Title))
                tile.SetTitle(gameFile.FileNameNoPath);
            else
                tile.SetTitle(gameFile.Title);

            if (gameFile.GameFileID.HasValue)
            {
                var screenshot = screenshots.FirstOrDefault(x => x.GameFileID == gameFile.GameFileID.Value);
                if (screenshot != null)
                    tile.SetPicture(Path.Combine(screenshotPath, screenshot.FileName));
                else
                    tile.SetImage(GameFileTileManager.Instance.DefaultImage);
            }
            else
            {
                tile.SetImage(GameFileTileManager.Instance.DefaultImage);
            }
        }

        private void Tile_TileDoubleClick(object sender, EventArgs e)
        {
            if (!m_visible)
                return;

            ItemDoubleClick?.Invoke(this, e);
        }

        private void Tile_TileClick(object sender, MouseEventArgs e)
        {
            GameFileTile tile = sender as GameFileTile;
            if (tile == null || !m_visible)
                return;

            ItemClick?.Invoke(this, EventArgs.Empty);

            if (e.Button == MouseButtons.Left)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                    ToggleSelection(tile);
                else if ((ModifierKeys & Keys.Shift) == Keys.Shift && m_selectedTiles.Count > 0)
                    SelectRange(tile);
                else
                    SelectGameFile(tile.GameFile);
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (!m_selectedTiles.Contains(tile))
                    SelectGameFile(tile.GameFile);
                Point pt =  PointToScreen(new Point(Location.X + tile.Location.X + e.X, Location.Y + tile.Location.Y + e.Y));
                m_menu.Show(pt);
            }
        }

        private void SelectRange(GameFileTile tile)
        {
            int start = GameFileTileIndex(m_selectedTiles.First());
            int end = GameFileTileIndex(m_selectedTiles.Last());

            if (start > end)
            {
                int temp = start;
                end = start;
                start = temp;
            }

            int index = GameFileTileIndex(tile);

            if (index < start)
                start = index;
            if (index > end)
                end = index;

            ClearSelection();

            for (int i = start; i <= end; i++)
            {
                m_selectedTiles.Add(GameFileTileManager.Instance.Tiles[i]);
                GameFileTileManager.Instance.Tiles[i].SetSelected(true);
            }

            SelectionChange?.Invoke(this, new EventArgs());
        }

        private int GameFileTileIndex(GameFileTile tile)
        {
            for (int i = 0; i < GameFileTileManager.Instance.Tiles.Count; i++)
            {
                if (tile == GameFileTileManager.Instance.Tiles[i])
                    return i;
            }

            return -1;
        }

        private void ToggleSelection(GameFileTile tile)
        {
            tile.SetSelected(!tile.Selected);
        }

        private void SelectGameFile(IGameFile gameFile)
        {
            ClearSelection();

            if (gameFile == null)
                return;

            foreach (GameFileTile tile in GameFileTileManager.Instance.Tiles)
            {
                if (tile.GameFile.Equals(gameFile))
                {
                    tile.SetSelected(true);
                    m_selectedTiles.Add(tile);
                    break;
                }
            }

            if (m_selectedTiles.Count > 0)
                SelectionChange?.Invoke(this, EventArgs.Empty);
        }

        private void ClearSelection()
        {
            m_selectedTiles.ForEach(x => x.SetSelected(false));
            m_selectedTiles.Clear();
        }
    }
}