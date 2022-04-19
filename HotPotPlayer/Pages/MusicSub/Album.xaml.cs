﻿using HotPotPlayer.Extensions;
using HotPotPlayer.Models;
using HotPotPlayer.Pages.Helper;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HotPotPlayer.Pages.MusicSub
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Album : Page, INotifyPropertyChanged
    {
        public Album()
        {
            this.InitializeComponent();
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

        private AlbumItem _selectedAlbum;
        public AlbumItem SelectedAlbum
        {
            get => _selectedAlbum;
            set => Set(ref _selectedAlbum, value);
        }
        MenuFlyout _albumAddFlyout;
        MenuFlyout AlbumAddFlyout
        {
            get => _albumAddFlyout;
            set => Set(ref _albumAddFlyout, value);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            SelectedAlbum = e.Parameter switch
            {
                MusicItem music => await GetAlbumAsync(music),
                AlbumItem album => album,
                _ => throw new NotImplementedException()
            };
            AlbumAddFlyout = InitAlbumAddFlyout();
            base.OnNavigatedTo(e);
        }

        static async Task<AlbumItem> GetAlbumAsync(MusicItem m)
        {
            var album = await Task.Run(() =>
            {
                var musicService = ((App)Application.Current).LocalMusicService;
                var album = musicService.QueryAlbum(m);
                return album;
            });
            return album;
        }

        private void AlbumPopupListClick(object sender, RoutedEventArgs e)
        {
            var music = ((Button)sender).Tag as MusicItem;
            var player = ((App)Application.Current).MusicPlayer;
            player.PlayNext(music, SelectedAlbum);
        }

        MenuFlyout InitAlbumAddFlyout()
        {
            var flyout = new MenuFlyout();
            var i1 = new MenuFlyoutItem
            {
                Text = "当前列表"
            };
            i1.Click += (s, a) => AlbumHelper.AlbumAddOne(SelectedAlbum);
            flyout.Items.Add(i1);
            if(((App)Application.Current).LocalMusicService.LocalPlayListList.Count > 0)
            {
                var i2 = new MenuFlyoutSeparator();
                flyout.Items.Add(i2);
                foreach (var item in ((App)Application.Current).LocalMusicService.LocalPlayListList)
                {
                    var i = new MenuFlyoutItem
                    {
                        Text = item.Title,
                        Tag = item
                    };
                    i.Click += (s, a) => AlbumHelper.AlbumAddToPlayList(item.Title, SelectedAlbum);
                    flyout.Items.Add(i);
                }
            }
            return flyout;
        }
    }
}
