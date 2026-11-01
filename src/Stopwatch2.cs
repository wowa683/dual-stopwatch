// ---------------------------------------------------------------------------
//  Двойной секундомер для Windows 11
//  Компактная панель: высота подстраивается под панель задач четвёртого монитора,
//  всегда поверх окон, без рамки, в левом нижнем углу экрана.
//  Два независимых табло: старт/пауза, сброс, перемотка на 10 минут в обе стороны.
//  .NET Framework 4.8 + WPF, без внешних зависимостей.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DualStopwatch
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(new MainWindow());
        }
    }

    /// <summary>Цвета и кисти интерфейса.</summary>
    internal static class Palette
    {
        public static readonly Color AccentCyanColor = Color.FromRgb(0x3F, 0xC9, 0xFF);
        public static readonly Color AccentAmberColor = Color.FromRgb(0xFF, 0xB2, 0x4C);

        public static readonly SolidColorBrush WindowBackground = Make(0x0D, 0x0F, 0x14);
        public static readonly SolidColorBrush WindowBorder = Make(0x24, 0x2C, 0x3A);
        public static readonly SolidColorBrush Card = Make(0x15, 0x1A, 0x23);
        public static readonly SolidColorBrush CardActive = Make(0x1A, 0x21, 0x2C);
        public static readonly SolidColorBrush CardBorder = Make(0x20, 0x28, 0x36);
        public static readonly SolidColorBrush TextPrimary = Make(0xF2, 0xF6, 0xFD);
        public static readonly SolidColorBrush TextSecondary = Make(0xA8, 0xB4, 0xC8);
        public static readonly SolidColorBrush TextMuted = Make(0x84, 0x8F, 0xA3);
        public static readonly SolidColorBrush Button = Make(0x22, 0x29, 0x34);
        public static readonly SolidColorBrush ButtonBorder = Make(0x2F, 0x38, 0x48);
        public static readonly SolidColorBrush ButtonText = Make(0xDD, 0xE4, 0xEF);
        public static readonly SolidColorBrush Ink = Make(0x07, 0x10, 0x1A);

        public static readonly SolidColorBrush AccentCyan = Freeze(new SolidColorBrush(AccentCyanColor));
        public static readonly SolidColorBrush AccentAmber = Freeze(new SolidColorBrush(AccentAmberColor));

        public static SolidColorBrush Alpha(Color color, byte alpha)
        {
            return Freeze(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)));
        }

        private static SolidColorBrush Make(byte r, byte g, byte b)
        {
            return Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Кнопки с закруглёнными углами и подсветкой при наведении.</summary>
    internal static class Ui
    {
        public static Button MakeButton(
            string content,
            FontFamily font,
            double fontSize,
            FontWeight weight,
            Brush background,
            Brush foreground,
            Brush border,
            double radius)
        {
            var button = new Button
            {
                Content = content,
                FontFamily = font,
                FontSize = fontSize,
                FontWeight = weight,
                Background = background,
                Foreground = foreground,
                Template = CreateTemplate(border != null, radius),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                SnapsToDevicePixels = true
            };
            if (border != null)
            {
                button.BorderBrush = border;
            }
            return button;
        }

        private static ControlTemplate CreateTemplate(bool outlined, double radius)
        {
            var corner = new CornerRadius(radius);

            var root = new FrameworkElementFactory(typeof(Grid));
            root.Name = "RootLayer";

            var background = new FrameworkElementFactory(typeof(Border));
            background.SetValue(Border.CornerRadiusProperty, corner);
            background.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            if (outlined)
            {
                background.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
                background.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            }
            root.AppendChild(background);

            var hover = new FrameworkElementFactory(typeof(Border));
            hover.Name = "HoverLayer";
            hover.SetValue(Border.CornerRadiusProperty, corner);
            hover.SetValue(Border.BackgroundProperty, new SolidColorBrush(Colors.White));
            hover.SetValue(UIElement.OpacityProperty, 0.0);
            hover.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(hover);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button));
            template.VisualTree = root;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.10, "HoverLayer"));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.18, "HoverLayer"));
            template.Triggers.Add(pressedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35, "RootLayer"));
            template.Triggers.Add(disabledTrigger);

            return template;
        }
    }

    /// <summary>Независимое табло: накопленное время + текущий прогон.</summary>
    internal sealed class StopwatchBoard
    {
        private static readonly TimeSpan Jump = TimeSpan.FromMinutes(10);

        private readonly Stopwatch watch = new Stopwatch();
        private TimeSpan banked;

        public bool IsRunning
        {
            get { return watch.IsRunning; }
        }

        public TimeSpan Elapsed
        {
            get { return banked + watch.Elapsed; }
        }

        public void Toggle()
        {
            if (watch.IsRunning)
            {
                watch.Stop();
                banked += watch.Elapsed;
                watch.Reset();
            }
            else
            {
                watch.Start();
            }
        }

        public void Shift(int direction)
        {
            // Сначала фиксируем текущий прогон, чтобы перемотка была точной,
            // затем продолжаем счёт с новой точки.
            if (watch.IsRunning)
            {
                banked += watch.Elapsed;
                watch.Restart();
            }
            banked += TimeSpan.FromTicks(Jump.Ticks * direction);
            if (banked < TimeSpan.Zero)
            {
                // Перемотка в минус запрещена — время не уходит ниже нуля.
                banked = TimeSpan.Zero;
            }
        }

        public void Reset()
        {
            watch.Reset();
            banked = TimeSpan.Zero;
        }

        public long ElapsedTicks
        {
            get { return banked.Ticks + watch.Elapsed.Ticks; }
        }

        public void RestoreElapsedTicks(long ticks)
        {
            watch.Reset();
            watch.Stop();
            banked = TimeSpan.FromTicks(ticks);
        }

        public void Pause()
        {
            if (watch.IsRunning)
            {
                banked += watch.Elapsed;
                watch.Reset();
            }
        }

        public void Resume()
        {
            if (!watch.IsRunning)
            {
                watch.Start();
            }
        }
    }

    /// <summary>Компактная карточка одного табло.</summary>
    internal sealed class BoardCard : Border
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly FontFamily MonoFont = new FontFamily("Consolas");
        private static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        private static readonly FontFamily UiFont = new FontFamily("Segoe UI");

        private static readonly Geometry PlayGeometry = FreezeGeometry(Geometry.Parse("M 0,0 L 12,6.5 L 0,13 Z"));
        private static readonly Geometry PauseGeometry = FreezeGeometry(Geometry.Parse("M 0,0 L 4,0 L 4,13 L 0,13 Z M 8,0 L 12,0 L 12,13 L 8,13 Z"));

        private readonly StopwatchBoard board = new StopwatchBoard();
        private readonly Run mainRun;
        private readonly Button toggleButton;
        private readonly Path toggleIcon;
        private readonly SolidColorBrush accentBrush;
        private readonly SolidColorBrush activeBorderBrush;
        private bool isActive;
        private bool pausedByIdle;

        public event EventHandler UserToggled;

        public event EventHandler Activated;

        public BoardCard(string index, Color accent)
        {
            accentBrush = Freeze(new SolidColorBrush(accent));
            activeBorderBrush = Palette.Alpha(accent, 0x90);

            Background = Palette.Card;
            BorderBrush = Palette.CardBorder;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(7);
            Padding = new Thickness(5, 3, 5, 3);
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), true);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Цветная метка табло
            var stripe = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = accentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Табло " + index
            };
            content.Children.Add(stripe);

            // Время без долей секунд, масштабируется под ширину
            mainRun = new Run("00:00:00")
            {
                FontFamily = MonoFont,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Palette.TextPrimary
            };
            var timeBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            timeBlock.Inlines.Add(mainRun);

            var timeBox = new Viewbox
            {
                Child = timeBlock,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 30,
                Margin = new Thickness(0, 0, 3, 0)
            };
            Grid.SetColumn(timeBox, 1);
            content.Children.Add(timeBox);

            // Кнопки управления таблом
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            toggleButton = Ui.MakeButton(string.Empty, IconFont, 10, FontWeights.Normal, accentBrush, Palette.Ink, null, 7);
            toggleIcon = new Path
            {
                Data = PlayGeometry,
                Fill = Palette.Ink,
                Width = 8,
                Height = 10,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            toggleButton.Content = toggleIcon;
            toggleButton.Width = 22;
            toggleButton.Height = 30;
            toggleButton.Margin = new Thickness(0, 0, 2, 0);
            toggleButton.ToolTip = "Старт / пауза (ПРОБЕЛ)";
            toggleButton.Click += delegate
            {
                if (UserToggled != null)
                {
                    UserToggled(this, EventArgs.Empty);
                }
            };
            actions.Children.Add(toggleButton);

            var minusButton = Ui.MakeButton("−", UiFont, 12.5, FontWeights.SemiBold, Palette.Button, Palette.ButtonText, Palette.ButtonBorder, 7);
            minusButton.Width = 20;
            minusButton.Height = 30;
            minusButton.Margin = new Thickness(0, 0, 2, 0);
            minusButton.ToolTip = "Перемотать на 10 минут назад";
            minusButton.Click += delegate { Shift(-1); };
            actions.Children.Add(minusButton);

            var plusButton = Ui.MakeButton("+", UiFont, 12.5, FontWeights.SemiBold, Palette.Button, Palette.ButtonText, Palette.ButtonBorder, 7);
            plusButton.Width = 20;
            plusButton.Height = 30;
            plusButton.Margin = new Thickness(0, 0, 2, 0);
            plusButton.ToolTip = "Перемотать на 10 минут вперёд";
            plusButton.Click += delegate { Shift(1); };
            actions.Children.Add(plusButton);

            var resetButton = Ui.MakeButton("\uE72C", IconFont, 9, FontWeights.Normal, Brushes.Transparent, Palette.TextMuted, Palette.ButtonBorder, 7);
            resetButton.Width = 16;
            resetButton.Height = 30;
            resetButton.ToolTip = "Сбросить табло (R)";
            resetButton.Click += delegate { ResetTimer(); };
            actions.Children.Add(resetButton);

            Grid.SetColumn(actions, 2);
            content.Children.Add(actions);

            Child = content;
            Refresh();
        }

        public bool IsActive
        {
            get { return isActive; }
            set
            {
                if (isActive == value)
                {
                    return;
                }
                isActive = value;
                Background = value ? Palette.CardActive : Palette.Card;
                BorderBrush = value ? activeBorderBrush : Palette.CardBorder;
            }
        }

        public void Toggle()
        {
            pausedByIdle = false;
            board.Toggle();
            Refresh();
        }

        public void Shift(int direction)
        {
            pausedByIdle = false;
            board.Shift(direction);
            Refresh();
        }

        public void ResetTimer()
        {
            pausedByIdle = false;
            board.Reset();
            Refresh();
        }

        public long ElapsedTicks
        {
            get { return board.ElapsedTicks; }
        }

        public void RestoreElapsedTicks(long ticks)
        {
            board.RestoreElapsedTicks(ticks);
            Refresh();
        }

        public bool IsPausedByIdle
        {
            get { return pausedByIdle; }
        }

        public bool IsRunning
        {
            get { return board.IsRunning; }
        }

        public void PauseByIdle()
        {
            if (board.IsRunning)
            {
                board.Pause();
            }
            pausedByIdle = true;
            Refresh();
        }

        public void ResumeFromIdle()
        {
            if (pausedByIdle)
            {
                board.Resume();
                pausedByIdle = false;
                Refresh();
            }
        }

        /// <summary>Обновление цифр во время хода (вызывается таймером).</summary>
        public void Tick()
        {
            if (board.IsRunning)
            {
                SetTime(board.Elapsed);
            }
        }

        private void Refresh()
        {
            SetTime(board.Elapsed);

            bool running = board.IsRunning;
            toggleIcon.Data = running ? PauseGeometry : PlayGeometry;
            mainRun.Foreground = running ? accentBrush : Palette.TextPrimary;
        }

        private void SetTime(TimeSpan value)
        {
            long ticks = value.Ticks;
            bool negative = ticks < 0;
            if (negative)
            {
                ticks = -ticks;
            }
            long hours = ticks / TimeSpan.TicksPerHour;
            long minutes = ticks / TimeSpan.TicksPerMinute % 60;
            long seconds = ticks / TimeSpan.TicksPerSecond % 60;

            mainRun.Text = string.Format(
                Invariant,
                "{0}{1:00}:{2:00}:{3:00}",
                negative ? "−" : string.Empty,
                hours,
                minutes,
                seconds);
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            EventHandler handler = Activated;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static Geometry FreezeGeometry(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }
    }

    /// <summary>Узкое окно-панель поверх всех окон.</summary>
    internal sealed class MainWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out PixelRect rect);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo lastInput);

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref PixelRect rect, uint flags);

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref PixelRect rect, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct PixelRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo
        {
            public int Size;
            public PixelRect Monitor;
            public PixelRect Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        // Имя четвёртого монитора в системе (номер совпадает с «Параметрами экрана»).
        private const string FourthScreenDevice = @"\\.\DISPLAY4";

        // Расчётный (эталонный) размер панели: от него считается пропорция сторон.
        private const double DesignWidth = 440;
        private const double DesignHeight = 48;

        // Доля от высоты панели задач, которую занимает панель (чуть меньше самой панели).
        private const double PanelHeightFactor = 0.9;

        // Расстояние до края (в пикселях), при котором панель прилипает к краю.
        private const int SnapDistance = 24;

        private const int WmMoving = 0x0216;
        private const uint MonitorDefaultToNearest = 0x00000002;

        private static readonly FontFamily UiFont = new FontFamily("Segoe UI");
        private static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        private readonly BoardCard card1;
        private readonly BoardCard card2;
        private readonly DispatcherTimer timer;
        private int activeIndex;

        private DispatcherTimer idleTimer;
        private bool idlePauseApplied;
        private int idleMinutes = 1;

        private const string StateDirName = "Stopwatch2";
        private const string StateFileName = "stopwatch_state.txt";
        private const string IdleSettingsFileName = "stopwatch_idle_settings.txt";

        public MainWindow()
        {
            Title = "Секундомер";
            Width = DesignWidth;
            Height = DesignHeight;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            Background = Palette.WindowBackground;
            Topmost = true;
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = UiFont;
            FontSize = 11;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            card1 = new BoardCard("1", Palette.AccentCyanColor);
            card2 = new BoardCard("2", Palette.AccentAmberColor);

            card1.UserToggled += delegate(object sender, EventArgs e) { ToggleActive(0); };
            card2.UserToggled += delegate(object sender, EventArgs e) { ToggleActive(1); };

            var outer = new Border
            {
                Width = DesignWidth,
                Height = DesignHeight,
                Background = Palette.WindowBackground,
                BorderBrush = Palette.WindowBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(3),
                Cursor = Cursors.SizeAll
            };
            outer.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState != MouseButtonState.Pressed)
                {
                    return;
                }

                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // окно уже не в режиме перетаскивания — ничего страшного
                    return;
                }

                // После отпускания мыши прилипаем к ближайшему краю экрана.
                SnapWindowToEdges();
            };

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            layout.Children.Add(card1);
            Grid.SetColumn(card1, 0);

            layout.Children.Add(card2);
            Grid.SetColumn(card2, 2);

            var windowButtons = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var settingsButton = MakeWindowButton("\uE7E8", "Настройки");
            settingsButton.Margin = new Thickness(0, 0, 0, 3);
            settingsButton.Click += delegate { OpenSettings(); };
            windowButtons.Children.Add(settingsButton);

            var closeButton = MakeWindowButton("\uE8BB", "Закрыть");
            closeButton.Click += delegate { Close(); };
            windowButtons.Children.Add(closeButton);

            Grid.SetColumn(windowButtons, 4);
            layout.Children.Add(windowButtons);

            outer.Child = layout;

            // Панель масштабируется под высоту панели задач, поэтому содержимое
            // растягивается вместе с окном, сохраняя пропорции.
            Content = new Viewbox
            {
                Stretch = Stretch.Fill,
                StretchDirection = StretchDirection.Both,
                Child = outer
            };

            LoadState();

            card1.Activated += delegate { ActivateCard(0); };
            card2.Activated += delegate { ActivateCard(1); };

            timer = new DispatcherTimer(DispatcherPriority.Render);
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += delegate
            {
                card1.Tick();
                card2.Tick();
            };

            Loaded += delegate
            {
                PlaceInFourthScreenCorner();
                ActivateCard(0);
                timer.Start();
                idleTimer = new DispatcherTimer();
                idleTimer.Interval = TimeSpan.FromMilliseconds(3000);
                idleTimer.Tick += delegate
                {
                    IdleTick();
                };
                idleTimer.Start();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
                }
                int cornerPreference = 2; // DWMWCP_ROUND — скруглённые углы Windows 11
                DwmSetWindowAttribute(handle, 33, ref cornerPreference, sizeof(int));
                int borderColor = 0x003A2C24; // COLORREF: #242C3A
                DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));
            }
            catch
            {
                // оформление окна — необязательная часть
            }

            // Ставим панель на место сразу при создании окна, чтобы она не мигала
            // в центре экрана перед первым кадром.
            PlaceInFourthScreenCorner();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    ActivateCard(0);
                    e.Handled = true;
                    break;

                case Key.D2:
                case Key.NumPad2:
                    ActivateCard(1);
                    e.Handled = true;
                    break;

                case Key.Space:
                    e.Handled = true;
                    if (!e.IsRepeat)
                    {
                        ToggleActive(activeIndex);
                    }
                    break;

                case Key.Left:
                    ActiveCard().Shift(-1);
                    e.Handled = true;
                    idlePauseApplied = false;
                    break;

                case Key.Right:
                    ActiveCard().Shift(1);
                    e.Handled = true;
                    idlePauseApplied = false;
                    break;

                case Key.R:
                    e.Handled = true;
                    if (!e.IsRepeat)
                    {
                        idlePauseApplied = false;
                        ActiveCard().ResetTimer();
                    }
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveState();
            SaveIdleSettings();
            timer.Stop();
            if (idleTimer != null)
            {
                idleTimer.Stop();
            }
            base.OnClosed(e);
        }

        private void SaveIdleSettings()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    StateDirName);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, IdleSettingsFileName),
                    idleMinutes.ToString());
            }
            catch
            {
            }
        }

        private void IdleTick()
        {
            int lastInput = 0;
            LastInputInfo lastInputInfo = new LastInputInfo();
            lastInputInfo.cbSize = 8;
            if (GetLastInputInfo(ref lastInputInfo))
            {
                lastInput = (int)lastInputInfo.dwTime;
            }

            int now = Environment.TickCount;
            int timeoutMs = idleMinutes * 60000;

            if (now - lastInput > timeoutMs)
            {
                if (!idlePauseApplied)
                {
                    idlePauseApplied = true;
                    card1.PauseByIdle();
                    card2.PauseByIdle();
                }
            }
        }

        private void ToggleActive(int index)
        {
            BoardCard target = index == 0 ? card1 : card2;
            BoardCard other = index == 0 ? card2 : card1;
            bool wasRunning = target.IsRunning;
            target.Toggle();
            if (target.IsRunning && !wasRunning)
            {
                if (other.IsRunning)
                {
                    other.Toggle();
                }
            }
            // Пользовательский запуск/останов — сбрасываем авто-паузу.
            idlePauseApplied = false;
        }

        private void OpenSettings()
        {
            string idleSettingsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Stopwatch2",
                "stopwatch_idle_settings.txt");
            var settings = new SettingsWindow(idleMinutes, idleSettingsPath);
            settings.Closed += delegate(object sender, EventArgs e)
            {
                int minutes = 1;
                try
                {
                    if (System.IO.File.Exists(idleSettingsPath))
                    {
                        int.TryParse(System.IO.File.ReadAllText(idleSettingsPath), out minutes);
                    }
                }
                catch
                {
                }
                idleMinutes = minutes;
                idleTimer.Interval = TimeSpan.FromMilliseconds(3000);
            };
            settings.Show();
            PositionSettingsWindow(settings);
        }

        private void PositionSettingsWindow(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }
            PixelRect rect;
            if (!GetWindowRect(handle, out rect))
            {
                return;
            }
            IntPtr mainHandle = new WindowInteropHelper(this).Handle;
            PixelRect mainRect;
            if (mainHandle == IntPtr.Zero || !GetWindowRect(mainHandle, out mainRect))
            {
                return;
            }
            int settingsHeight = rect.Bottom - rect.Top;
            int top = mainRect.Top - settingsHeight - 8;
            SetWindowPos(handle, IntPtr.Zero, mainRect.Left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }

        private void LoadState()
        {
            LoadIdleMinutes();
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    StateDirName);
                string path = System.IO.Path.Combine(dir, StateFileName);
                if (!System.IO.File.Exists(path))
                {
                    return;
                }

                string[] parts = System.IO.File.ReadAllText(path).Split(',');
                if (parts.Length < 2)
                {
                    return;
                }

                long ticks1 = 0;
                long ticks2 = 0;
                long.TryParse(parts[0], out ticks1);
                long.TryParse(parts[1], out ticks2);

                card1.RestoreElapsedTicks(ticks1);
                card2.RestoreElapsedTicks(ticks2);
            }
            catch
            {
                // состояние не прочиталось — используем нули
            }
        }

        private void LoadIdleMinutes()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    StateDirName,
                    IdleSettingsFileName);
                if (System.IO.File.Exists(path))
                {
                    int.TryParse(System.IO.File.ReadAllText(path), out idleMinutes);
                }
            }
            catch
            {
            }
        }

        private void SaveState()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    StateDirName);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                string path = System.IO.Path.Combine(dir, StateFileName);
                System.IO.File.WriteAllText(
                    path,
                    card1.ElapsedTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    card2.ElapsedTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch
            {
                // сохранение не критично
            }
        }

        internal sealed class SettingsWindow : Window
        {
            private ComboBox combo;

            public SettingsWindow(int initialMinutes, string stateFilePath)
            {
                Width = 180;
                Height = 96;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                Background = Palette.WindowBackground;
                FontFamily = new FontFamily("Segoe UI");
                FontSize = 11;
                Topmost = true;
                Content = BuildContent(initialMinutes, stateFilePath);
            }

            private object BuildContent(int initialMinutes, string stateFilePath)
            {
                var border = new Border
                {
                    Background = Palette.WindowBackground,
                    BorderBrush = Palette.WindowBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Width = 180,
                    Height = 96
                };
                var panel = new StackPanel();
                panel.HorizontalAlignment = HorizontalAlignment.Center;
                panel.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(new TextBlock
                {
                    Text = "Пауза при простое",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Foreground = Palette.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                combo = new ComboBox
                {
                    Width = 110,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    Background = Brushes.White,
                    Foreground = Brushes.Black,
                    BorderBrush = Palette.ButtonBorder,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                combo.ItemsSource = new List<string> { "1 минута", "2 минуты", "3 минуты", "5 минут", "10 минут" };
                int index = 0;
                int[] minutesValues = new int[] { 1, 2, 3, 5, 10 };
                for (int i = 0; i < minutesValues.Length; i++)
                {
                    if (minutesValues[i] == initialMinutes)
                    {
                        index = i;
                        break;
                    }
                }
                combo.SelectedIndex = index;

                var ok = Ui.MakeButton("OK", new FontFamily("Segoe UI"), 11, FontWeights.Normal, Palette.Button, Palette.ButtonText, Palette.ButtonBorder, 7);
                ok.Width = 60;
                ok.Height = 24;
                ok.HorizontalAlignment = HorizontalAlignment.Center;
                ok.Click += delegate(object sender, RoutedEventArgs e)
                {
                    int minutes = minutesValues[combo.SelectedIndex];
                    try
                    {
                        System.IO.File.WriteAllText(stateFilePath, minutes.ToString());
                    }
                    catch
                    {
                    }
                    Close();
                };

                panel.Children.Add(combo);
                panel.Children.Add(ok);
                border.Child = panel;
                return border;
            }
        }

        private static Button MakeWindowButton(string glyph, string tip)
        {
            var button = Ui.MakeButton(glyph, IconFont, 8.5, FontWeights.Normal, Brushes.Transparent, Palette.TextMuted, null, 6);
            button.Width = 18;
            button.Height = 18;
            button.ToolTip = tip;
            return button;
        }

        private BoardCard ActiveCard()
        {
            return activeIndex == 0 ? card1 : card2;
        }

        private void ActivateCard(int index)
        {
            activeIndex = index;
            card1.IsActive = index == 0;
            card2.IsActive = index == 1;
        }

        /// <summary>Ловит перетаскивание окна, чтобы прилипать к краям экрана.</summary>
        /// <summary>Прилипание окна к ближайшему краю после перетаскивания.</summary>
        private void SnapWindowToEdges()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            PixelRect bounds;
            if (!GetWindowRect(handle, out bounds))
            {
                return;
            }

            PixelRect snapped = SnapToEdges(bounds);
            if (snapped.Left == bounds.Left && snapped.Top == bounds.Top)
            {
                return;
            }

            SetWindowPos(handle, IntPtr.Zero, snapped.Left, snapped.Top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }

        /// <summary>Прилипание к ближайшему краю монитора или рабочей области.</summary>
        private static PixelRect SnapToEdges(PixelRect rect)
        {
            var info = new MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            IntPtr monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            {
                return rect;
            }

            int dx = SnapDelta(rect.Left, info.Monitor.Left, info.Work.Left);
            int dxRight = SnapDelta(rect.Right, info.Monitor.Right, info.Work.Right);
            if (dxRight != 0 && (dx == 0 || Math.Abs(dxRight) < Math.Abs(dx)))
            {
                dx = dxRight;
            }

            int dy = SnapDelta(rect.Top, info.Monitor.Top, info.Work.Top);
            int dyBottom = SnapDelta(rect.Bottom, info.Monitor.Bottom, info.Work.Bottom);
            if (dyBottom != 0 && (dy == 0 || Math.Abs(dyBottom) < Math.Abs(dy)))
            {
                dy = dyBottom;
            }

            rect.Left += dx;
            rect.Right += dx;
            rect.Top += dy;
            rect.Bottom += dy;
            return rect;
        }

        /// <summary>Смещение до ближайшего края, если он ближе SnapDistance, иначе 0.</summary>
        private static int SnapDelta(int position, int firstTarget, int secondTarget)
        {
            int best = 0;
            int bestDistance = SnapDistance + 1;

            int distance = Math.Abs(position - firstTarget);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = firstTarget - position;
            }

            distance = Math.Abs(position - secondTarget);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = secondTarget - position;
            }

            return bestDistance <= SnapDistance ? best : 0;
        }

        /// <summary>
        /// Прижимает окно к левому нижнему углу четвёртого монитора и подгоняет его
        /// высоту под панель задач этого монитора (в физических пикселях).
        /// </summary>
        private void PlaceInFourthScreenCorner()
        {
            PixelRect monitor;
            PixelRect work;
            if (!TryGetFourthScreen(out monitor, out work))
            {
                return;
            }

            // Высота панели задач = разница между границей монитора и рабочей областью.
            int taskbarHeight = monitor.Bottom - work.Bottom;
            if (taskbarHeight < 24 || taskbarHeight > 120)
            {
                // Панель задач скрыта или расположена сбоку — берём эталонную высоту.
                taskbarHeight = (int)Math.Round(DesignHeight);
            }

            int height = (int)Math.Round(taskbarHeight * PanelHeightFactor);
            if (height < 32)
            {
                height = 32;
            }

            int width = (int)Math.Round(height * DesignWidth / DesignHeight);

            int monitorWidth = monitor.Right - monitor.Left;
            int monitorHeight = monitor.Bottom - monitor.Top;
            if (width > monitorWidth)
            {
                width = monitorWidth;
                height = (int)Math.Round(width * DesignHeight / DesignWidth);
            }
            if (height > monitorHeight)
            {
                height = monitorHeight;
            }

            int left = monitor.Left;
            int top = monitor.Bottom - height;

            IntPtr handle = new WindowInteropHelper(this).Handle;
            SetWindowPos(handle, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate);
        }

        /// <summary>Границы и рабочая область четвёртого монитора.</summary>
        private static bool TryGetFourthScreen(out PixelRect monitor, out PixelRect work)
        {
            var monitors = new List<MonitorInfo>();
            MonitorEnumProc callback = delegate(IntPtr monitorHandle, IntPtr hdc, ref PixelRect rect, IntPtr data)
            {
                var info = new MonitorInfo();
                info.Size = Marshal.SizeOf(typeof(MonitorInfo));
                if (GetMonitorInfo(monitorHandle, ref info))
                {
                    monitors.Add(info);
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

            monitor = new PixelRect();
            work = new PixelRect();
            if (monitors.Count == 0)
            {
                return false;
            }

            MonitorInfo target = monitors[0];
            bool found = false;
            foreach (MonitorInfo info in monitors)
            {
                if (string.Equals(info.Device, FourthScreenDevice, StringComparison.OrdinalIgnoreCase))
                {
                    target = info;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // Имена мониторов не совпали — берём четвёртый слева направо.
                monitors.Sort(delegate(MonitorInfo a, MonitorInfo b)
                {
                    return a.Monitor.Left.CompareTo(b.Monitor.Left);
                });
                target = monitors[monitors.Count >= 4 ? 3 : monitors.Count - 1];
            }

            monitor = target.Monitor;
            work = target.Work;
            return true;
        }
    }
}
