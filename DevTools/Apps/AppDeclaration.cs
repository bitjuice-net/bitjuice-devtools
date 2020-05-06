﻿using System.Collections.Generic;

namespace DevTools.Apps
{
    public class AppDeclaration
    {
        public string Description { get; set; }
        public string Path { get; set; }
        public string Selected { get; set; }
        public bool Disabled { get; set; }
        public Dictionary<string, AppVariant> Variants { get; set; }
    }
}