using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace AdkillerReaders {
public static class VideoEngine {
    public static string Find(string name) {
        string home=AppDomain.CurrentDomain.BaseDirectory;
        foreach(string folder in new[]{Path.Combine(home,"tools"),home,Path.GetFullPath(Path.Combine(home,"..","Adkiller Media Player","tools"))}) {
            string path=Path.Combine(folder,name);if(File.Exists(path))return path;
        }
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';'))try{string path=Path.Combine(folder.Trim('"'),name);if(File.Exists(path))return path;}catch{}
        try{string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","WinGet","Packages");if(Directory.Exists(root))return Directory.GetFiles(root,name,SearchOption.AllDirectories).FirstOrDefault();}catch{}
        return null;
    }
}
public class PreviewErrorEventArgs : EventArgs {public Exception ErrorException;public PreviewErrorEventArgs(Exception error){ErrorException=error;}}
// Decode pixels locally: neither Media Foundation codecs nor a full-length proxy are required.
public class VideoPreview : Image {
    public event EventHandler MediaOpened;
    public event EventHandler<PreviewErrorEventArgs> MediaFailed;
    public int NaturalVideoWidth {get;private set;}
    public int NaturalVideoHeight {get;private set;}
    public Duration NaturalDuration {get;private set;}
    public bool IsPlaying {get;private set;}
    public int MaxPreviewWidth=960,MaxPreviewHeight=540;
    string file;TimeSpan position;int generation;CancellationTokenSource work;
    public VideoPreview(){Stretch=Stretch.Uniform;NaturalDuration=Duration.Automatic;}
    public TimeSpan Position {get{return position;}set{position=TimeSpan.FromSeconds(Math.Max(0,Math.Min(value.TotalSeconds,NaturalDuration.HasTimeSpan?Math.Max(0,NaturalDuration.TimeSpan.TotalSeconds-0.05):value.TotalSeconds)));if(NaturalVideoWidth>0)Decode(IsPlaying,false);}}
    void Cancel(){generation++;if(work!=null){work.Cancel();work=null;}}
    public void Close(){Cancel();IsPlaying=false;file=null;base.Source=null;NaturalVideoWidth=NaturalVideoHeight=0;NaturalDuration=Duration.Automatic;position=TimeSpan.Zero;}
    public void Pause(){IsPlaying=false;Cancel();}
    public void Play(){if(NaturalVideoWidth==0)return;if(NaturalDuration.HasTimeSpan&&position.TotalSeconds>=NaturalDuration.TimeSpan.TotalSeconds-0.1)position=TimeSpan.Zero;Decode(true,false);}
    static string Q(string text){return "\""+text+"\"";}
    static double Number(string text,double fallback){double value;return Double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!Double.IsNaN(value)&&!Double.IsInfinity(value)?value:fallback;}
    void Fail(Exception error,int id){Dispatcher.BeginInvoke((Action)delegate{if(id!=generation)return;IsPlaying=false;if(MediaFailed!=null)MediaFailed(this,new PreviewErrorEventArgs(error));});}
    public void Open(string path){
        Close();file=path;work=new CancellationTokenSource();work.CancelAfter(30000);var token=work.Token;int id=generation;
        Task.Factory.StartNew(delegate{
            try{
                string probe=VideoEngine.Find("ffprobe.exe");if(probe==null)throw new FileNotFoundException("영상 처리 엔진이 없습니다. 프로그램의 tools 폴더를 복원해 주세요.");
                string xml;
                using(var p=new Process{StartInfo=new ProcessStartInfo(probe,"-v error -select_streams v:0 -show_streams -show_format -of xml "+Q(path)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
                    p.Start();using(token.Register(delegate{try{p.Kill();}catch{}})){
                        var errors=p.StandardError.ReadToEndAsync();xml=p.StandardOutput.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new IOException("영상 정보를 읽을 수 없습니다. "+errors.Result);
                    }
                }
                token.ThrowIfCancellationRequested();var doc=XDocument.Parse(xml);var stream=doc.Descendants("stream").FirstOrDefault();if(stream==null)throw new IOException("이 파일에 편집 가능한 영상이 없습니다.");
                double width=Number((string)stream.Attribute("width"),0),height=Number((string)stream.Attribute("height"),0);
                string[] sar=((string)stream.Attribute("sample_aspect_ratio")??"1:1").Split(':');if(sar.Length==2)width*=Number(sar[0],1)/Math.Max(1,Number(sar[1],1));
                double rotation=0;foreach(var side in stream.Descendants("side_data"))rotation=Number((string)side.Attribute("rotation"),rotation);
                if(Math.Abs(rotation)%180>45&&Math.Abs(rotation)%180<135){double temp=width;width=height;height=temp;}
                var format=doc.Descendants("format").FirstOrDefault();double duration=Number((string)stream.Attribute("duration"),format==null?0:Number((string)format.Attribute("duration"),0));
                if(width<2||height<2||width>32768||height>32768||duration<=0)throw new IOException("영상 크기 또는 재생 시간을 읽지 못했습니다. 파일이 손상되었거나 지원되지 않는 형식일 수 있습니다.");
                Dispatcher.BeginInvoke((Action)delegate{if(id!=generation)return;NaturalVideoWidth=((int)Math.Round(width)+1)/2*2;NaturalVideoHeight=((int)Math.Round(height)+1)/2*2;NaturalDuration=new Duration(TimeSpan.FromSeconds(duration));Decode(false,true);});
            }catch(Exception error){Fail(error,id);}
        });
    }
    void Decode(bool play,bool opening){
        Cancel();IsPlaying=play;work=new CancellationTokenSource();work.CancelAfter(45000);var watchdog=work;var token=work.Token;int id=generation;
        string input=file;double start=position.TotalSeconds,duration=NaturalDuration.TimeSpan.TotalSeconds;
        double scale=Math.Min(1,Math.Min((double)MaxPreviewWidth/NaturalVideoWidth,(double)MaxPreviewHeight/NaturalVideoHeight));int width=Math.Max(2,(int)(NaturalVideoWidth*scale)/2*2),height=Math.Max(2,(int)(NaturalVideoHeight*scale)/2*2);
        Task.Factory.StartNew(delegate{
            try{
                string ff=VideoEngine.Find("ffmpeg.exe");if(ff==null)throw new FileNotFoundException("영상 처리 엔진을 찾지 못했습니다.");
                string args="-hide_banner -loglevel error -nostdin -threads 2 -ss "+start.ToString("0.######",CultureInfo.InvariantCulture)+" -i "+Q(input)+" -map 0:v:0 -an -sn -dn -vf \"fps=12,scale="+width+":"+height+",setsar=1\" "+(play?"":"-frames:v 1 ")+"-threads 2 -pix_fmt bgr24 -f rawvideo pipe:1";
                using(var p=new Process{StartInfo=new ProcessStartInfo(ff,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
                    p.Start();using(token.Register(delegate{try{p.Kill();}catch{}})){
                        var errors=p.StandardError.ReadToEndAsync();byte[] buffer=new byte[width*height*3];int count=0;var clock=new Stopwatch();
                        while(true){
                            token.ThrowIfCancellationRequested();int offset=0;while(offset<buffer.Length){int read=p.StandardOutput.BaseStream.Read(buffer,offset,buffer.Length-offset);if(read==0)break;offset+=read;}
                            token.ThrowIfCancellationRequested();if(offset!=buffer.Length)break;watchdog.CancelAfter(45000);
                            if(!clock.IsRunning)clock.Start();int wait=(int)(count*1000.0/12-clock.Elapsed.TotalMilliseconds);if(play&&wait>0&&token.WaitHandle.WaitOne(wait))token.ThrowIfCancellationRequested();
                            var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgr24,null,buffer,width*3);bitmap.Freeze();double timestamp=Math.Min(duration,start+count/12.0);bool first=count==0;
                            Dispatcher.Invoke((Action)delegate{if(id!=generation)return;base.Source=bitmap;position=TimeSpan.FromSeconds(timestamp);if(opening&&first&&MediaOpened!=null)MediaOpened(this,EventArgs.Empty);});
                            count++;if(!play)break;
                        }
                        p.WaitForExit();token.ThrowIfCancellationRequested();if(p.ExitCode!=0||count==0)throw new IOException("영상 화면을 해석하지 못했습니다. "+errors.Result);
                        Dispatcher.BeginInvoke((Action)delegate{if(id==generation)IsPlaying=false;});
                    }
                }
            }catch(Exception error){Fail(error,id);}
        });
    }
}
}

namespace AdkillerReaders {
// One cancellable decoder per hover card; moving the pointer never queues a video per pixel.
public sealed class TimelinePreview {
    readonly System.Windows.Controls.Slider bar;
    readonly System.Func<string> path;
    readonly System.Func<double> duration;
    readonly System.Func<int> rotation;
    readonly VideoPreview image=new VideoPreview{Width=288,Height=162};
    readonly System.Windows.Controls.TextBlock label=new System.Windows.Controls.TextBlock{Foreground=System.Windows.Media.Brushes.White,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new System.Windows.Thickness(4)};
    readonly System.Windows.Controls.Primitives.Popup popup=new System.Windows.Controls.Primitives.Popup{Placement=System.Windows.Controls.Primitives.PlacementMode.Custom,AllowsTransparency=true,StaysOpen=true,IsHitTestVisible=false};
    readonly System.Windows.Threading.DispatcherTimer delay=new System.Windows.Threading.DispatcherTimer{Interval=System.TimeSpan.FromMilliseconds(250)};
    string opened; double target; bool ready;
    public TimelinePreview(System.Windows.Window owner,System.Windows.Controls.Slider slider,System.Func<string> file,System.Func<double> length,System.Func<int> angle){
        bar=slider;path=file;duration=length;rotation=angle;image.MaxPreviewWidth=288;image.MaxPreviewHeight=162;
        var stack=new System.Windows.Controls.StackPanel();stack.Children.Add(image);stack.Children.Add(label);
        popup.Child=new System.Windows.Controls.Border{Background=System.Windows.Media.Brushes.Black,CornerRadius=new System.Windows.CornerRadius(7),Padding=new System.Windows.Thickness(6),Child=stack};popup.PlacementTarget=bar;
        popup.CustomPopupPlacementCallback=delegate(System.Windows.Size size,System.Windows.Size targetSize,System.Windows.Point offset){return new[]{new System.Windows.Controls.Primitives.CustomPopupPlacement(new System.Windows.Point(-size.Width/2,-size.Height-10),System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)};};
        bar.MouseMove+=delegate(object sender,System.Windows.Input.MouseEventArgs e){if(string.IsNullOrEmpty(path())||duration()<=0)return;target=System.Math.Min(System.Math.Max(0,duration()-0.1),duration()*System.Math.Max(0,System.Math.Min(1,e.GetPosition(bar).X/System.Math.Max(1,bar.ActualWidth))));label.Text=System.TimeSpan.FromSeconds(target).ToString(@"hh\:mm\:ss");popup.HorizontalOffset=e.GetPosition(bar).X;popup.VerticalOffset=e.GetPosition(bar).Y;popup.IsOpen=true;delay.Stop();delay.Start();};
        bar.MouseLeave+=delegate{Close();};owner.Deactivated+=delegate{Close();};
        image.MediaOpened+=delegate{ready=true;image.Position=System.TimeSpan.FromSeconds(target);};
        image.MediaFailed+=delegate{label.Text="미리보기를 읽을 수 없습니다";};
        delay.Tick+=delegate{delay.Stop();if(!popup.IsOpen)return;image.LayoutTransform=new System.Windows.Media.RotateTransform(rotation());if(opened!=path()){opened=path();ready=false;image.Open(opened);}else if(ready)image.Position=System.TimeSpan.FromSeconds(target);};
    }
    public void Close(){delay.Stop();popup.IsOpen=false;image.Close();opened=null;ready=false;}
}
}
