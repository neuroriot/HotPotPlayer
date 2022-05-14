﻿using HotPotPlayer.Interop;
using HotPotPlayer.Models;
using HotPotPlayer.Models.CloudMusic;
using HotPotPlayer.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HotPotPlayer.Controls
{
    public sealed partial class PlayScreen : UserControl, INotifyPropertyChanged
    {
        public PlayScreen()
        {
            this.InitializeComponent();
            MusicPlayer.PropertyChanged += MusicPlayer_PropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Set<T>(ref T oldValue, T newValue, [CallerMemberName] string propertyName = "")
        {
            if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
            {
                oldValue = newValue;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        bool _pendingChange = true;
        private async void MusicPlayer_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            MusicPlayer m = (MusicPlayer)sender;
            if (m.CurrentPlaying != null && m.CurrentPlaying is CloudMusicItem c)
            {
                if (e.PropertyName == "IsPlayScreenVisible" && m.IsPlayScreenVisible)
                {
                    if (_pendingChange)
                    {
                        Comments ??= new ObservableCollection<CloudCommentItem>();
                        Comments.Clear();
                        var l = await CloudMusicService.GetSongCommentAsync(c.SId);
                        foreach (var item in l)
                        {
                            Comments.Add(item);
                        }
                        _pendingChange = false;
                    }
                }
                else if (e.PropertyName == "CurrentPlaying")
                {
                    if (m.IsPlayScreenVisible)
                    {
                        Comments.Clear();
                        var l = await CloudMusicService.GetSongCommentAsync(c.SId);
                        foreach (var item in l)
                        {
                            Comments.Add(item);
                        }
                    }
                    else
                    {
                        _pendingChange = true;
                    }
                }
            }
        }

        private ObservableCollection<CloudCommentItem> _comments;
        public ObservableCollection<CloudCommentItem> Comments
        {
            get => _comments;
            set => Set(ref _comments, value);
        }

        NetEaseMusicService CloudMusicService => ((App)Application.Current).NetEaseMusicService;
        MusicPlayer MusicPlayer => ((App)Application.Current).MusicPlayer;
        App App => (App)Application.Current;

        private void PlayScreen_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint currentPoint = e.GetCurrentPoint(Root);
            if (currentPoint.PointerDeviceType == PointerDeviceType.Mouse)
            {
                PointerPointProperties pointerProperties = currentPoint.Properties;
                if (pointerProperties.PointerUpdateKind == PointerUpdateKind.XButton1Released)
                {
                    MusicPlayer.HidePlayScreen();
                }
            }
            e.Handled = true;
        }

        public string GetLikeButtonGlyph(MusicItem m)
        {
            if (m is CloudMusicItem c)
            {
                return CloudMusicService.GetSongLiked(c) ? "\uEB52" : "\uEB51";
            }
            return "\uEB51";
        }

        public SolidColorBrush GetLikeButtonForeground(MusicItem m)
        {
            if (m is CloudMusicItem c)
            {
                return CloudMusicService.GetSongLiked(c) ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.Black);
            }
            return new SolidColorBrush(Colors.Black);
        }

        public string GetAlias(MusicItem m)
        {
            if (m is CloudMusicItem c)
            {
                return c.Alias;
            }
            return string.Empty;
        }

        public Visibility GetAliasVisible(MusicItem m)
        {
            if (m is CloudMusicItem c && !string.IsNullOrEmpty(c.Alias))
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            DirectoryInfo path = MusicPlayer.CurrentPlaying?.Source?.Directory;
            if (path != null)
            {
                await Launcher.LaunchFolderPathAsync(path.FullName);
            }
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            var hWnd = App.MainWindowHandle;

            IDataTransferManagerInterop interop = DataTransferManager.As<IDataTransferManagerInterop>();

            IntPtr result = interop.GetForWindow(hWnd, DataTransferManagerInteropConstants._dtm_iid);
            var dataTransferManager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(result);

            dataTransferManager.DataRequested += (sender, args) =>
            {
                args.Request.Data.Properties.Title = "分享音乐";
                args.Request.Data.SetText(MusicPlayer.CurrentPlaying.ToString());
                args.Request.Data.RequestedOperation = DataPackageOperation.Copy;
            };

            // Show the Share UI
            interop.ShowShareUIForWindow(hWnd);
        }
    }
}
