﻿using Core.Utils;
using System;

namespace TcpMobile.Models
{
    public enum MenuItemType
    {
        Default,
        CreateGame,
        JoinGame,
        SingleGame,
        EndGame,
        Debug,
        Settings,
        ShareApp,
        Contribute,
        About
    }

    public class SideBarMenuItem
    {
        public MenuItemType Type { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public bool Divider { get; set; } = false;
    }
}
