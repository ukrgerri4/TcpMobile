﻿using Core.Utils;
using Rg.Plugins.Popup.Pages;
using System;
using Xamarin.Forms.Xaml;

namespace MunchkinCounterLan.Views.Popups
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DicePage : PopupPage
    {
        private static readonly string[] _dice = {
            FontAwesomeIcons.DiceOne,
            FontAwesomeIcons.DiceTwo,
            FontAwesomeIcons.DiceThree,
            FontAwesomeIcons.DiceFour,
            FontAwesomeIcons.DiceFive,
            FontAwesomeIcons.DiceSix
        };
        public string DiceValue => _dice[new Random().Next(0, 6)];
        public DicePage()
        {
            InitializeComponent();

            BindingContext = this;
        }

        private void Throw(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(DiceValue));
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            OnPropertyChanged(nameof(DiceValue));
        }
    }
}