using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AdkillerReaders;

class PlayerFeatureCheck {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    static MediaPlayerWindow win;
    static StringBuilder log = new StringBuilder();
    static bool pass = true;
    static int phase;
    static string resultPath;

    static object Get(string name) {
        var type = typeof(MediaPlayerWindow);
        var property = type.GetProperty(name, F);
        if (property != null) return property.GetValue(win, null);
        return type.GetField(name, F).GetValue(win);
    }
    static void Check(string name, bool ok, string detail) {
        pass = pass && ok;
        log.AppendLine((ok ? "PASS " : "FAIL ") + name + " - " + detail);
    }
    static double Volume() { return ((Slider)Get("volume")).Value; }
    static bool FullScreen() { return (bool)Get("fullScreen"); }
    static Visibility ExitVisibility() { return ((Button)Get("fullscreenExit")).Visibility; }
    static string Feedback() {
        var overlay = (Border)Get("feedbackOverlay");
        if (overlay.Visibility != Visibility.Visible) return "(hidden)";
        return ((TextBlock)Get("feedbackText")).Text;
    }
    static void Press(Key key) {
        var source = PresentationSource.FromVisual(win);
        win.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
    }
    static void Wheel(int delta) {
        var frame = (Border)Get("mediaFrame");
        frame.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
    }
    static void MoveToTop() {
        var frame = (Border)Get("mediaFrame");
        Point p = win.PointToScreen(new Point(win.ActualWidth / 2, 10));
        SetCursorPos((int)p.X, (int)p.Y);
        frame.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
    }
    static void Screenshot(string path) {
        win.UpdateLayout();
        int w = (int)Math.Max(1, win.ActualWidth), h = (int)Math.Max(1, win.ActualHeight);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(win);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(path)) png.Save(output);
    }
    static void Finish() {
        log.AppendLine(pass ? "RESULT PASS" : "RESULT FAIL");
        File.WriteAllText(resultPath, log.ToString(), Encoding.UTF8);
        win.Close();
        Application.Current.Shutdown(pass ? 0 : 1);
    }

    [STAThread] static void Main(string[] args) {
        resultPath = args.Length > 1 ? args[1] : "player-feature-check.txt";
        File.WriteAllText(resultPath + ".start", "start " + args.Length + " " + String.Join("|", args), Encoding.UTF8);
        var app = new Application();
        win = new MediaPlayerWindow(args.Length > 0 ? args[0] : null);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        var started = DateTime.UtcNow;

        var watchdog = new System.Threading.Thread(delegate() {
            System.Threading.Thread.Sleep(60000);
            File.AppendAllText(resultPath, "RESULT FAIL - timeout\n", Encoding.UTF8);
            Environment.Exit(2);
        });
        watchdog.IsBackground = true;
        watchdog.Start();

        timer.Tick += delegate {
            try {
                if (phase == 0) {
                    if (Get("Current") == null) {
                        if ((DateTime.UtcNow - started).TotalSeconds > 25) { Check("media load", false, "timeout"); Finish(); }
                        return;
                    }
                    var media = (MediaElement)Get("media");
                    if (!media.NaturalDuration.HasTimeSpan && (DateTime.UtcNow - started).TotalSeconds < 10) return;
                    ((Slider)Get("volume")).Value = 0;
                    media.IsMuted = true;
                    Check("not fullscreen at start", !FullScreen(), "fullScreen=" + FullScreen());
                    Check("exit X hidden at start", ExitVisibility() == Visibility.Collapsed, "visibility=" + ExitVisibility());
                    phase = 1;
                    return;
                }
                if (phase == 1) {
                    Wheel(120);
                    Check("wheel up +5", Math.Abs(Volume() - 0.05) < 0.001, "volume=" + Volume());
                    string feedback = Feedback();
                    Check("wheel shows no volume overlay", feedback == "(hidden)" || feedback.IndexOf("볼륨") < 0, "feedback=" + feedback);
                    phase = 2;
                    return;
                }
                if (phase == 2) {
                    Wheel(-120);
                    Check("wheel down -5", Math.Abs(Volume()) < 0.001, "volume=" + Volume());
                    Check("wheel still silent", Feedback().IndexOf("볼륨") < 0, "feedback=" + Feedback());
                    Press(Key.Up);
                    Check("arrow up +5 silent", Math.Abs(Volume() - 0.05) < 0.001 && Feedback().IndexOf("볼륨") < 0, "volume=" + Volume() + " feedback=" + Feedback());
                    Press(Key.Down);
                    phase = 3;
                    return;
                }
                if (phase == 3) {
                    Press(Key.Enter);
                    Check("Enter fullscreen", FullScreen(), "fullScreen=" + FullScreen());
                    phase = 4;
                    return;
                }
                if (phase == 4) {
                    var exit = (Button)Get("fullscreenExit");
                    var fill = exit.Background as SolidColorBrush;
                    Check("X centered horizontally", exit.HorizontalAlignment == HorizontalAlignment.Center, "alignment=" + exit.HorizontalAlignment);
                    Check("X background transparent", fill != null && fill.Color.A == 0, "background=" + (fill == null ? "null" : fill.Color.ToString()));
                    Check("X no border", exit.BorderThickness == new Thickness(0), "border=" + exit.BorderThickness);
                    MoveToTop();
                    bool shown = ExitVisibility() == Visibility.Visible;
                    Check("top mouse shows X", shown, "visibility=" + ExitVisibility());
                    if (shown) Screenshot(Path.Combine(Path.GetDirectoryName(resultPath), "player-x-fullscreen.png"));
                    ((Button)Get("fullscreenExit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check("X click exits fullscreen", !FullScreen(), "fullScreen=" + FullScreen());
                    Check("X hidden after exit", ExitVisibility() == Visibility.Collapsed, "visibility=" + ExitVisibility());
                    phase = 5;
                    return;
                }
                if (phase == 5) {
                    Press(Key.Enter);
                    bool entered = FullScreen();
                    Check("Enter again fullscreen", entered, "fullScreen=" + entered);
                    Press(Key.Escape);
                    Check("Esc exits fullscreen", !FullScreen(), "fullScreen=" + FullScreen());
                    timer.Stop();
                    Finish();
                }
            } catch (Exception error) {
                Check("exception", false, error.GetType().Name + ": " + error.Message);
                timer.Stop();
                Finish();
            }
        };
        win.ContentRendered += delegate { File.AppendAllText(resultPath + ".start", "\nrendered", Encoding.UTF8); timer.Start(); };
        try { app.Run(win); } catch (Exception error) { File.AppendAllText(resultPath + ".start", "\nrun-error " + error, Encoding.UTF8); throw; }
    }
}
