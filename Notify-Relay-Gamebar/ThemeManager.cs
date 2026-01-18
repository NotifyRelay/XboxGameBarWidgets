using Microsoft.Gaming.XboxGameBar;
using System;
using TimberLog;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace NotifyRelayGamebar
{
    public class ThemeManager
    {
        private XboxGameBarWidget _widget;
        private Panel _playerWidgetView;
        private Panel _playbackControlsPanel;
        private Panel _toastStack;
        private Panel _backgroundGrid;
        
        // 主题画笔
        public SolidColorBrush WidgetDarkThemeBrush { get; private set; }
        public SolidColorBrush WidgetLightThemeBrush { get; private set; }
        
        // 保存原始背景画笔以便恢复
        public Brush OriginalPlayerBackgroundBrush { get; private set; }
        public Brush OriginalToastBackgroundBrush { get; private set; }

        public ThemeManager(
            XboxGameBarWidget widget,
            Panel playerWidgetView,
            Panel playbackControlsPanel,
            Panel toastStack,
            Panel backgroundGrid)
        {
            _widget = widget;
            _playerWidgetView = playerWidgetView;
            _playbackControlsPanel = playbackControlsPanel;
            _toastStack = toastStack;
            _backgroundGrid = backgroundGrid;
            
            InitializeThemeBrushes();
            SaveOriginalBrushes();
        }

        private void InitializeThemeBrushes()
        {
            // 初始化主题画笔
            WidgetDarkThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 38));
            WidgetLightThemeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 219, 219, 219));
        }

        private void SaveOriginalBrushes()
        {
            // 保存控件的原始背景画笔
            try
            {
                OriginalPlayerBackgroundBrush = _playerWidgetView?.Background;
            }
            catch { OriginalPlayerBackgroundBrush = null; }

            try
            {
                OriginalToastBackgroundBrush = _toastStack?.Background;
            }
            catch { OriginalToastBackgroundBrush = null; }
        }

        /// <summary>
        /// 检查是否处于固定且关闭状态
        /// </summary>
        /// <returns>如果处于固定且关闭状态返回true，否则返回false</returns>
        public bool IsInPinnedAndClosedState()
        {
            if (_widget == null)
                return false;

            bool isPinned = _widget.Pinned;
            bool isGameBarClosed = _widget.GameBarDisplayMode == XboxGameBarDisplayMode.PinnedOnly;
            
            Timber.Log(LoggerLevel.Info, "IsInPinnedAndClosedState: Pinned={0}, GameBarDisplayMode={1}, Result={2}", 
                isPinned, _widget.GameBarDisplayMode, isPinned && isGameBarClosed);
            
            return isPinned && isGameBarClosed;
        }

        /// <summary>
        /// 设置背景透明度和媒体控制按钮可见性
        /// </summary>
        public void SetBackgroundOpacity()
        {
            // 调试日志
            Timber.Log(LoggerLevel.Info, "SetBackgroundOpacity called, widget: {0}, PlayerWidgetView: {1}", _widget != null ? "NotNull" : "Null", _playerWidgetView != null ? "NotNull" : "Null");

            // 当小部件被固定且GameBar关闭时，强制完全透明并调整媒体/通知背景画笔
            try
            {
                if (_widget != null)
                {
                    bool isPinnedAndClosed = IsInPinnedAndClosedState();

                    if (isPinnedAndClosed)
                    {
                        Timber.Log(LoggerLevel.Info, "Setting fixed and closed state: Background opacity 0.0");

                        try
                        {
                            _playbackControlsPanel.Visibility = Visibility.Collapsed;
                        }
                        catch (Exception ex)
                        {
                            Timber.Log(LoggerLevel.Error, ex, "Error setting controls visibility when pinned and closed");
                        }

                        // 在固定且关闭时，仅更改背景画笔（不影响文本和边框）；如果没有会话，隐藏整个媒体块
                        // 注意：hasSessionsPinned 参数需要从外部传入，这里暂时注释
                        // bool hasSessionsPinned = (mediaSessions?.Count ?? 0) > 0;
                        // _playerWidgetView.Visibility = hasSessionsPinned ? Visibility.Visible : Visibility.Collapsed;

                        // 在固定且关闭时，保持PlayerWidgetView背景透明，不设置背景
                        if (_playerWidgetView != null)
                        {
                            _playerWidgetView.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                        }

                        // ToastStack 始终保持透明，不设置背景
                        if (_toastStack != null)
                        {
                            _toastStack.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                        }

                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // 如果访问 Pinned 属性失败，记录错误并继续使用回退逻辑
                Timber.Log(LoggerLevel.Error, ex, "Error accessing widget.Pinned property");
            }

            // 未固定时，根据是否有媒体会话显示媒体控件
            // 注意：hasSessions 参数需要从外部传入，这里暂时注释
            // bool hasSessions = (mediaSessions?.Count ?? 0) > 0;
            // var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
            // _playbackControlsPanel.Visibility = visibility;

            // 未固定时，保持PlayerWidgetView背景透明，不设置背景
            if (_playerWidgetView != null)
            {
                _playerWidgetView.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
            }

            // ToastStack 始终保持透明，不恢复背景
            if (_toastStack != null)
            {
                _toastStack.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
            }
        }

        /// <summary>
        /// 设置背景颜色
        /// </summary>
        public void SetBackgroundColor()
        {
            if (_widget == null)
                return;

            var requestedTheme = _widget.RequestedTheme;
            
            // 设置页面主题
            if (_backgroundGrid != null)
            {
                _backgroundGrid.RequestedTheme = requestedTheme;
            }
            
            // 保持BackgroundGrid透明，避免显示方形背景
            _backgroundGrid.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
        }

        /// <summary>
        /// 创建半透明画笔
        /// </summary>
        public Brush CreateSemiTransparentBrush(Brush source, byte alpha)
        {
            try
            {
                if (source is SolidColorBrush scb)
                {
                    var c = scb.Color;
                    c.A = alpha;
                    return new SolidColorBrush(c);
                }

                // 回退到应用主题背景色（如果可用）并调整透明度
                if (Application.Current?.Resources != null && Application.Current.Resources.ContainsKey("ApplicationPageBackgroundThemeBrush"))
                {
                    var def = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as SolidColorBrush;
                    if (def != null)
                    {
                        var c = def.Color;
                        c.A = alpha;
                        return new SolidColorBrush(c);
                    }
                }

                return source;
            }
            catch
            {
                return source;
            }
        }
    }
}