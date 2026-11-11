using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AdkillerReaders {
public class MediaPlayerApp : Application {
    [STAThread] public static void Main(string[] args) { var app=new MediaPlayerApp();if(args.Length>1&&args[0]=="--blur-session"){var lines=File.ReadAllLines(args[1]);var editor=new BlurEditorWindow(lines[0]);editor.RestoreSession(lines);app.Run(editor);}else if(args.Length>0&&args[0]=="--blur")app.Run(new BlurEditorWindow(args.Length>1?args[1]:null));else app.Run(new MediaPlayerWindow(args.Length>0?args[0]:null)); }
}
public class MediaPlayerWindow : Window {
    static readonly string[] Exts={".mp4",".m4v",".mov",".avi",".wmv",".webm",".mkv",".ts",".mts",".m2ts",".flv",".vob",".mxf",".ogv",".3gp",".mpg",".mpeg",".mp3",".wav",".wma",".m4a",".aac",".flac",".ogg"};
    static readonly string[] ImageExts={".jpg",".jpeg",".png",".bmp",".gif",".tif",".tiff",".webp",".ico",".jfif"};
    readonly Color panelColor=Color.FromRgb(248,249,251),lineColor=Color.FromRgb(205,210,218),buttonColor=Color.FromRgb(255,255,255);
    readonly MediaElement media=new MediaElement{LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,ScrubbingEnabled=true,Stretch=Stretch.Uniform};
    readonly System.Windows.Controls.Image imageView=new System.Windows.Controls.Image{Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
    readonly WrapPanel ribbon=new WrapPanel{Orientation=Orientation.Horizontal,Background=new SolidColorBrush(Color.FromRgb(242,244,247))};
    readonly Slider position=new Slider{Minimum=0,Margin=new Thickness(14,5,14,1)};
    readonly Slider volume=new Slider{Minimum=0,Maximum=1,Value=.7,Width=105,Margin=new Thickness(5)};
    readonly TextBlock status=new TextBlock{Foreground=new SolidColorBrush(Color.FromRgb(55,61,70)),Margin=new Thickness(14,3,14,7),TextTrimming=TextTrimming.CharacterEllipsis};
    readonly TextBlock modeStatus=new TextBlock{Foreground=new SolidColorBrush(Color.FromRgb(33,112,174)),Margin=new Thickness(12,3,12,7),FontWeight=FontWeights.Bold};
    readonly TextBlock smartInfo=new TextBlock{Foreground=new SolidColorBrush(Color.FromRgb(62,68,78)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,4,12,10)};
    readonly TextBlock smartTitle=new TextBlock{Text="Smart Playlist",Foreground=new SolidColorBrush(Color.FromRgb(36,41,49)),FontSize=16,FontWeight=FontWeights.Bold,Margin=new Thickness(12)};
    readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
    readonly DispatcherTimer fullScreenHideTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
    readonly DispatcherTimer singleClickTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(380)};
    readonly DispatcherTimer feedbackTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(750)};
    readonly ListBox playlist=new ListBox{Background=Brushes.White,Foreground=new SolidColorBrush(Color.FromRgb(38,43,51)),BorderThickness=new Thickness(0),Padding=new Thickness(6)};
    readonly Border playlistPanel=new Border{Width=292,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(205,210,218)),BorderThickness=new Thickness(1,0,0,0)};
    readonly Border bottomBar=new Border{Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(205,210,218)),BorderThickness=new Thickness(0,1,0,0)};
    readonly Border mediaFrame=new Border{Background=Brushes.Black,BorderBrush=new SolidColorBrush(Color.FromRgb(74,78,86)),BorderThickness=new Thickness(0),Margin=new Thickness(0)};
    readonly Border headerBar=new Border();readonly StackPanel fullScreenControls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Visibility=Visibility.Collapsed};
    readonly Button fullscreenExit=new Button{Content="✕",FontSize=26,FontWeight=FontWeights.Bold,Foreground=Brushes.White,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Width=56,Height=56,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,10,0,0),Cursor=Cursors.Hand,Focusable=false,Visibility=Visibility.Collapsed,ToolTip="전체화면 종료 (Esc)",Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=14,ShadowDepth=0,Opacity=0.95}};
    const double VolumeStep=.05;bool exitHover;
    readonly TextBlock feedbackText=new TextBlock{Foreground=Brushes.White,FontSize=27,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center};
    readonly Border feedbackOverlay=new Border{Background=new SolidColorBrush(Color.FromArgb(175,15,18,24)),CornerRadius=new CornerRadius(12),Padding=new Thickness(22,13,22,13),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed,IsHitTestVisible=false};
    Button repeatButton,verticalButton,abButton,playButton; bool playlistVisible; MenuItem playlistMenu,repeatMenu,abMenu; int rotation; TimelinePreview hoverPreview;
    List<string> files=new List<string>();List<string> images=new List<string>();int index=-1,imageIndex=-1,repeatMode=0;bool playing,dragging,fullScreen,verticalMode,imageMode,fallbackActive;TimeSpan? pointA,pointB;WindowStyle oldStyle;WindowState oldState;double normalWidth=1280,normalHeight=780;Point playlistDragStart;

    public MediaPlayerWindow(string initial){Title="얼마루미디어";Width=1280;Height=780;MinWidth=520;MinHeight=520;Background=new SolidColorBrush(Color.FromRgb(245,247,250));WindowStartupLocation=WindowStartupLocation.CenterScreen;AllowDrop=true;ApplyAppIcon();
        var root=new DockPanel();Content=root;BuildHeader();DockPanel.SetDock(headerBar,Dock.Top);root.Children.Add(headerBar);BuildBottom();BuildSmartPanel();DockPanel.SetDock(playlistPanel,Dock.Right);root.Children.Add(playlistPanel);var videoGrid=new Grid();videoGrid.Children.Add(media);RenderOptions.SetBitmapScalingMode(imageView,BitmapScalingMode.HighQuality);videoGrid.Children.Add(imageView);feedbackOverlay.Child=feedbackText;videoGrid.Children.Add(feedbackOverlay);videoGrid.Children.Add(fullscreenExit);Panel.SetZIndex(fullscreenExit,60);videoGrid.Children.Add(bottomBar);mediaFrame.Child=videoGrid;mediaFrame.ContextMenu=BuildContextMenu();root.Children.Add(mediaFrame);
        volume.ValueChanged+=delegate{media.Volume=volume.Value;};position.PreviewMouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(!media.NaturalDuration.HasTimeSpan)return;dragging=true;position.CaptureMouse();SeekFromBar(e,false);e.Handled=true;};position.PreviewMouseMove+=delegate(object s,MouseEventArgs e){if(dragging&&e.LeftButton==MouseButtonState.Pressed){SeekFromBar(e,false);e.Handled=true;}};position.PreviewMouseLeftButtonUp+=delegate(object s,MouseButtonEventArgs e){if(dragging){SeekFromBar(e,true);dragging=false;position.ReleaseMouseCapture();e.Handled=true;}};
        media.MediaOpened+=delegate{fallbackActive=false;if(media.NaturalDuration.HasTimeSpan)position.Maximum=media.NaturalDuration.TimeSpan.TotalSeconds;var openedSource=media.Source;Dispatcher.BeginInvoke(DispatcherPriority.Render,(Action)delegate{if(playing&&media.Source==openedSource)media.Play();});UpdateStatus();};media.MediaEnded+=delegate{HandleMediaEnded();};media.MediaFailed+=delegate(object s,ExceptionRoutedEventArgs e){if(!StartCodecFallback(Current,e.ErrorException))MessageBox.Show("재생할 수 없습니다. 설치된 코덱 지원 플레이어도 찾지 못했습니다.\n\n"+e.ErrorException.Message,"재생 오류");};timer.Tick+=delegate{OnTimer();};timer.Start();playlist.MouseDoubleClick+=delegate{if(playlist.SelectedIndex>=0){if(imageMode){imageIndex=playlist.SelectedIndex;ShowCurrentImage();}else{index=playlist.SelectedIndex;PlayCurrent();}}};playlist.PreviewMouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){playlistDragStart=e.GetPosition(playlist);};playlist.PreviewMouseMove+=BeginPlaylistDrag;
        fullScreenHideTimer.Tick+=delegate{fullScreenHideTimer.Stop();if(fullScreen)HideFullscreenControls();};singleClickTimer.Tick+=delegate{singleClickTimer.Stop();TogglePlay();};feedbackTimer.Tick+=delegate{feedbackTimer.Stop();feedbackOverlay.Visibility=Visibility.Collapsed;};mediaFrame.MouseLeftButtonUp+=OnVideoClick;mediaFrame.MouseEnter+=delegate{ShowFullscreenControls();};mediaFrame.MouseMove+=delegate(object s,MouseEventArgs e){ShowFullscreenControls();UpdateExitHover(e);};mediaFrame.MouseLeave+=delegate{if(!dragging){exitHover=false;fullscreenExit.Visibility=Visibility.Collapsed;HideFullscreenControls();}};mediaFrame.PreviewMouseWheel+=delegate(object s,MouseWheelEventArgs e){if(imageMode||Current==null)return;double next=Math.Max(0,Math.Min(1,volume.Value+(e.Delta>0?VolumeStep:-VolumeStep)));e.Handled=true;if(Math.Abs(next-volume.Value)<0.0001)return;volume.Value=next;};fullscreenExit.Click+=delegate{ToggleFullscreen();};bottomBar.MouseEnter+=delegate{if(fullScreen)fullScreenHideTimer.Stop();};bottomBar.MouseLeave+=delegate{if(fullScreen)fullScreenHideTimer.Start();};
        SizeChanged+=delegate{UpdateResponsiveLayout();};KeyDown+=OnKeyDown;Drop+=OnDrop;DragOver+=delegate(object s,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};SourceInitialized+=delegate{var surface=System.Windows.PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;if(surface!=null)surface.CompositionTarget.RenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;};
        bool initialOpened=false;ContentRendered+=delegate{if(initialOpened)return;initialOpened=true;UpdateResponsiveLayout();Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,(Action)delegate{if(!String.IsNullOrEmpty(initial)&&File.Exists(initial))OpenPath(initial);else status.Text="영상·음악·이미지 파일을 열거나 창으로 끌어 놓으세요";});};hoverPreview=new TimelinePreview(this,position,()=>imageMode?null:Current,()=>position.Maximum,()=>rotation);Closed+=delegate{timer.Stop();fullScreenHideTimer.Stop();singleClickTimer.Stop();feedbackTimer.Stop();hoverPreview.Close();media.Close();};
    }
    void ApplyAppIcon(){try{string appDir=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);DirectoryInfo parent=Directory.GetParent(appDir);if(parent==null)return;string iconPath=Path.Combine(parent.FullName,"assets","eolmaru-video-baby.ico");if(File.Exists(iconPath))Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath,UriKind.Absolute));}catch{} }
    void BuildHeader(){
        var row=new DockPanel{LastChildFill=true};
        var icons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        icons.Children.Add(IconButton("＋","파일 열기",delegate{Open();}));
        playButton=IconButton("▶","재생 / 일시정지",delegate{TogglePlay();});icons.Children.Add(playButton);
        icons.Children.Add(IconButton("↷","오른쪽 90도 회전",delegate{Rotate(90);}));
        icons.Children.Add(IconButton("⛶","전체화면",delegate{ToggleFullscreen();}));
        icons.Children.Add(IconButton("☰","재생목록 표시 / 숨기기",delegate{TogglePlaylist();}));
        DockPanel.SetDock(icons,Dock.Right);row.Children.Add(icons);
        var menu=new Menu{Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Center,FontSize=13};
        var file=new MenuItem{Header="파일"};MenuAction(file,"파일 열기",delegate{Open();});MenuAction(file,"닫기",delegate{Close();});menu.Items.Add(file);
        var playback=new MenuItem{Header="재생"};MenuAction(playback,"재생 / 일시정지    Space",delegate{TogglePlay();});MenuAction(playback,"정지",delegate{Stop();});MenuAction(playback,"이전 파일",delegate{Navigate(-1);});MenuAction(playback,"다음 파일",delegate{Navigate(1);});MenuAction(playback,"5초 뒤로    ←",delegate{Seek(-5);});MenuAction(playback,"5초 앞으로    →",delegate{Seek(5);});
        repeatMenu=MenuAction(playback,"반복: 끔",delegate{CycleRepeat();});abMenu=MenuAction(playback,"구간 반복: 시작 지정",delegate{CycleAB();});
        var sound=new MenuItem{Header="음량"};volume.ToolTip="음량";sound.Items.Add(new MenuItem{Header=volume,StaysOpenOnClick=true});MenuAction(sound,"음소거 / 해제",delegate{media.IsMuted=!media.IsMuted;});playback.Items.Add(sound);menu.Items.Add(playback);
        var view=new MenuItem{Header="보기"};playlistMenu=MenuAction(view,"재생목록",delegate{TogglePlaylist();});playlistMenu.IsCheckable=true;MenuAction(view,"전체화면    F / Esc",delegate{ToggleFullscreen();});MenuAction(view,"왼쪽 90도 회전",delegate{Rotate(-90);});MenuAction(view,"오른쪽 90도 회전",delegate{Rotate(90);});MenuAction(view,"세로 보기    V",delegate{ToggleVertical();});menu.Items.Add(view);
        var tools=new MenuItem{Header="도구"};MenuAction(tools,"블러 제작",delegate{OpenBlurEditor();});MenuAction(tools,"사운드 변환",delegate{OpenSiblingTool("얼마루 사운드","얼마루 사운드.exe");});MenuAction(tools,"이미지 갤러리",delegate{OpenImageGallery();});menu.Items.Add(tools);
        row.Children.Add(menu);headerBar.Child=row;headerBar.Height=40;headerBar.Background=Brushes.White;headerBar.Padding=new Thickness(8,0,6,0);
    }
    MenuItem MenuAction(MenuItem parent,string text,RoutedEventHandler action){var item=new MenuItem{Header=text};item.Click+=action;parent.Items.Add(item);return item;}
    Button IconButton(string glyph,string description,RoutedEventHandler action){var b=new Button{Content=glyph,ToolTip=description,Width=36,Height=32,FontSize=20,Foreground=new SolidColorBrush(Color.FromRgb(55,65,80)),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand};var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);b.Template=new ControlTemplate(typeof(Button)){VisualTree=presenter};System.Windows.Automation.AutomationProperties.SetName(b,description);b.Click+=action;return b;}
    void TogglePlaylist(){playlistVisible=!playlistVisible;UpdateResponsiveLayout();}

    Button HeaderShortcut(string badge,string label,Color accent,RoutedEventHandler click){var content=new StackPanel{Orientation=Orientation.Horizontal};content.Children.Add(new TextBlock{Text=badge,Foreground=new SolidColorBrush(accent),FontSize=11,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,0)});content.Children.Add(new TextBlock{Text=label,Foreground=new SolidColorBrush(Color.FromRgb(42,47,55)),FontSize=12,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});var button=new Button{Content=content,Background=new SolidColorBrush(Color.FromRgb(250,251,253)),BorderBrush=new SolidColorBrush(accent),BorderThickness=new Thickness(1),Padding=new Thickness(9,4,9,4),Margin=new Thickness(0,3,7,3),Cursor=Cursors.Hand};button.Click+=click;return button;}
    void OpenSiblingTool(string folder,string file){try{string appDir=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);DirectoryInfo root=Directory.GetParent(appDir);if(root==null)throw new DirectoryNotFoundException("프로그램 폴더를 찾을 수 없습니다.");string tool=Path.Combine(root.FullName,folder,file);if(!File.Exists(tool))throw new FileNotFoundException("실행 파일을 찾을 수 없습니다.",tool);Process.Start(new ProcessStartInfo(tool){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(tool)});}catch(Exception ex){MessageBox.Show("도구를 열지 못했습니다.\n\n"+ex.Message,"얼마루미디어");}}
    void OpenBlurEditor(){var editor=new BlurEditorWindow(Current);editor.Owner=this;editor.Show();status.Text="얼마루 자체 블러제작 화면을 열었습니다.";}
    void BeginPlaylistDrag(object sender,MouseEventArgs e){if(e.LeftButton!=MouseButtonState.Pressed||playlist.SelectedIndex<0)return;Point p=e.GetPosition(playlist);if(Math.Abs(p.X-playlistDragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-playlistDragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;string path=null;if(imageMode){if(playlist.SelectedIndex<images.Count)path=images[playlist.SelectedIndex];}else if(playlist.SelectedIndex<files.Count)path=files[playlist.SelectedIndex];if(!String.IsNullOrEmpty(path)&&File.Exists(path)){var data=new DataObject(DataFormats.FileDrop,new[]{path});DragDrop.DoDragDrop(playlist,data,DragDropEffects.Copy);}}
    void BuildRibbon(){}
    Border Group(string title,IEnumerable<Button> buttons){var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};foreach(var b in buttons)row.Children.Add(b);var label=new TextBlock{Text=title,Foreground=new SolidColorBrush(Color.FromRgb(105,111,121)),FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,2,0,0)};var stack=new StackPanel();stack.Children.Add(row);stack.Children.Add(label);return new Border{Margin=new Thickness(3),Padding=new Thickness(3),Background=new SolidColorBrush(panelColor),BorderBrush=new SolidColorBrush(lineColor),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Child=stack};}
    Button Tool(string icon,string caption,RoutedEventHandler click){var it=new TextBlock{Text=icon,FontSize=18,HorizontalAlignment=HorizontalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(42,132,198))};var ct=new TextBlock{Text=caption,FontSize=9,TextAlignment=TextAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(48,53,61)),MaxWidth=58};var stack=new StackPanel();stack.Children.Add(it);stack.Children.Add(ct);var b=new Button{Content=stack,ToolTip=caption,MinWidth=54,Height=49,Margin=new Thickness(2),Padding=new Thickness(4,2,4,2),Foreground=new SolidColorBrush(Color.FromRgb(48,53,61)),Background=new SolidColorBrush(buttonColor),BorderBrush=new SolidColorBrush(Color.FromRgb(198,204,213)),Cursor=Cursors.Hand};b.Click+=click;return b;}
    void SetTool(Button b,string icon,string caption,Color color){if(b==null)return;b.Content=icon;b.ToolTip=caption;}
    void BuildBottom(){position.Margin=new Thickness(14,0,14,0);position.Height=28;System.Windows.Automation.AutomationProperties.SetName(position,"영상 탐색");bottomBar.Background=Brushes.Transparent;bottomBar.BorderThickness=new Thickness(0);bottomBar.VerticalAlignment=VerticalAlignment.Bottom;bottomBar.Margin=new Thickness(0,0,0,8);bottomBar.Height=28;bottomBar.Visibility=Visibility.Collapsed;bottomBar.Child=position;}
    void AddMini(string text,RoutedEventHandler click){var b=new Button{Content=text,Foreground=new SolidColorBrush(Color.FromRgb(45,50,58)),Background=new SolidColorBrush(buttonColor),BorderBrush=new SolidColorBrush(lineColor),Padding=new Thickness(14,7,14,7),Margin=new Thickness(3),Cursor=Cursors.Hand};b.Click+=click;fullScreenControls.Children.Add(b);}
    ContextMenu BuildContextMenu(){var menu=new ContextMenu();AddMenu(menu,"파일 열기",delegate{Open();});AddMenu(menu,"재생 / 일시정지",delegate{TogglePlay();});AddMenu(menu,"재생목록 표시 / 숨기기",delegate{TogglePlaylist();});AddMenu(menu,"전체화면 / 창 모드",delegate{ToggleFullscreen();});AddMenu(menu,"오른쪽 90도 회전",delegate{Rotate(90);});AddMenu(menu,"블러 제작",delegate{OpenBlurEditor();});menu.Opened+=delegate{singleClickTimer.Stop();Cursor=Cursors.Arrow;};return menu;}
    void AddMenu(ContextMenu menu,string text,RoutedEventHandler click){var item=new MenuItem{Header=text};item.Click+=click;menu.Items.Add(item);}
    void BuildSmartPanel(){var panel=new DockPanel();var top=new DockPanel{Margin=new Thickness(10,5,5,5)};var close=IconButton("×","재생목록 숨기기",delegate{TogglePlaylist();});DockPanel.SetDock(close,Dock.Right);top.Children.Add(close);top.Children.Add(new TextBlock{Text="재생목록",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold});DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);panel.Children.Add(playlist);playlistPanel.Child=panel;playlistPanel.Visibility=Visibility.Collapsed;}
    string Current{get{return index>=0&&index<files.Count?files[index]:null;}}string CurrentImage{get{return imageIndex>=0&&imageIndex<images.Count?images[imageIndex]:null;}}bool Supported(string p){return Exts.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase);}bool ImageSupported(string p){return ImageExts.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase);}void Open(){var d=new OpenFileDialog{Filter="영상·음악 파일|*.mp4;*.m4v;*.mov;*.avi;*.wmv;*.webm;*.mkv;*.ts;*.mts;*.m2ts;*.flv;*.vob;*.mxf;*.ogv;*.3gp;*.mpg;*.mpeg;*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac;*.ogg|모든 파일|*.*"};if(d.ShowDialog()==true)LoadPath(d.FileName);}void OpenPath(string path){if(ImageSupported(path))OpenImageGallery(path);else LoadPath(path);}
    void OpenImage(){OpenImageGallery();}void OpenImageGallery(){OpenImageGallery(null);}void OpenImageGallery(string imagePath){try{string appDir=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);DirectoryInfo root=Directory.GetParent(appDir);if(root==null)throw new DirectoryNotFoundException("프로그램 폴더를 찾을 수 없습니다.");string viewer=Path.Combine(root.FullName,"Adkiller Image Viewer","얼마루 이미지 갤러리.exe");if(!File.Exists(viewer))viewer=Path.Combine(root.FullName,"Adkiller Image Viewer","adkiller Image Viewer.exe");if(!File.Exists(viewer))throw new FileNotFoundException("이미지 갤러리 실행 파일을 찾을 수 없습니다.");var info=new ProcessStartInfo(viewer){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(viewer)};if(!String.IsNullOrEmpty(imagePath))info.Arguments="\""+imagePath+"\"";Process.Start(info);status.Text="별도 이미지 갤러리를 열었습니다.";}catch(Exception ex){MessageBox.Show("이미지 갤러리를 열지 못했습니다.\n\n"+ex.Message,"이미지 갤러리");}}
    void LoadPath(string path){try{path=Path.GetFullPath(path);if(!Supported(path))throw new InvalidOperationException("지원하지 않는 미디어 형식입니다.");files=Directory.GetFiles(Path.GetDirectoryName(path)).Where(Supported).OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase).ToList();index=files.FindIndex(x=>String.Equals(x,path,StringComparison.OrdinalIgnoreCase));playlist.ItemsSource=files.Select(Path.GetFileName).ToList();playlist.SelectedIndex=index;PlayCurrent();}catch(Exception ex){MessageBox.Show(ex.Message,"파일 열기 실패");}}
    void PlayCurrent(){if(Current==null)return;fallbackActive=false;imageMode=false;imageView.Visibility=Visibility.Collapsed;imageView.Source=null;media.Visibility=Visibility.Visible;smartTitle.Text="미디어 재생목록";position.IsEnabled=true;pointA=null;pointB=null;UpdateABButton();media.Close();media.Source=null;playing=true;media.Source=new Uri(Current);media.Volume=volume.Value;media.Pause();playlist.ItemsSource=files.Select(Path.GetFileName).ToList();playlist.SelectedIndex=index;playlist.ScrollIntoView(playlist.SelectedItem);Title=Path.GetFileName(Current)+" - 얼마루미디어";UpdateStatus();}
    void LoadImagePath(string path){try{path=Path.GetFullPath(path);if(!ImageSupported(path))throw new InvalidOperationException("지원하지 않는 이미지 형식입니다.");images=Directory.GetFiles(Path.GetDirectoryName(path)).Where(ImageSupported).OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase).ToList();imageIndex=images.FindIndex(x=>String.Equals(x,path,StringComparison.OrdinalIgnoreCase));ShowCurrentImage();}catch(Exception ex){MessageBox.Show(ex.Message,"이미지 열기 실패");}}
    void ShowCurrentImage(){if(CurrentImage==null)return;try{media.Pause();playing=false;imageMode=true;media.Visibility=Visibility.Collapsed;imageView.Visibility=Visibility.Visible;position.IsEnabled=false;position.Value=0;smartTitle.Text="이미지 목록";var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(CurrentImage,UriKind.Absolute);bitmap.EndInit();bitmap.Freeze();imageView.Source=bitmap;imageView.Stretch=Stretch.Uniform;playlist.ItemsSource=images.Select(Path.GetFileName).ToList();playlist.SelectedIndex=imageIndex;playlist.ScrollIntoView(playlist.SelectedItem);Title=Path.GetFileName(CurrentImage)+" - 얼마루미디어";status.Text=Path.GetFileName(CurrentImage)+"   ("+(imageIndex+1)+"/"+images.Count+")   ·   "+bitmap.PixelWidth+" × "+bitmap.PixelHeight;smartInfo.Text="현재 이미지\n"+Path.GetFileName(CurrentImage)+"\n\n크기  "+bitmap.PixelWidth+" × "+bitmap.PixelHeight+"\n보기  화면 맞춤\n\n광고 없음 · 인터넷 연결 없음";modeStatus.Text="이미지 보기 · 좌우 키로 이동";}catch(Exception ex){MessageBox.Show("이미지를 표시하지 못했습니다.\n\n"+ex.Message,"이미지 오류");}}
    void NavigateImage(int d){if(!imageMode){if(images.Count==0){OpenImage();return;}imageMode=true;}if(images.Count==0)return;imageIndex=(imageIndex+d+images.Count)%images.Count;ShowCurrentImage();}
    void FitImage(){if(!imageMode){OpenImage();return;}imageView.Stretch=Stretch.Uniform;ShowFeedback("화면 맞춤");}
    void ExitImageMode(){if(!imageMode)return;imageMode=false;imageView.Visibility=Visibility.Collapsed;imageView.Source=null;media.Visibility=Visibility.Visible;position.IsEnabled=true;smartTitle.Text="미디어 재생목록";playlist.ItemsSource=files.Select(Path.GetFileName).ToList();playlist.SelectedIndex=index;Title="얼마루미디어";if(Current!=null){status.Text=Path.GetFileName(Current)+" · 재생 준비";smartInfo.Text="현재 미디어\n"+Path.GetFileName(Current)+"\n\n재생 버튼을 누르면 이어서 재생합니다.\n\n광고 없음 · 인터넷 연결 없음";}else{status.Text="영상 또는 음악 파일을 여세요";smartInfo.Text="미디어 파일이 아직 열리지 않았습니다.";}UpdateModeStatus();}
    void Rotate(int delta){rotation=(rotation+delta+360)%360;media.LayoutTransform=new RotateTransform(rotation);imageView.LayoutTransform=new RotateTransform(rotation);ShowFeedback(rotation+"°");}
    void TogglePlay(){if(imageMode)return;if(Current==null){Open();return;}if(playing){media.Pause();playing=false;ShowFeedback("❚❚");}else{media.Play();playing=true;ShowFeedback("▶");}UpdateModeStatus();}void Stop(){if(imageMode)return;media.Stop();playing=false;position.Value=0;ShowFeedback("■");UpdateModeStatus();}void Navigate(int d){if(imageMode){NavigateImage(d);return;}if(files.Count==0)return;index=(index+d+files.Count)%files.Count;PlayCurrent();}
    void Seek(double seconds){if(imageMode||Current==null)return;TimeSpan target=media.Position+TimeSpan.FromSeconds(seconds);if(target<TimeSpan.Zero)target=TimeSpan.Zero;if(media.NaturalDuration.HasTimeSpan&&target>media.NaturalDuration.TimeSpan)target=media.NaturalDuration.TimeSpan;media.Position=target;position.Value=target.TotalSeconds;ShowFeedback(seconds<0?"−5초":"+5초");UpdateStatus();}
    void SeekFromBar(MouseEventArgs e,bool showFeedback){if(imageMode||!media.NaturalDuration.HasTimeSpan||position.ActualWidth<=0)return;double ratio=e.GetPosition(position).X/position.ActualWidth;ratio=Math.Max(0,Math.Min(1,ratio));TimeSpan target=TimeSpan.FromSeconds(position.Maximum*ratio);position.Value=target.TotalSeconds;media.Position=target;if(showFeedback)ShowFeedback(target.ToString(target.TotalHours>=1?@"hh\:mm\:ss":@"mm\:ss"));UpdateStatus();}
    void ShowFeedback(string text){feedbackText.Text=text;feedbackOverlay.Visibility=Visibility.Visible;feedbackTimer.Stop();feedbackTimer.Start();}
    void OnVideoClick(object sender,MouseButtonEventArgs e){if(e.ChangedButton!=MouseButton.Left)return;if(e.ClickCount>=2){singleClickTimer.Stop();ToggleFullscreen();e.Handled=true;}else{singleClickTimer.Stop();singleClickTimer.Start();e.Handled=true;}}
    bool StartCodecFallback(string path,Exception error){if(fallbackActive)return true;if(String.IsNullOrEmpty(path)||!File.Exists(path))return false;string player=FindCodecPlayer();if(String.IsNullOrEmpty(player))return false;try{fallbackActive=true;playing=false;media.Stop();media.Source=null;var info=new ProcessStartInfo(player){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(player)};if(Path.GetFileName(player).Equals("ffplay.exe",StringComparison.OrdinalIgnoreCase))info.Arguments="-autoexit -loglevel warning \""+path+"\"";else info.Arguments="\""+path+"\"";Process.Start(info);string engine=Path.GetFileNameWithoutExtension(player);status.Text=Path.GetFileName(path)+" · 내부 코덱이 없어 "+engine+" 재생 엔진으로 자동 전환";smartInfo.Text="현재 파일\n"+Path.GetFileName(path)+"\n\n상태  코덱 대체 재생 중\n재생 엔진  "+engine+"\n\n파일 연결은 얼마루미디어로 유지됩니다.";modeStatus.Text="코덱 대체 재생 · "+engine;return true;}catch{fallbackActive=false;return false;}}
    string FindCodecPlayer(){string[] candidates={@"C:\Program Files (x86)\K-Lite Codec Pack\MPC-HC64\mpc-hc64.exe",@"C:\Program Files\K-Lite Codec Pack\MPC-HC64\mpc-hc64.exe",@"C:\Program Files\MPC-BE x64\mpc-be64.exe",@"C:\Program Files\VideoLAN\VLC\vlc.exe",@"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe"};foreach(string candidate in candidates)if(File.Exists(candidate))return candidate;string path=Environment.GetEnvironmentVariable("PATH")??"";foreach(string folder in path.Split(Path.PathSeparator)){try{string candidate=Path.Combine(folder.Trim(),"ffplay.exe");if(File.Exists(candidate))return candidate;}catch{}}try{string packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","WinGet","Packages");if(Directory.Exists(packages)){string ffplay=Directory.GetFiles(packages,"ffplay.exe",SearchOption.AllDirectories).FirstOrDefault();if(!String.IsNullOrEmpty(ffplay))return ffplay;}}catch{}return null;}
    void HandleMediaEnded(){if(imageMode)return;if(repeatMode==2){media.Position=TimeSpan.Zero;media.Play();playing=true;return;}if(index+1<files.Count){index++;PlayCurrent();return;}if(repeatMode==1&&files.Count>0){index=0;PlayCurrent();return;}playing=false;media.Stop();position.Value=position.Maximum;UpdateModeStatus();}
    void CycleRepeat(){if(imageMode)return;repeatMode=(repeatMode+1)%3;UpdateRepeatButton();UpdateModeStatus();}void UpdateRepeatButton(){if(repeatMenu!=null)repeatMenu.Header="반복: "+(repeatMode==0?"끔":repeatMode==1?"전체":"한 파일");if(repeatMode==0)SetTool(repeatButton,"↻","일반",Color.FromRgb(126,191,255));else if(repeatMode==1)SetTool(repeatButton,"↻ ALL","전체",Color.FromRgb(88,224,165));else SetTool(repeatButton,"↻ 1","1곡",Color.FromRgb(255,121,210));}
    void CycleAB(){if(imageMode||Current==null){MessageBox.Show("먼저 미디어 파일을 재생하세요.","구간 반복");return;}if(!pointA.HasValue){pointA=media.Position;pointB=null;}else if(!pointB.HasValue){if(media.Position<=pointA.Value+TimeSpan.FromMilliseconds(300)){MessageBox.Show("B 지점은 A 지점보다 뒤에 지정하세요.","구간 반복");return;}pointB=media.Position;}else{pointA=null;pointB=null;}UpdateABButton();UpdateModeStatus();}void UpdateABButton(){if(abMenu!=null)abMenu.Header="구간 반복: "+(!pointA.HasValue?"시작 지정":!pointB.HasValue?"끝 지정":"해제");if(!pointA.HasValue)SetTool(abButton,"A—B","A 지정",Color.FromRgb(126,191,255));else if(!pointB.HasValue)SetTool(abButton,"A ●","B 지정",Color.FromRgb(255,204,92));else SetTool(abButton,"A↔B","해제",Color.FromRgb(255,112,174));}
    void ToggleVertical(){if(fullScreen)return;if(!verticalMode){normalWidth=ActualWidth;normalHeight=ActualHeight;verticalMode=true;Width=560;Height=Math.Max(760,SystemParameters.WorkArea.Height*.88);playlistPanel.Visibility=Visibility.Collapsed;SetTool(verticalButton,"▯","세로 해제",Color.FromRgb(255,121,210));}else{verticalMode=false;Width=Math.Max(900,normalWidth);Height=Math.Max(600,normalHeight);SetTool(verticalButton,"▯","세로",Color.FromRgb(126,191,255));UpdateResponsiveLayout();}UpdateModeStatus();}
    void OnTimer(){if(playButton!=null){SetTool(playButton,playing?"❚❚":"▶","",Color.FromRgb(42,132,198));playButton.ToolTip=playing?"일시정지":"재생";}if(fullScreenControls.Children.Count>1)((Button)fullScreenControls.Children[1]).Content=playing?"❚❚":"▶";if(imageMode||fallbackActive)return;if(pointA.HasValue&&pointB.HasValue&&media.Position>=pointB.Value){media.Position=pointA.Value;if(!playing){media.Play();playing=true;}}if(!dragging&&media.NaturalDuration.HasTimeSpan){position.Value=media.Position.TotalSeconds;UpdateStatus();}}
    void UpdateStatus(){if(imageMode||Current==null)return;var total=media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan:TimeSpan.Zero;status.Text=Path.GetFileName(Current)+"   ("+(index+1)+"/"+files.Count+")   ·   "+media.Position.ToString(@"hh\:mm\:ss")+" / "+total.ToString(@"hh\:mm\:ss");smartInfo.Text="현재 파일\n"+Path.GetFileName(Current)+"\n\n재생 시간  "+total.ToString(@"hh\:mm\:ss")+"\n상태  "+(playing?"재생 중":"일시정지")+"\n\n광고 없음 · 인터넷 연결 없음";UpdateModeStatus();}
    void UpdateModeStatus(){}
    void UpdateResponsiveLayout(){playlistPanel.Width=Math.Min(292,Math.Max(180,ActualWidth*.3));playlistPanel.Visibility=playlistVisible&&!fullScreen?Visibility.Visible:Visibility.Collapsed;if(playlistMenu!=null)playlistMenu.IsChecked=playlistVisible;}
    void ToggleFullscreen(){if(!fullScreen){oldStyle=WindowStyle;oldState=WindowState;fullScreen=true;WindowStyle=WindowStyle.None;WindowState=WindowState.Maximized;headerBar.Visibility=Visibility.Collapsed;playlistPanel.Visibility=Visibility.Collapsed;HideFullscreenControls();exitHover=false;fullscreenExit.Visibility=Visibility.Collapsed;}else{fullScreen=false;fullScreenHideTimer.Stop();Cursor=Cursors.Arrow;WindowStyle=oldStyle;WindowState=oldState;headerBar.Visibility=Visibility.Visible;bottomBar.Visibility=Visibility.Collapsed;exitHover=false;fullscreenExit.Visibility=Visibility.Collapsed;UpdateResponsiveLayout();}}
    void ShowFullscreenControls(){Cursor=Cursors.Arrow;bottomBar.Visibility=Visibility.Visible;if(fullScreen){fullScreenHideTimer.Stop();fullScreenHideTimer.Start();}}
    void HideFullscreenControls(){fullScreenHideTimer.Stop();bottomBar.Visibility=Visibility.Collapsed;if(hoverPreview!=null)hoverPreview.Close();if(fullScreen&&!exitHover)Cursor=Cursors.None;}
    void UpdateExitHover(MouseEventArgs e){if(!fullScreen){exitHover=false;fullscreenExit.Visibility=Visibility.Collapsed;return;}var p=e.GetPosition(mediaFrame);exitHover=p.Y<=72;fullscreenExit.Visibility=exitHover?Visibility.Visible:Visibility.Collapsed;if(exitHover)Cursor=Cursors.Arrow;}
    void OnDrop(object s,DragEventArgs e){var p=e.Data.GetData(DataFormats.FileDrop) as string[];if(p!=null&&p.Length>0&&File.Exists(p[0]))OpenPath(p[0]);}void OnKeyDown(object s,KeyEventArgs e){if(Keyboard.FocusedElement is MenuItem||Keyboard.FocusedElement is Slider)return;bool used=true;if(e.Key==Key.Space)TogglePlay();else if(e.Key==Key.Left){if(imageMode)NavigateImage(-1);else Seek(-5);}else if(e.Key==Key.Right){if(imageMode)NavigateImage(1);else Seek(5);}else if(e.Key==Key.Up)volume.Value=Math.Min(1,volume.Value+VolumeStep);else if(e.Key==Key.Down)volume.Value=Math.Max(0,volume.Value-VolumeStep);else if(e.Key==Key.F||e.Key==Key.Enter)ToggleFullscreen();else if(e.Key==Key.R)CycleRepeat();else if(e.Key==Key.B)CycleAB();else if(e.Key==Key.V)ToggleVertical();else if(e.Key==Key.L)TogglePlaylist();else if(e.Key==Key.Escape&&fullScreen)ToggleFullscreen();else used=false;if(used)e.Handled=true;}
}

public class BlurEditorWindow : Window {
    readonly VideoPreview preview=new VideoPreview();
    readonly Canvas overlay=new Canvas{Background=Brushes.Transparent,Cursor=Cursors.Cross};
    readonly System.Windows.Shapes.Rectangle[] regions={new System.Windows.Shapes.Rectangle{Stroke=new SolidColorBrush(Color.FromRgb(235,70,70)),StrokeThickness=3,Visibility=Visibility.Collapsed},new System.Windows.Shapes.Rectangle{Stroke=new SolidColorBrush(Color.FromRgb(52,153,235)),StrokeThickness=3,Visibility=Visibility.Collapsed}};
    readonly Slider timeline=new Slider{Minimum=0,Margin=new Thickness(10,5,10,5)};
    readonly Slider[] strengths={new Slider{Minimum=2,Maximum=40,Value=14,TickFrequency=1,IsSnapToTickEnabled=true},new Slider{Minimum=2,Maximum=40,Value=14,TickFrequency=1,IsSnapToTickEnabled=true}};
    readonly TextBox startBox=new TextBox{Text="0.00",Margin=new Thickness(0,0,0,2)},endBox=new TextBox{Margin=new Thickness(0,0,0,2)};
    readonly TextBlock fileText=new TextBlock{Text="영상을 열어 주세요",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(70,76,86)),Margin=new Thickness(2)};
    readonly TextBlock saveFolderText=new TextBlock{Text="원본 영상 폴더",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(70,76,86)),Margin=new Thickness(2)};
    readonly TextBlock statusText=new TextBlock{Text="미리보기 위에서 드래그해 블러 영역을 지정하세요.",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(92,98,108)),Margin=new Thickness(0,12,0,0)};
    readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
    string source,saveFolder;Point dragStart;bool drawing,seeking;int activeRegion;readonly double[] rx=new double[2],ry=new double[2],rw=new double[2],rh=new double[2];
    public BlurEditorWindow(string initial){Title="얼마루 블러제작 · 코덱 내장";Width=Math.Min(1360,SystemParameters.WorkArea.Width);Height=Math.Min(940,SystemParameters.WorkArea.Height);MinWidth=900;MinHeight=620;Background=new SolidColorBrush(Color.FromRgb(246,247,249));WindowStartupLocation=WindowStartupLocation.CenterOwner;AllowDrop=true;LoadSaveFolder();Build();foreach(var region in regions)overlay.Children.Add(region);overlay.MouseLeftButtonDown+=BeginSelection;overlay.MouseMove+=MoveSelection;overlay.MouseLeftButtonUp+=EndSelection;DragOver+=delegate(object s,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};Drop+=OnFileDrop;timeline.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(!previewReady)return;seeking=true;timeline.CaptureMouse();SeekPreview(e);e.Handled=true;};timeline.PreviewMouseMove+=delegate(object sender,MouseEventArgs e){if(seeking&&e.LeftButton==MouseButtonState.Pressed){SeekPreview(e);e.Handled=true;}};timeline.PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){if(!seeking)return;SeekPreview(e);seeking=false;timeline.ReleaseMouseCapture();preview.Position=TimeSpan.FromSeconds(timeline.Value);e.Handled=true;};timeline.LostMouseCapture+=delegate{seeking=false;};preview.MediaFailed+=PreviewFailed;preview.MediaOpened+=delegate{previewReady=preview.NaturalVideoWidth>0&&preview.NaturalVideoHeight>0;if(!previewReady)return;if(preview.NaturalDuration.HasTimeSpan){timeline.Maximum=preview.NaturalDuration.TimeSpan.TotalSeconds;endBox.Text=preview.NaturalDuration.TimeSpan.TotalSeconds.ToString("0.00");}statusText.Text="영상 준비 완료 · 빨간 영역 또는 파란 영역을 선택한 뒤 영상 위에서 드래그하세요.";RefreshPreviewBrushes();Dispatcher.BeginInvoke((Action)delegate{if(previewReady&&!exporting)preview.Play();});};timer.Tick+=delegate{if(previewPlayButton!=null){previewPlayButton.Content=preview.IsPlaying?"❚❚":"▶";previewPlayButton.ToolTip=preview.IsPlaying?"일시정지":"재생";}RefreshPreviewBrushes();if(!seeking&&preview.NaturalDuration.HasTimeSpan)timeline.Value=preview.Position.TotalSeconds;playbackTime.Text=preview.Position.ToString(@"hh\:mm\:ss")+" / "+(preview.NaturalDuration.HasTimeSpan?preview.NaturalDuration.TimeSpan:TimeSpan.Zero).ToString(@"hh\:mm\:ss");};timer.Start();Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs args){if(exporting){args.Cancel=true;statusText.Text="영상 저장 중입니다. 저장이 끝나면 창을 닫을 수 있습니다.";}};hoverPreview=new TimelinePreview(this,timeline,()=>source,()=>timeline.Maximum,()=>rotation);Closed+=delegate{timer.Stop();hoverPreview.Close();preview.Close();};if(!String.IsNullOrEmpty(initial)&&File.Exists(initial))LoadVideo(initial);}
    Button previewPlayButton; int rotation; Grid previewStage; TimelinePreview hoverPreview;
    void SeekPreview(MouseEventArgs e){timeline.Value=timeline.Maximum*Math.Max(0,Math.Min(1,e.GetPosition(timeline).X/Math.Max(1,timeline.ActualWidth)));}
    void RotatePreview(int delta){if(exporting)return;rotation=(rotation+delta+360)%360;previewStage.LayoutTransform=new RotateTransform(rotation);statusText.Text="회전 "+rotation+"° · 저장 영상에도 적용됩니다.";}
    readonly TextBlock playbackTime=new TextBlock{Text="00:00:00 / 00:00:00",FontSize=13,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(79,91,108)),Margin=new Thickness(14,0,4,0)};
    readonly Button[] regionButtons=new Button[2];
    void Build(){
        FontFamily=new FontFamily("Malgun Gothic");FontSize=14;
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var header=new Grid{Margin=new Thickness(0,0,0,16)};
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var brand=new StackPanel{Margin=new Thickness(0,0,24,0),VerticalAlignment=VerticalAlignment.Center};
        brand.Children.Add(new TextBlock{Text="얼마루 블러제작",FontSize=22,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(28,39,54))});
        brand.Children.Add(new TextBlock{Text="영역마다 원하는 만큼, 간편하게",FontSize=12,Foreground=Brushes.SlateGray,Margin=new Thickness(0,3,0,0)});
        header.Children.Add(brand);
        fileText.TextWrapping=TextWrapping.NoWrap;fileText.TextTrimming=TextTrimming.CharacterEllipsis;fileText.VerticalAlignment=VerticalAlignment.Center;fileText.FontSize=14;fileText.Margin=new Thickness(8,0,20,0);
        Grid.SetColumn(fileText,1);header.Children.Add(fileText);
        var open=Action("영상 열기",OpenVideo);open.MinWidth=124;Grid.SetColumn(open,2);header.Children.Add(open);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        statusText.Margin=new Thickness(12,8,12,8);statusText.FontSize=13;statusText.MaxHeight=60;
        var statusBar=new Border{Background=new SolidColorBrush(Color.FromRgb(231,237,244)),CornerRadius=new CornerRadius(7),Margin=new Thickness(0,14,0,0),Child=statusText};
        DockPanel.SetDock(statusBar,Dock.Bottom);root.Children.Add(statusBar);
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(348)});
        root.Children.Add(body);
        var left=new DockPanel{Margin=new Thickness(0,0,18,0)};body.Children.Add(left);
        var caption=new TextBlock{Text="영상 미리보기 · 무음 / 저장 영상은 원본 소리 유지",FontSize=15,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,10),Foreground=new SolidColorBrush(Color.FromRgb(46,58,76))};
        DockPanel.SetDock(caption,Dock.Top);left.Children.Add(caption);
        var playback=new StackPanel{Margin=new Thickness(0,10,0,0)};
        timeline.Height=30;timeline.Margin=new Thickness(0,4,0,6);playback.Children.Add(timeline);
        System.Windows.Automation.AutomationProperties.SetName(timeline,"영상 탐색");
        var controls=new DockPanel();
        var play=Action("▶",TogglePreview);play.MinWidth=52;play.ToolTip="재생 / 일시정지";previewPlayButton=play;
        DockPanel.SetDock(play,Dock.Left);controls.Children.Add(play);var turn=Action("↷ 90°",delegate{RotatePreview(90);});turn.ToolTip="오른쪽으로 90도 회전 · 네 번 누르면 원위치";DockPanel.SetDock(turn,Dock.Right);controls.Children.Add(turn);controls.Children.Add(playbackTime);playback.Children.Add(controls);
        DockPanel.SetDock(playback,Dock.Bottom);left.Children.Add(playback);
        var stage=new Grid{Background=new SolidColorBrush(Color.FromRgb(21,27,36)),ClipToBounds=true};
        previewStage=stage;stage.Children.Add(preview);stage.Children.Add(overlay);
        left.Children.Add(new Border{Background=stage.Background,CornerRadius=new CornerRadius(10),Child=stage});
        overlay.SizeChanged+=ResizeRegions;
        var sidebar=new DockPanel();Grid.SetColumn(sidebar,1);body.Children.Add(sidebar);
        var savePanel=new StackPanel();
        savePanel.Children.Add(SectionTitle("03  저장"));
        saveFolderText.FontSize=12;saveFolderText.MaxHeight=36;saveFolderText.Margin=new Thickness(0,0,0,8);savePanel.Children.Add(saveFolderText);
        savePanel.Children.Add(Action("저장 폴더 선택",ChooseSaveFolder));
        var save=Action("블러 영상 저장",Export);save.Height=50;save.FontSize=16;save.FontWeight=FontWeights.Bold;save.Margin=new Thickness(0,8,0,0);
        save.Background=new SolidColorBrush(Color.FromRgb(224,99,40));save.BorderBrush=save.Background;save.Foreground=Brushes.White;savePanel.Children.Add(save);
        var saveCard=Card(savePanel);saveCard.Margin=new Thickness(0,10,0,0);DockPanel.SetDock(saveCard,Dock.Bottom);sidebar.Children.Add(saveCard);
        var settings=new StackPanel();
        var times=new StackPanel();times.Children.Add(SectionTitle("01  적용 구간"));
        times.Children.Add(new TextBlock{Text="지정한 시간 동안 두 영역에 블러를 적용합니다.",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,0,0,10)});
        var timeGrid=new Grid();timeGrid.ColumnDefinitions.Add(new ColumnDefinition());timeGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});timeGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for(int i=0;i<2;i++){
            var col=new StackPanel();col.Children.Add(new TextBlock{Text=i==0?"시작 (초)":"종료 (초)",FontSize=13,Margin=new Thickness(0,0,0,6)});
            var box=i==0?startBox:endBox;box.Height=40;box.FontSize=16;box.Padding=new Thickness(8,5,8,5);box.VerticalContentAlignment=VerticalAlignment.Center;box.Margin=new Thickness(0,0,0,8);
            box.BorderBrush=new SolidColorBrush(Color.FromRgb(205,213,224));System.Windows.Automation.AutomationProperties.SetName(box,i==0?"블러 시작 시간 (초)":"블러 종료 시간 (초)");col.Children.Add(box);
            int slot=i;var mark=Action("현재 위치",delegate{(slot==0?startBox:endBox).Text=preview.Position.TotalSeconds.ToString("0.00");});mark.FontSize=13;mark.Height=38;col.Children.Add(mark);
            Grid.SetColumn(col,i*2);timeGrid.Children.Add(col);
        }
        times.Children.Add(timeGrid);settings.Children.Add(Card(times));
        settings.Children.Add(new TextBlock{Text="02  블러 영역",FontSize=16,FontWeight=FontWeights.Bold,Margin=new Thickness(2,10,0,8)});
        for(int i=0;i<2;i++){
            int regionIndex=i;string name=i==0?"빨간 영역 1":"파란 영역 2";
            var panel=new StackPanel();var heading=new DockPanel();
            var number=new TextBlock{Text=strengths[i].Value.ToString("0"),FontSize=22,FontWeight=FontWeights.Bold,Foreground=regions[i].Stroke};
            DockPanel.SetDock(number,Dock.Right);heading.Children.Add(number);
            heading.Children.Add(new TextBlock{Text=name,FontSize=15,FontWeight=FontWeights.Bold,Foreground=regions[i].Stroke,VerticalAlignment=VerticalAlignment.Center});panel.Children.Add(heading);
            strengths[i].Height=28;strengths[i].Margin=new Thickness(0,4,0,4);
            System.Windows.Automation.AutomationProperties.SetName(strengths[i],name+" 블러 강도");
            strengths[i].ValueChanged+=delegate{number.Text=strengths[regionIndex].Value.ToString("0");RefreshPreviewBrush(regionIndex);};
            panel.Children.Add(strengths[i]);
            var scale=new DockPanel{Margin=new Thickness(0,0,0,4)};var strong=new TextBlock{Text="강하게",FontSize=11,Foreground=Brushes.SlateGray};DockPanel.SetDock(strong,Dock.Right);scale.Children.Add(strong);scale.Children.Add(new TextBlock{Text="약하게",FontSize=11,Foreground=Brushes.SlateGray});panel.Children.Add(scale);
            var actions=new Grid();actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(8)});actions.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(94)});
            var choose=Action("영역 지정",delegate{SelectRegion(regionIndex);});choose.BorderBrush=regions[i].Stroke;regionButtons[i]=choose;actions.Children.Add(choose);
            var reset=Action("다시 지정",delegate{ResetRegion(regionIndex);SelectRegion(regionIndex);});reset.FontSize=13;Grid.SetColumn(reset,2);actions.Children.Add(reset);panel.Children.Add(actions);
            var card=Card(panel);card.BorderBrush=new SolidColorBrush(i==0?Color.FromRgb(244,207,207):Color.FromRgb(198,221,241));card.Margin=new Thickness(0,0,0,10);settings.Children.Add(card);
        }
        sidebar.Children.Add(new ScrollViewer{Content=settings,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,5,0)});
        SelectRegion(0);
    }
    Border Card(UIElement child){return new Border{Child=child,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(217,224,233)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(12)};}
    TextBlock SectionTitle(string text){return new TextBlock{Text=text,FontSize=16,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(40,52,70)),Margin=new Thickness(0,0,0,10)};}
    Button Action(string text,RoutedEventHandler action){var b=new Button{Content=text,Height=42,Padding=new Thickness(10,4,10,4),FontSize=14,Background=new SolidColorBrush(Color.FromRgb(247,249,252)),Foreground=new SolidColorBrush(Color.FromRgb(40,52,70)),BorderBrush=new SolidColorBrush(Color.FromRgb(202,212,225)),BorderThickness=new Thickness(1),Cursor=Cursors.Hand,HorizontalContentAlignment=HorizontalAlignment.Center};b.Click+=action;return b;}
    void SelectRegion(int i){
        activeRegion=i;
        for(int n=0;n<2;n++)if(regionButtons[n]!=null){regionButtons[n].Content=n==i?"선택됨 · 영역 그리기":"영역 지정";regionButtons[n].FontSize=n==i?12:14;regionButtons[n].Background=n==i?regions[n].Stroke:new SolidColorBrush(Color.FromRgb(247,249,252));regionButtons[n].Foreground=n==i?Brushes.White:new SolidColorBrush(Color.FromRgb(40,52,70));}
        statusText.Text=(i==0?"빨간 영역 1":"파란 영역 2")+" 선택 · 영상 위에서 드래그하세요. 각 영역의 강도는 따로 적용됩니다.";
    }
    void ResizeRegions(object sender,SizeChangedEventArgs e){
        double vw=preview.NaturalVideoWidth,vh=preview.NaturalVideoHeight;
        if(vw<=0||vh<=0||e.PreviousSize.Width<=0||e.PreviousSize.Height<=0)return;
        double oldScale=Math.Min(e.PreviousSize.Width/vw,e.PreviousSize.Height/vh),newScale=Math.Min(e.NewSize.Width/vw,e.NewSize.Height/vh);
        if(oldScale<=0||newScale<=0)return;
        double oldX=(e.PreviousSize.Width-vw*oldScale)/2,oldY=(e.PreviousSize.Height-vh*oldScale)/2;
        double newX=(e.NewSize.Width-vw*newScale)/2,newY=(e.NewSize.Height-vh*newScale)/2;
        for(int i=0;i<2;i++)if(rw[i]>0&&rh[i]>0){rx[i]=newX+(rx[i]-oldX)*newScale/oldScale;ry[i]=newY+(ry[i]-oldY)*newScale/oldScale;rw[i]*=newScale/oldScale;rh[i]*=newScale/oldScale;Canvas.SetLeft(regions[i],rx[i]);Canvas.SetTop(regions[i],ry[i]);regions[i].Width=rw[i];regions[i].Height=rh[i];}
        RefreshPreviewBrushes();
    }
    string FolderSettingFile(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EolmaruMedia","blur-output-folder.txt");}
    void LoadSaveFolder(){try{string setting=FolderSettingFile();if(File.Exists(setting)){string saved=File.ReadAllText(setting).Trim();if(Directory.Exists(saved))saveFolder=saved;}}catch{}saveFolderText.Text=String.IsNullOrEmpty(saveFolder)?"원본 영상 폴더":saveFolder;}
    void ChooseSaveFolder(object s,RoutedEventArgs e){using(var d=new System.Windows.Forms.FolderBrowserDialog()){d.Description="블러 처리된 영상을 저장할 폴더를 선택하세요";d.SelectedPath=Directory.Exists(saveFolder)?saveFolder:(!String.IsNullOrEmpty(source)?Path.GetDirectoryName(source):Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));if(d.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;saveFolder=d.SelectedPath;saveFolderText.Text=saveFolder;try{string setting=FolderSettingFile();Directory.CreateDirectory(Path.GetDirectoryName(setting));File.WriteAllText(setting,saveFolder);}catch{}statusText.Text="저장 폴더 지정 완료: "+saveFolder;}}
    string OutputPath(){string folder=Directory.Exists(saveFolder)?saveFolder:Path.GetDirectoryName(source);string stem="블러_"+Path.GetFileNameWithoutExtension(source);string path=Path.Combine(folder,stem+".mp4");int n=2;while(File.Exists(path)){path=Path.Combine(folder,stem+"_"+n+".mp4");n++;}return path;}
    void OpenVideo(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="영상 파일|*.mp4;*.m4v;*.mov;*.avi;*.wmv;*.webm;*.mkv;*.ts;*.mts;*.m2ts;*.flv;*.vob;*.mxf;*.ogv;*.3gp;*.mpg;*.mpeg|모든 파일|*.*"};if(d.ShowDialog()==true)LoadVideo(d.FileName);}
    bool previewReady;
    void LoadVideo(string path){
        if(exporting){MessageBox.Show("저장이 끝난 뒤 다른 영상을 열어 주세요.");return;}
        if(!File.Exists(path)){MessageBox.Show("영상 파일을 찾을 수 없습니다.");return;}
        previewReady=false;rotation=0;previewStage.LayoutTransform=Transform.Identity;source=Path.GetFullPath(path);
        fileText.Text=Path.GetFileName(source);fileText.ToolTip=source;
        startBox.Text="0.00";endBox.Text="";timeline.Value=0;timeline.Maximum=0;
        ResetRegion(0);ResetRegion(1);SelectRegion(0);
        statusText.Text="영상 화면을 직접 읽는 중입니다. 긴 영상도 전체 변환 없이 열립니다...";
        preview.Open(source);
    }
    void PreviewFailed(object sender,PreviewErrorEventArgs e){
        statusText.Text="영상 읽기 실패: "+e.ErrorException.Message;
    }
    void TogglePreview(object s,RoutedEventArgs e){if(!previewReady||exporting)return;if(preview.IsPlaying){preview.Pause();}else{preview.Play();}}
    void BeginSelection(object s,MouseButtonEventArgs e){if(!previewReady||exporting)return;drawing=true;dragStart=e.GetPosition(overlay);overlay.CaptureMouse();var region=regions[activeRegion];region.Visibility=Visibility.Visible;Canvas.SetLeft(region,dragStart.X);Canvas.SetTop(region,dragStart.Y);region.Width=region.Height=1;e.Handled=true;}
    void MoveSelection(object s,MouseEventArgs e){if(!drawing||e.LeftButton!=MouseButtonState.Pressed)return;Point p=e.GetPosition(overlay);int i=activeRegion;rx[i]=Math.Max(0,Math.Min(dragStart.X,p.X));ry[i]=Math.Max(0,Math.Min(dragStart.Y,p.Y));rw[i]=Math.Min(overlay.ActualWidth,Math.Max(dragStart.X,p.X))-rx[i];rh[i]=Math.Min(overlay.ActualHeight,Math.Max(dragStart.Y,p.Y))-ry[i];var region=regions[i];Canvas.SetLeft(region,rx[i]);Canvas.SetTop(region,ry[i]);region.Width=rw[i];region.Height=rh[i];RefreshPreviewBrush(i);}
    void EndSelection(object s,MouseButtonEventArgs e){if(!drawing)return;drawing=false;overlay.ReleaseMouseCapture();int i=activeRegion;RefreshPreviewBrush(i);statusText.Text="영역 "+(i+1)+": "+Math.Round(rw[i])+" × "+Math.Round(rh[i])+" · 즉시 블러 미리보기 적용됨";e.Handled=true;}
    void ResetRegion(int i){regions[i].Visibility=Visibility.Collapsed;regions[i].Fill=null;rx[i]=ry[i]=rw[i]=rh[i]=0;activeRegion=i;statusText.Text="영역 "+(i+1)+"을 미리보기에서 다시 드래그하세요.";}
    void RefreshPreviewBrushes(){RefreshPreviewBrush(0);RefreshPreviewBrush(1);}
    readonly ImageSource[] lastRegionSource=new ImageSource[2];
    readonly string[] lastRegionKey=new string[2];
    void RefreshPreviewBrush(int i){
        var frame=preview.Source as System.Windows.Media.Imaging.BitmapSource;
        if(rw[i]<2||rh[i]<2||frame==null)return;
        double scale=Math.Min(overlay.ActualWidth/preview.NaturalVideoWidth,overlay.ActualHeight/preview.NaturalVideoHeight);if(scale<=0)return;
        double width=preview.NaturalVideoWidth*scale,height=preview.NaturalVideoHeight*scale,ox=(overlay.ActualWidth-width)/2,oy=(overlay.ActualHeight-height)/2;
        int x=Math.Max(0,Math.Min(frame.PixelWidth-1,(int)((rx[i]-ox)/width*frame.PixelWidth))),y=Math.Max(0,Math.Min(frame.PixelHeight-1,(int)((ry[i]-oy)/height*frame.PixelHeight)));
        int w=Math.Max(1,Math.Min(frame.PixelWidth-x,(int)(rw[i]/width*frame.PixelWidth))),h=Math.Max(1,Math.Min(frame.PixelHeight-y,(int)(rh[i]/height*frame.PixelHeight)));
        string key=x+","+y+","+w+","+h+","+strengths[i].Value;
        if(lastRegionSource[i]==frame&&lastRegionKey[i]==key&&regions[i].Fill!=null)return;
        var crop=new System.Windows.Media.Imaging.CroppedBitmap(frame,new Int32Rect(x,y,w,h));
        double shrink=1/Math.Max(2,strengths[i].Value*0.75);
        var soft=new System.Windows.Media.Imaging.TransformedBitmap(crop,new ScaleTransform(Math.Max(1.0/w,shrink),Math.Max(1.0/h,shrink)));soft.Freeze();
        regions[i].Fill=new ImageBrush(soft){Stretch=Stretch.Fill};regions[i].Effect=null;RenderOptions.SetBitmapScalingMode(regions[i],BitmapScalingMode.HighQuality);
        lastRegionSource[i]=frame;lastRegionKey[i]=key;
    }
    void OnFileDrop(object s,DragEventArgs e){var paths=e.Data.GetData(DataFormats.FileDrop) as string[];if(paths!=null&&paths.Length>0&&File.Exists(paths[0]))LoadVideo(paths[0]);e.Handled=true;}
    bool exporting;
    public void RestoreSession(string[] lines) {
        bool restored=false;preview.MediaOpened += delegate { if(restored)return;restored=true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (System.Action)delegate {
                var inv=System.Globalization.CultureInfo.InvariantCulture;
                startBox.Text=Double.Parse(lines[1],inv).ToString();
                endBox.Text=Double.Parse(lines[2],inv).ToString();
                var restoredStrengths=lines[3].Split(',');for(int i=0;i<2;i++)strengths[i].Value=Double.Parse(restoredStrengths[Math.Min(i,restoredStrengths.Length-1)],inv);
                preview.Position=TimeSpan.FromSeconds(Double.Parse(lines[4],inv));
                double vw=preview.NaturalVideoWidth,vh=preview.NaturalVideoHeight;
                double scale=Math.Min(overlay.ActualWidth/vw,overlay.ActualHeight/vh);
                double ox=(overlay.ActualWidth-vw*scale)/2,oy=(overlay.ActualHeight-vh*scale)/2;
                for(int i=0;i<Math.Min(2,lines.Length-5);i++){
                    var v=lines[i+5].Split(',').Select(x=>Double.Parse(x,inv)).ToArray();
                    rx[i]=ox+v[0]*scale;ry[i]=oy+v[1]*scale;rw[i]=v[2]*scale;rh[i]=v[3]*scale;
                    Canvas.SetLeft(regions[i],rx[i]);Canvas.SetTop(regions[i],ry[i]);
                    regions[i].Width=rw[i];regions[i].Height=rh[i];regions[i].Visibility=Visibility.Visible;
                }
                RefreshPreviewBrushes();statusText.Text="이전 블러 영역과 적용 구간을 복원했습니다.";
            });
        };
    }
    public static string BuildBlurFilter(List<int[]> mapped,int[] blurStrengths,double from,double to){
        var inv=System.Globalization.CultureInfo.InvariantCulture;
        string enable="between(t,"+from.ToString(inv)+","+to.ToString(inv)+")";
        // FFmpeg validates luma and subsampled chroma planes separately.

        var filter=new StringBuilder("[0:v:0]split="+(mapped.Count+1)+"[base]");
        for(int i=0;i<mapped.Count;i++)filter.Append("[p"+i+"]");
        filter.Append(";");
        for(int i=0;i<mapped.Count;i++){
            var a=mapped[i];int blur=blurStrengths[i];
        string kernel="boxblur=luma_radius='min("+blur+",max(0,floor(min(w,h)/2)-1))':luma_power=1:chroma_radius='min("+blur+",max(0,floor(min(cw,ch)/2)-1))':chroma_power=1";
            filter.Append("[p"+i+"]crop="+a[2]+":"+a[3]+":"+a[0]+":"+a[1]+","+kernel+"[b"+i+"];");
        }
        for(int i=0;i<mapped.Count;i++){
            var a=mapped[i];string input=i==0?"base":"s"+(i-1);string output=i==mapped.Count-1?"v":"s"+i;
            filter.Append("["+input+"][b"+i+"]overlay="+a[0]+":"+a[1]+":enable='"+enable+"'["+output+"]");
            if(i<mapped.Count-1)filter.Append(";");
        }
        return filter.ToString();
    }
    void Export(object s,RoutedEventArgs e){
        if(exporting)return;if(!previewReady){MessageBox.Show("영상 미리보기가 준비된 뒤 블러 영역을 지정하고 저장하세요.");return;}
        if(String.IsNullOrEmpty(source)||!File.Exists(source)){MessageBox.Show("먼저 영상을 여세요.");return;}
        var enabled=new List<int>();for(int i=0;i<2;i++)if(rw[i]>=4&&rh[i]>=4)enabled.Add(i);
        if(enabled.Count==0){MessageBox.Show("미리보기에서 블러 영역을 한 곳 이상 드래그하세요.");return;}
        double from,to;
        if(!Double.TryParse(startBox.Text,out from)||!Double.TryParse(endBox.Text,out to)||Double.IsNaN(from)||Double.IsNaN(to)||Double.IsInfinity(from)||Double.IsInfinity(to)||from<0||to<=from){MessageBox.Show("시작·종료 시간을 확인하세요.");return;}
        if(preview.NaturalVideoWidth<=0||preview.NaturalVideoHeight<=0){MessageBox.Show("영상 정보를 불러온 뒤 다시 저장하세요.");return;}
        string ff=FindFfmpeg();if(ff==null){MessageBox.Show("FFmpeg를 찾지 못했습니다.");return;}
        var mapped=enabled.Select(MapRegion).ToList();if(mapped.Any(v=>v[2]<2||v[3]<2)){MessageBox.Show("선택 영역이 영상 밖에 있습니다.");return;}
        string outputPath=OutputPath();string pendingPath=Path.Combine(Path.GetDirectoryName(outputPath),"."+Path.GetFileNameWithoutExtension(outputPath)+"-"+Guid.NewGuid().ToString("N")+".partial.mp4");double expectedDuration=preview.NaturalDuration.TimeSpan.TotalSeconds;
        string logFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EolmaruMedia","logs");
        string logPath=Path.Combine(logFolder,"blur-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".log");
        string filter=BuildBlurFilter(mapped,enabled.Select(i=>(int)strengths[i].Value).ToArray(),from,to).Replace("[0:v:0]split=","[0:v:0]scale="+preview.NaturalVideoWidth+":"+preview.NaturalVideoHeight+",setsar=1,split=");
        if(rotation!=0)filter=filter.Substring(0,filter.Length-3)+"[rot];[rot]"+(rotation==90?"transpose=1":rotation==180?"hflip,vflip":"transpose=2")+"[v]";
        string args="-hide_banner -nostdin -n -threads 2 -filter_complex_threads 2 -i \""+source+"\" -filter_complex \""+filter+"\" -map \"[v]\" -map 0:a? -c:v libx264 -threads 4 -preset fast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart \""+pendingPath+"\"";
        preview.Pause();hoverPreview.Close();var button=s as Button;exporting=true;if(button!=null)button.IsEnabled=false;
        statusText.Text="블러 "+mapped.Count+"개 영역을 저장하는 중입니다...";
        System.Threading.Tasks.Task.Factory.StartNew(delegate{
            int exitCode=-1;string detail="";
            try{
                Directory.CreateDirectory(logFolder);
                using(var log=new StreamWriter(logPath,false,Encoding.UTF8)){
                    log.WriteLine(ff+" "+args);log.Flush();
                    using(var p=new Process{StartInfo=new ProcessStartInfo(ff,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}}){
                        p.Start();string line;DateTime lastUpdate=DateTime.MinValue;
                        while((line=p.StandardError.ReadLine())!=null){
                            log.WriteLine(line);log.Flush();
                            if(!String.IsNullOrWhiteSpace(line))detail=line;
                            if(line.Contains("time=")&&(DateTime.UtcNow-lastUpdate).TotalSeconds>=1){
                                lastUpdate=DateTime.UtcNow;
                                var match=Regex.Match(line,@"time=(\d+:\d+:\d+\.\d+)");TimeSpan elapsed;
                                if(match.Success&&TimeSpan.TryParse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture,out elapsed)){
                                    double seconds=elapsed.TotalSeconds;
                                    Dispatcher.BeginInvoke((System.Action)delegate{
                                        double duration=preview.NaturalDuration.HasTimeSpan?preview.NaturalDuration.TimeSpan.TotalSeconds:to;
                                        statusText.Text="저장 중 "+Math.Min(99,seconds/Math.Max(1,duration)*100).ToString("0")+"% · "+TimeSpan.FromSeconds(seconds).ToString();
                                    });
                                }
                            }
                        }
                        p.WaitForExit();exitCode=p.ExitCode;
                    }
                }
            }catch(Exception ex){detail=ex.Message;}
            bool success=false;
            try{
                if(exitCode==0&&File.Exists(pendingPath)&&new FileInfo(pendingPath).Length>0){
                    using(var check=new Process{StartInfo=new ProcessStartInfo(VideoEngine.Find("ffprobe.exe"),"-v error -select_streams v:0 -show_entries stream=codec_name:format=duration -of default=noprint_wrappers=1 \""+pendingPath+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
                        check.Start();var errors=check.StandardError.ReadToEndAsync();var output=check.StandardOutput.ReadToEndAsync();if(!check.WaitForExit(30000)){try{check.Kill();}catch{}throw new IOException("저장 파일 검사 제한 시간(30초)을 초과했습니다.");}string report=output.Result;double savedDuration;var match=Regex.Match(report,@"duration=([0-9.]+)");
                        if(check.ExitCode!=0||!report.Contains("codec_name=h264")||!Double.TryParse(match.Groups[1].Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out savedDuration)||Math.Abs(savedDuration-expectedDuration)>Math.Max(2,expectedDuration*0.01))throw new IOException("저장된 영상의 재생 시간 또는 형식 검증에 실패했습니다. "+errors.Result);
                    }
                    File.Move(pendingPath,outputPath);success=true;
                }
            }catch(Exception ex){detail=ex.Message;}
            // Keep an unsuccessful result for recovery; never discard a long encoding silently.
            try{File.AppendAllText(logPath,"\nRESULT="+(success?"SUCCESS":"FAILED")+"\n"+(success?outputPath:detail)+"\n",Encoding.UTF8);}catch{}
            Dispatcher.BeginInvoke((System.Action)delegate{
                exporting=false;if(button!=null)button.IsEnabled=true;
                if(success){statusText.Text="저장 완료: "+outputPath;try{ShowExportComplete(outputPath);}catch(Exception popupError){File.AppendAllText(logPath,"POPUP: "+popupError.ToString());MessageBox.Show(this,"저장 완료\n"+outputPath,"블러 영상 저장 완료");}}
                else{statusText.Text="저장 실패: "+detail;if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();MessageBox.Show(this,"블러 영상을 저장하지 못했습니다.\n\n"+detail+"\n\n오류 기록: "+logPath,"얼마루 블러제작");}
            });
        });
    }
    void ShowExportComplete(string path){
        // An owned, topmost dialog remains visible even while another app is active.
        if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        var dialog=new Window{Title="블러 영상 저장 완료",Owner=this,Width=560,MaxWidth=SystemParameters.WorkArea.Width,MaxHeight=SystemParameters.WorkArea.Height,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowInTaskbar=false,Topmost=true,Background=Brushes.White,FontFamily=new FontFamily("Malgun Gothic"),FontSize=14};
        var panel=new StackPanel{Margin=new Thickness(28)};
        var badge=new Border{Width=60,Height=60,CornerRadius=new CornerRadius(30),Background=new SolidColorBrush(Color.FromRgb(226,246,237)),HorizontalAlignment=HorizontalAlignment.Left,Child=new TextBlock{Text="✓",FontSize=34,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(29,139,91)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        panel.Children.Add(badge);
        panel.Children.Add(new TextBlock{Text="블러 영상이 완성됐어요",FontSize=24,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(28,39,54)),Margin=new Thickness(0,16,0,8),TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="저장과 파일 검증을 마쳤습니다.",Foreground=Brushes.SlateGray,Margin=new Thickness(0,0,0,20)});
        var info=new StackPanel();
        info.Children.Add(new TextBlock{Text=Path.GetFileName(path),FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(40,52,70)),Margin=new Thickness(0,0,0,10)});
        info.Children.Add(new TextBlock{Text="저장 위치",FontSize=12,Foreground=Brushes.SlateGray,Margin=new Thickness(0,0,0,4)});
        info.Children.Add(new TextBox{Text=Path.GetDirectoryName(path),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Foreground=new SolidColorBrush(Color.FromRgb(70,80,95)),Padding=new Thickness(0)});
        panel.Children.Add(new Border{Background=new SolidColorBrush(Color.FromRgb(246,248,251)),CornerRadius=new CornerRadius(10),Padding=new Thickness(16),Child=info});
        var buttons=new Grid{Margin=new Thickness(0,22,0,0)};buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});buttons.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(120)});
        var folder=Action("저장 폴더 열기",delegate{try{Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+path+"\""){UseShellExecute=true});dialog.Close();}catch(Exception ex){MessageBox.Show(dialog,"폴더를 열지 못했습니다.\n"+ex.Message,"저장 폴더");}});
        var done=Action("확인",delegate{dialog.Close();});done.IsDefault=true;done.IsCancel=true;done.Background=new SolidColorBrush(Color.FromRgb(224,99,40));done.Foreground=Brushes.White;done.BorderBrush=done.Background;done.FontWeight=FontWeights.Bold;
        buttons.Children.Add(folder);Grid.SetColumn(done,2);buttons.Children.Add(done);panel.Children.Add(buttons);
        dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        dialog.ContentRendered+=delegate{dialog.Activate();done.Focus();};
        dialog.ShowDialog();
    }
    int[] MapRegion(int i){double vw=preview.NaturalVideoWidth,vh=preview.NaturalVideoHeight,scale=Math.Min(overlay.ActualWidth/vw,overlay.ActualHeight/vh);if(scale<=0)return new int[4];double ox=(overlay.ActualWidth-vw*scale)/2,oy=(overlay.ActualHeight-vh*scale)/2;int x=EvenCoord(Math.Min(vw,Math.Max(0,(rx[i]-ox)/scale))),y=EvenCoord(Math.Min(vh,Math.Max(0,(ry[i]-oy)/scale))),right=EvenCoord(Math.Min(vw,Math.Max(0,(rx[i]+rw[i]-ox)/scale))),bottom=EvenCoord(Math.Min(vh,Math.Max(0,(ry[i]+rh[i]-oy)/scale)));return new[]{x,y,Math.Max(0,right-x),Math.Max(0,bottom-y)};}
    int Even(double n){int v=(int)Math.Round(n);return Math.Max(2,v-v%2);}
    int EvenCoord(double n){int v=(int)Math.Round(n);return Math.Max(0,v-v%2);}
    string FindFfmpeg(){return VideoEngine.Find("ffmpeg.exe");}
}
}
