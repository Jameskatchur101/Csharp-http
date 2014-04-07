﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EvHttpSharp.Interop;

namespace Tests
{
    public class TestBase
    {
        static readonly object SyncRoot = new object();
        private static bool _initialized;
        public TestBase()
        {
            lock (SyncRoot)
            {
                if(_initialized)
                    return;
                if (Path.DirectorySeparatorChar == '\\')
                    Event.Init(Directory.GetCurrentDirectory());
                else
                    Event.Init();
                _initialized = true;
            }
            
        }
    }
}
