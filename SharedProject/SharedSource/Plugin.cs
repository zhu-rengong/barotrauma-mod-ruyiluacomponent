using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Barotrauma;
using Barotrauma.Networking;
using HarmonyLib;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Compatibility;
using MoonSharp.Interpreter.Interop;

#if CLIENT
[assembly: IgnoresAccessChecksTo("Barotrauma")]
#endif
#if SERVER
[assembly: IgnoresAccessChecksTo("DedicatedServer")]
#endif
[assembly: IgnoresAccessChecksTo("BarotraumaCore")]
[assembly: IgnoresAccessChecksTo("MoonSharp.Interpreter")]

namespace LuaForBarotraumaModdingId_4Rcf1ZQPqrsB6aA7O
{
    public partial class Plugin : IAssemblyPlugin
    {
        public void Initialize()
        {

        }

        public void OnLoadCompleted()
        {

        }

        public void PreInitPatching()
        {
            // Not yet supported: Called during the Barotrauma startup phase before vanilla content is loaded.
        }

        public void Dispose()
        {

        }
    }
}
