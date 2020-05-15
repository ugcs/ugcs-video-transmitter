﻿using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Unosquare.FFME.Common;

namespace VideoTransmitter.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : Window
    {
        public MainView()
        {
            InitializeComponent();
            InitializeMediaEvents();
        }

        private void Media_MediaOpening(object sender, Unosquare.FFME.Common.MediaOpeningEventArgs e)
        {
            e.Options.MinimumPlaybackBufferPercent = 0;
        }

        private void Window_Initialized(object sender, EventArgs e)
        {

        }

        private void InitializeMediaEvents()
        {
            Media.MediaInitializing += OnMediaInitializing;
            Media.MediaOpening += OnMediaOpening;
            Media.MediaOpened += OnMediaOpened;
        }
        private void OnMediaOpening(object sender, MediaOpeningEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                LoadingVideo.Visibility = Visibility.Visible;
            });
        }
        private void OnMediaOpened(object sender, MediaOpenedEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                LoadingVideo.Visibility = Visibility.Hidden;
            });
        }
        private void OnMediaInitializing(object sender, MediaInitializingEventArgs e)
        {
            Execute.OnUIThreadAsync(() =>
            {
                LoadingVideo.Visibility = Visibility.Visible;
            });
        }
    }
}
