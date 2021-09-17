﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace KeqingNiuza.Launcher
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            Initialized += Window_Initialized;
            Loaded += Window_Loaded;
            InitializeComponent();
            var name = typeof(MainWindow).Assembly.Location;
            var v = FileVersionInfo.GetVersionInfo(name);
            VersionText.Text = v.FileVersion;
        }


        private Downloader _downloader;

        private bool _canceled;


        private string _InfoTest;
        public string InfoTest
        {
            get { return _InfoTest; }
            set
            {
                _InfoTest = value;
                OnPropertyChanged();
            }
        }


        private string _ProgressTest;
        public string ProgressTest
        {
            get { return _ProgressTest; }
            set
            {
                _ProgressTest = value;
                OnPropertyChanged();
            }
        }


        private string _SpeedTest;
        public string SpeedTest
        {
            get { return _SpeedTest; }
            set
            {
                _SpeedTest = value;
                OnPropertyChanged();
            }
        }


        private bool _CanCancel = true;
        public bool CanCancel
        {
            get { return _CanCancel; }
            set
            {
                _CanCancel = value;
                OnPropertyChanged();
            }
        }



        private void Window_Initialized(object sender, EventArgs e)
        {
            BitmapImage bitmap = null;
            try
            {
                string[] files = Directory.GetFiles(".\\splash");
                if (files.Any())
                {
                    Random random = new Random((int)DateTime.Now.Ticks);
                    string file = files[random.Next(files.Length)];
                    using (var s = File.OpenRead(file))
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = null;
                        bitmap.StreamSource = s;
                        bitmap.EndInit();
                    }
                }
                else
                {
                    bitmap = new BitmapImage(new Uri("./Moon over Monstadt.jpg", UriKind.Relative));
                }
            }
            catch (Exception)
            {
                bitmap = new BitmapImage(new Uri("./Moon over Monstadt.jpg", UriKind.Relative));
            }
            finally
            {
                SplashImage.Source = bitmap;
            }
        }


        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Button_WindowMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Button_WindowClose_Click(object sender, RoutedEventArgs e)
        {
            _downloader?.Cancel();
            if (Directory.Exists(".\\deleting"))
            {
                var files = Directory.GetFiles(".\\deleting");
                foreach (var file in files)
                {
                    var dest = file.Replace("\\deleting", "");
                    if (!File.Exists(dest))
                    {
                        File.Move(file, dest);
                    }
                }
            }
            Close();
        }

        private void Button_Skip_Click(object sender, RoutedEventArgs e)
        {
            if (CanCancel)
            {
                _canceled = true;
                Process.Start(".\\bin\\KeqingNiuza.exe");
                Environment.Exit(0);
            }
        }


        private async void Window_Loaded(object sender, RoutedEventArgs _)
        {
            await UpdateTask();
        }


        private async Task UpdateTask()
        {
            InfoTest = "正在检查更新";
            await Task.Delay(100);
            var list = await TestUpdate();
            if (_canceled)
            {
                return;
            }
            if ((bool)list?.Any())
            {
                CanCancel = false;
                await DownladFiles(list);
            }
            Process.Start(".\\bin\\KeqingNiuza.exe");
            Environment.Exit(0);
        }


        private async Task<List<KeqingNiuzaFileInfo>> TestUpdate()
        {
            var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync("https://xw6dp97kei-1306705684.file.myqcloud.com/keqingniuza/meta/version");
            var fs = Directory.GetFiles(".\\", "*", SearchOption.AllDirectories);
            var files = fs.Select(x => new KeqingNiuzaFileInfo(x)).ToList();
            var ms = new MemoryStream();
            using (var dcs = new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress))
            {
                await dcs.CopyToAsync(ms);
            }
            var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            var versionInfo = JsonConvert.DeserializeObject<VersionInfo>(json);
            if (versionInfo.Version != VersionText.Text)
            {
                VersionText.Text += $" -> {versionInfo.Version}";
            }
            versionInfo.KeqingNiuzaFiles.ForEach(x => x.Path = Path.GetFullPath(x.Path));
            return versionInfo.KeqingNiuzaFiles.Except(files).ToList();
        }



        private async Task DownladFiles(List<KeqingNiuzaFileInfo> list)
        {
            _progressLoading.SetAnimationState(AnimationState.IndicatorAppear);
            _progressLoading.SetAnimationState(AnimationState.ProgressExpand);
            _progressLoading.SetAnimationState(AnimationState.ProgressShow);
            await Task.Delay(800);
            var baseDir = Path.GetDirectoryName(AppContext.BaseDirectory);
            var rootFiles = list.Where(x => Path.GetDirectoryName(x.Path) == baseDir);
            if ((bool)rootFiles?.Any())
            {

                if (Directory.Exists(".\\deleting"))
                {
                    Directory.Delete(".\\deleting", true);
                }
                Directory.CreateDirectory(".\\deleting");
                foreach (var item in rootFiles)
                {
                    if (File.Exists(item.Path))
                    {
                        File.Move(item.Path, $".\\deleting\\{item.Name}");
                    }
                }
            }
            _downloader = new Downloader();
            _downloader.ProgressChanged += (s, e) =>
            {
                _progressLoading.ProgressValue = (float)e.DownloadedSize / e.TotalSize;
                InfoTest = "正在下载文件";
                ProgressTest = $"{(float)e.DownloadedSize / e.TotalSize:P2}";
                SpeedTest = $"{LengthToString(e.DownloadedSize, e.TotalSize)}   {e.Speed / 1024} KB/s";
            };
            _downloader.DownloadFinished += (s, e) => { InfoTest = "下载完成"; ProgressTest = ""; SpeedTest = ""; };
            await _downloader.DownloadAsync(list);
            await Task.Delay(300);
            _progressLoading.SetAnimationState(AnimationState.ProgressClose);
            _progressLoading.SetAnimationState(AnimationState.IndicatorDisappear);
            await Task.Delay(800);
        }


        private string LengthToString(long current, long total)
        {
            if (total <= 2 << 20)
            {
                return $"{(double)current / (2 << 10):F2}/{(double)total / (2 << 10):F2} KB";
            }
            else
            {
                return $"{(double)current / (2 << 20):F2}/{(double)total / (2 << 20):F2} MB";
            }
        }

    }
}
