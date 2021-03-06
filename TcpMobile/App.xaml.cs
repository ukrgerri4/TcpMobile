﻿using Infrastracture.Definitions;
using Infrastracture.Interfaces;
using System;
using TcpMobile.Views;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace TcpMobile
{
    public partial class App : Application
    {
        private IGameLogger _gameLogger => DependencyService.Get<IGameLogger>();
        private IBrightnessService _brightnessService => DependencyService.Get<IBrightnessService>();

        public App()
        {
            InitializeComponent();

            VersionTracking.Track();

            MainPage = new MainMDPage();

            AdjustBrightness();
        }

        private void AdjustBrightness()
        {
            if (Preferences.Get(PreferencesKey.KeepScreenOn, true))
            {
                Preferences.Set(PreferencesKey.KeepScreenOn, true);
                _brightnessService.KeepScreenOn();
            }
        }

        protected override void OnStart()
        {
            if (Preferences.Get(PreferencesKey.KeepScreenOn, false))
            {
                _brightnessService.KeepScreenOn();
            }

            _gameLogger.Debug("Game started");
        }

        protected override void OnSleep()
        {
            _gameLogger.Debug("Game go sleep");
        }

        protected override void OnResume()
        {
            _gameLogger.Debug("Game resumed");
        }
    }
}
