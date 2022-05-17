﻿using HotPotPlayer.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HotPotPlayer.Pages.CloudMusicSub
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Login : Page
    {
        public Login()
        {
            this.InitializeComponent();
            _ui = DispatcherQueue.GetForCurrentThread();
        }
        NetEaseMusicService CloudMusicService => ((App)Application.Current).NetEaseMusicService;
        MainWindow MainWindow => ((App)Application.Current).MainWindow;

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ui = DispatcherQueue.GetForCurrentThread();
            await SetQRCodeAndWait();
            MainWindow.NavigateTo("CloudMusic");
        }

        string qrKey;
        DispatcherQueue _ui;

        private async Task SetQRCodeAndWait()
        {
            qrKey = await CloudMusicService.GetQrKeyAsync();
            var qrData = CloudMusicService.GetQrImgByte(qrKey);
            BitmapImage image = new();
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(qrData.AsBuffer());
            stream.Seek(0);
            await image.SetSourceAsync(stream);
            QR.Source = image;
            await Task.Run(CheckLogin);
        }

        public async Task CheckLogin()
        {
            while (true)
            {
                var (code, message) = await CloudMusicService.GetQrCheckAsync(qrKey);
                _ui.TryEnqueue(() =>
                {
                    Status.Text = message;
                });
                if (code == 803)
                {
                    break;
                }
                await Task.Delay(1000);
            }
        }
    }
}
