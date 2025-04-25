// Many code snippets taken from MicroLua (https://steamcommunity.com/sharedfiles/filedetails/?id=3018125421) by Matheus

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Items.Components;
using HarmonyLib;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Compatibility;
using MoonSharp.Interpreter.Interop;
using Mono.Cecil;
using MoonSharp.Interpreter.Debugging;

namespace LuaForBarotraumaModdingId_4Rcf1ZQPqrsB6aA7O.Items.Components
{
    partial class RuyiLuaComponent : ItemComponent, IClientSerializable, IServerSerializable
    {
#if SERVER
        public const bool IsServer = true;
        public const bool IsClient = false;
#else
        public const bool IsServer = false;
        public const bool IsClient = true;
#endif

        private const string LOCAL_NAME_IS_CLIENT = "isClient";
        private const string LOCAL_NAME_IS_SERVER = "isServer";
        private const string LOCAL_NAME_IS_SINGLEPLAYER = "isSingleplayer";
        private const string LOCAL_NAME_IS_MULTIPLAYER = "isMultiplayer";
        private const string LOCAL_NAME_GET_TOTAL_TIME = "GetTotalTime";
        private const string LOCAL_NAME_RUYI_LUA = "ruyiLua";
        private const string LOCAL_NAME_OUT = "out";
        private const string LOCAL_NAME_CLEAR = nameof(clear);
        private const string LOCAL_NAME_READ_MEMORY = nameof(readMemory);
        private const string LOCAL_NAME_WRITE_MEMORY = nameof(writeMemory);
        private const string LOCAL_NAME_NET_SEND = nameof(netSend);
        private const string UPVALUE_NAME_ON_ITEM_LOADED = nameof(onItemLoaded);
        private const string UPVALUE_NAME_ON_MAP_LOADED = nameof(onMapLoaded);
        private const string UPVALUE_NAME_ON_SAVE = nameof(onSave);
        private const string UPVALUE_NAME_UPDATE = nameof(update);
        private const string UPVALUE_NAME_INP = nameof(inp);
        private const string UPVALUE_NAME_OUT_SENDER = nameof(outSender);
        private const string UPVALUE_NAME_INP_SENDERS = nameof(inpSenders);
        private const string UPVALUE_NAME_NET_RECEIVE = nameof(netReceive);
        private readonly static Regex inpRegex = new Regex(@"^signal_in(\d+)$");
        private readonly static Regex outRegex = new Regex(@"^signal_out(\d+)$");

        private Dictionary<int, Connection> outPinMappingConnection = new();
        private Dictionary<Connection, int> inpConnectionMappingPin = new();

        private string chunk = string.Empty;
        private string memory = string.Empty;
        private Script? script;
        private DynValue onItemLoaded = DynValue.Nil;
        private DynValue onMapLoaded = DynValue.Nil;
        private DynValue onSave = DynValue.Nil;
        private DynValue update = DynValue.Nil;
        private DynValue inp = DynValue.Nil;
        private DynValue outSender = DynValue.Nil;
        private DynValue inpSenders = DynValue.Nil;
        private DynValue netReceive = DynValue.Nil;

        private void HandleException(string source, InterpreterException e, bool terminate = false)
        {
            void LogError(string? location = null, string? lines = null)
            {
                string message = location is null
                    ? $"[{Item}] [{source}] {e.Message}"
                    : lines is null
                        ? $"[{Item}] [{source}] {e.Message}\n{new string(' ', 4)}at {location}"
                        : $"[{Item}] [{source}] {e.Message}\n{new string(' ', 4)}at {location}, source:\n{lines}";

                LuaCsLogger.LogError(message, LuaCsMessageOrigin.LuaMod);
            }

            if (script is not null
                && Traverse.Create(script).Field("m_MainProcessor").GetValue<MoonSharp.Interpreter.Execution.VM.Processor>() is var scriptMainProcessor
                && scriptMainProcessor is not null
                && scriptMainProcessor.GetCurrentSourceRef(e.InstructionPtr) is var sref
                && sref is not null)
            {
                int chunkRelativeToCodeStartLineOffset = 21;
                int chunkRelativeToCodeEndLineOffset = 12;
                string? location;
                string? errorSource;
                SourceCode sc = script.GetSourceCode(sref.SourceIdx);

                string GetLinesToPrint(int fromLine, int toLine, int extendedLines = 5)
                {
                    int extendedForwardLines = extendedLines / 2;
                    int extendedBackwardLines = extendedLines - extendedForwardLines;
                    int discardLines = Math.Max(1 + chunkRelativeToCodeStartLineOffset - (sref.FromLine - extendedBackwardLines), 0);
                    extendedBackwardLines -= discardLines;
                    extendedForwardLines += discardLines;
                    int startLine = sref.FromLine - extendedBackwardLines;
                    int endLine = Math.Min(sref.ToLine + extendedForwardLines, (sc.Lines.Length - 1) - chunkRelativeToCodeEndLineOffset); // Line 0 of sc.Lines is an auto-generated comment, so we need to account for the extra line in sc.Lines.
                    int lineColumnWidth = Math.Max(
                        (startLine - chunkRelativeToCodeStartLineOffset).ToString().Length,
                        (endLine - chunkRelativeToCodeStartLineOffset).ToString().Length);
                    return string.Join(
                        '\n',
                        Enumerable.Range(startLine, endLine - startLine + 1)
                            .Select(line => $"{(line - chunkRelativeToCodeStartLineOffset).ToString().PadLeft(lineColumnWidth, '0')}.{new string(' ', 4)}{sc.Lines[line]}")
                    );
                }

                if (sref.IsClrLocation)
                {
                    location = "[clr]";
                    errorSource = null;
                }
                else if (sref.FromLine == sref.ToLine)
                {
                    if (sref.FromChar == sref.ToChar)
                    {
                        location = $"{sc.Name}:({sref.FromLine - chunkRelativeToCodeStartLineOffset},{sref.FromChar})";
                    }
                    else
                    {
                        location = $"{sc.Name}:({sref.FromLine - chunkRelativeToCodeStartLineOffset},{sref.FromChar}-{sref.ToChar})";
                    }
                    errorSource = GetLinesToPrint(sref.FromLine, sref.ToLine);
                }
                else
                {
                    location = $"{sc.Name}:({sref.FromLine - chunkRelativeToCodeStartLineOffset},{sref.FromChar}-{sref.ToLine - chunkRelativeToCodeStartLineOffset},{sref.ToChar})";
                    errorSource = GetLinesToPrint(sref.FromLine, sref.ToLine);
                }
                LogError(location, errorSource);
            }
            else
            {
                LogError();
            }

            if (terminate) { Terminate(); }
        }

        private void Terminate()
        {
            script = null;
            onItemLoaded = DynValue.Nil;
            onMapLoaded = DynValue.Nil;
            onSave = DynValue.Nil;
            update = DynValue.Nil;
            inp = DynValue.Nil;
            outSender = DynValue.Nil;
            inpSenders = DynValue.Nil;
            netReceive = DynValue.Nil;
        }

        public override bool UpdateWhenInactive => true;

        [InGameEditable, Serialize(true, IsPropertySaveable.Yes, alwaysUseInstanceValues: true, translationTextTag: "sp.")]
        public bool CompileInSingleplayer { get; set; }

        [InGameEditable, Serialize(true, IsPropertySaveable.Yes, alwaysUseInstanceValues: true, translationTextTag: "sp.")]
        public bool CompileOnClientInMultiplayer { get; set; }

        [InGameEditable, Serialize(true, IsPropertySaveable.Yes, alwaysUseInstanceValues: true, translationTextTag: "sp.")]
        public bool CompileOnServerInMultiplayer { get; set; }

        private bool AllowCompile => GameMain.IsSingleplayer
            ? CompileInSingleplayer
            : GameMain.NetworkMember.IsClient
                ? CompileOnClientInMultiplayer
                : CompileOnServerInMultiplayer;

        [InGameEditable, Serialize("", IsPropertySaveable.Yes, description: "The currently stored value.", alwaysUseInstanceValues: true)]
        public string Memory
        {
            get { return memory; }
            set
            {
                if (memory == value) { return; }
                memory = value;
            }
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Can the properties of the component be edited in-game in multiplayer. Use to prevent clients uploading a malicious code to the server."), Editable()]
        public bool AllowInGameEditingInMultiplayer
        {
            get;
            set;
        }

        [InGameEditable, Serialize("", IsPropertySaveable.Yes, description: "A lua chunk to be complied.", alwaysUseInstanceValues: true)]
        public string Chunk
        {
            get => chunk;
            set
            {
                if (chunk == value) { return; }
                Terminate();
                chunk = value;
                if (chunk.IsNullOrEmpty()) { return; }

#if CLIENT
                if (SubEditorScreen.IsSubEditor()) { return; }
#endif

                if (!AllowCompile) { return; }

                try
                {
                    if (GameMain.NetworkMember?.IsClient ?? false)
                    {
                        // Sandboxing
                        // The following modules are prohibited:
                        // LoadMethods, The load methods: "load", "loadsafe", "loadfile", "loadfilesafe", "dofile" and "require"
                        // IO, The methods of "io" and "file" packages
                        // OS_System, The methods of "os" package excluding those listed for OS_Time
                        script = new Script(CoreModules.Preset_SoftSandbox | CoreModules.Debug & (~(CoreModules.LoadMethods | CoreModules.IO | CoreModules.OS_System)));
                        script.Options.DebugPrint = (string o) => LuaCsLogger.LogMessage(o);
                        script.Options.CheckThreadAccess = false;

                        if (Path.GetDirectoryName(item.Prefab?.ContentPackage?.Path) is string packagePath)
                        {
                            LuaScriptLoader scriptLoader = new();
                            scriptLoader.ModulePaths = new string[] { };
                            script.Options.ScriptLoader = scriptLoader;
                            script.Globals["setmodulepaths"] = (Action<string[]>)(str => scriptLoader.ModulePaths = str);
                            script.Globals["require"] = (Func<string, Table, DynValue>)new LuaRequire(script).Require;
                            script.Globals["LuaUserData"] = UserData.CreateStatic<LuaUserData>();

                            string entryPath = Path.Combine(packagePath, "RuyiLuaComponent/Sandboxing/entry.lua");

                            script.LoadFile(entryPath).Function.Call(Path.GetDirectoryName(Path.GetFullPath(entryPath)));

                            script.Globals.Remove("setmodulepaths");
                            script.Globals.Remove("require");
                            script.Globals.Remove("LuaUserData");
                        }
                    }
                    else
                    {
                        script = GameMain.LuaCs.Lua;
                    }

                    var initialize = script.DoString($@"
return function(_)
    local {LOCAL_NAME_IS_CLIENT} = _.{LOCAL_NAME_IS_CLIENT}
    local {LOCAL_NAME_IS_SERVER} = _.{LOCAL_NAME_IS_SERVER}
    local {LOCAL_NAME_IS_SINGLEPLAYER} = _.{LOCAL_NAME_IS_SINGLEPLAYER}
    local {LOCAL_NAME_IS_MULTIPLAYER} = _.{LOCAL_NAME_IS_MULTIPLAYER}
    local {LOCAL_NAME_GET_TOTAL_TIME} = _.{LOCAL_NAME_GET_TOTAL_TIME}
    local {LOCAL_NAME_RUYI_LUA} = _.{LOCAL_NAME_RUYI_LUA}
    local {LOCAL_NAME_OUT} = _.{LOCAL_NAME_OUT}
    local {LOCAL_NAME_CLEAR} = _.{LOCAL_NAME_CLEAR}
    local {LOCAL_NAME_READ_MEMORY} = _.{LOCAL_NAME_READ_MEMORY}
    local {LOCAL_NAME_WRITE_MEMORY} = _.{LOCAL_NAME_WRITE_MEMORY}
    local {LOCAL_NAME_NET_SEND} = _.{LOCAL_NAME_NET_SEND}
    local {UPVALUE_NAME_ON_ITEM_LOADED}
    local {UPVALUE_NAME_ON_MAP_LOADED}
    local {UPVALUE_NAME_ON_SAVE}
    local {UPVALUE_NAME_UPDATE}
    local {UPVALUE_NAME_INP}
    local {UPVALUE_NAME_OUT_SENDER}
    local {UPVALUE_NAME_INP_SENDERS}
    local {UPVALUE_NAME_NET_RECEIVE}
{chunk}
    return function()
        return
            {UPVALUE_NAME_ON_ITEM_LOADED},
            {UPVALUE_NAME_ON_MAP_LOADED},
            {UPVALUE_NAME_ON_SAVE},
            {UPVALUE_NAME_UPDATE},
            {UPVALUE_NAME_INP},
            {UPVALUE_NAME_OUT_SENDER},
            {UPVALUE_NAME_INP_SENDERS},
            {UPVALUE_NAME_NET_RECEIVE}
    end
end", codeFriendlyName: null);

                    var args = new Table(script);
                    args[LOCAL_NAME_IS_CLIENT] = IsClient;
                    args[LOCAL_NAME_IS_SERVER] = IsServer;
                    args[LOCAL_NAME_IS_SINGLEPLAYER] = GameMain.IsSingleplayer;
                    args[LOCAL_NAME_IS_MULTIPLAYER] = GameMain.IsMultiplayer;
                    args[LOCAL_NAME_GET_TOTAL_TIME] = DynValue.NewCallback((ctx, args) => DynValue.NewNumber(Timing.TotalTime));
                    args[LOCAL_NAME_RUYI_LUA] = this;
                    args[LOCAL_NAME_OUT] = UserData.Create(this, new OutDescriptor());
                    args[LOCAL_NAME_CLEAR] = DynValue.NewCallback(clear);
                    args[LOCAL_NAME_READ_MEMORY] = DynValue.NewCallback(readMemory);
                    args[LOCAL_NAME_WRITE_MEMORY] = DynValue.NewCallback(writeMemory);
                    args[LOCAL_NAME_NET_SEND] = DynValue.NewCallback(netSend);
                    var internalClosure = script.Call(initialize, DynValue.NewTable(args)).Function;

                    Dictionary<string, DynValue> nameMapUpvalue = new(
                        Enumerable.Range(0, internalClosure.GetUpvaluesCount())
                        .Select(i =>
                            KeyValuePair.Create(internalClosure.GetUpvalueName(i), internalClosure.GetUpvalue(i))
                        )
                    );
                    onItemLoaded = nameMapUpvalue[UPVALUE_NAME_ON_ITEM_LOADED];
                    onMapLoaded = nameMapUpvalue[UPVALUE_NAME_ON_MAP_LOADED];
                    onSave = nameMapUpvalue[UPVALUE_NAME_ON_SAVE];
                    update = nameMapUpvalue[UPVALUE_NAME_UPDATE];
                    inp = nameMapUpvalue[UPVALUE_NAME_INP];
                    outSender = nameMapUpvalue[UPVALUE_NAME_OUT_SENDER];
                    inpSenders = nameMapUpvalue[UPVALUE_NAME_INP_SENDERS];
                    netReceive = nameMapUpvalue[UPVALUE_NAME_NET_RECEIVE];
                }
                catch (SyntaxErrorException e)
                {
                    HandleException("compile", e, terminate: true);
                }
                catch (ScriptRuntimeException e)
                {
                    HandleException("compile", e, terminate: true);
                }
            }
        }

        static RuyiLuaComponent()
        {
            if (!UserData.IsTypeRegistered<RuyiLuaComponent>())
            {
                UserData.RegisterType<RuyiLuaComponent>();
            }
        }

        public RuyiLuaComponent(Item item, ContentXElement element) : base(item, element) { }

        public class OutDescriptor : IUserDataDescriptor
        {
            public string Name => "out";

            public Type Type => typeof(RuyiLuaComponent);

            public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
            {
                throw new ScriptRuntimeException("__index metamethod not implemented");
            }

            public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
            {
                RuyiLuaComponent? ruyiLua = obj as RuyiLuaComponent;

                if (ruyiLua is null)
                {
                    throw new ScriptRuntimeException("unknown behavior!");
                }

                if (ruyiLua.script is null)
                {
                    throw new ScriptRuntimeException("out is prohibited when no script!");
                }

                if (index.Type != DataType.Number)
                {
                    throw new ScriptRuntimeException($"pin must be an 'integer', but got '{index.Type}'!");
                }

                if (index.Number != Math.Truncate(index.Number))
                {
                    throw new ScriptRuntimeException($"pin must be an 'integer', but got '{index.Number}'!");
                }

                int pin = (int)index.Number;

                if (!ruyiLua.outPinMappingConnection.TryGetValue(pin, out var connection))
                {
                    throw new ScriptRuntimeException($"invalid pin {pin}!");
                }

                ruyiLua.item.SendSignal(new Signal(value.Type switch
                {
                    DataType.Number => value.Number.ToString(CultureInfo.InvariantCulture),
                    DataType.String => value.String,
                    _ => throw new ScriptRuntimeException($"pin {pin} outputs an invalid value type '{value.Type}'!")
                }, sender: ruyiLua.outSender.IsNotNil() ? ruyiLua.outSender.ToObject<Character>() : null), connection);

                return true;
            }

            public string AsString(object obj)
            {
                return nameof(RuyiLuaComponent);
            }

            public DynValue MetaIndex(Script script, object obj, string metaname)
            {
                throw new ScriptRuntimeException($"{metaname} metamethod not implemented");
            }

            public bool IsTypeCompatible(Type type, object obj)
            {
                return Framework.Do.IsInstanceOfType(type, obj);
            }
        }

        private static DynValue clear(ScriptExecutionContext executionContext, CallbackArguments args)
        {
            args.AsType(0, nameof(clear), DataType.Table, false).Table.Clear();
            return DynValue.Nil;
        }

        private DynValue readMemory(ScriptExecutionContext executionContext, CallbackArguments args)
        {
            return DynValue.NewString(Memory);
        }

        private DynValue writeMemory(ScriptExecutionContext executionContext, CallbackArguments args)
        {
            string value = args.AsType(0, nameof(writeMemory), DataType.String, false).String;
            Memory = value;
            return DynValue.Nil;
        }

#if SERVER
        public void ServerEventRead(IReadMessage msg, Client c)
#elif CLIENT
        public void ClientEventRead(IReadMessage msg, float sendingTime)
#endif
        {
            string message = msg.ReadString();
            if (script is null || netReceive.Type != DataType.Function) { return; }

            try
            {
#if SERVER
                script.Call(netReceive, DynValue.NewString(message), c);
#elif CLIENT
                script.Call(netReceive, DynValue.NewString(message));
#endif
            }
            catch (ScriptRuntimeException e)
            {
                HandleException(nameof(netReceive), e, terminate: true);
            }
        }

#if SERVER
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData)
#elif CLIENT
        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData)
#endif
        {
            msg.WriteString(ExtractEventData<EventData>(extraData).Message);
        }

        private DynValue netSend(ScriptExecutionContext executionContext, CallbackArguments args)
        {
            if (GameMain.IsSingleplayer)
            {
                throw new ScriptRuntimeException($"function netSend is not available in singleplayer.");
            }

            string message = args.AsType(0, nameof(netSend), DataType.String, false).String;
#if SERVER
            item.CreateServerEvent(this, new EventData(message));
#elif CLIENT
            item.CreateClientEvent(this, new EventData(message));
#endif
            return DynValue.Nil;
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();

            if (GameMain.IsMultiplayer && !AllowInGameEditingInMultiplayer)
            {
                AllowInGameEditing = false;
            }

            foreach (var connection in item.Connections)
            {
                if (outRegex.Match(connection.Name) is { Success: true } outMatch)
                {
                    outPinMappingConnection.Add(int.Parse(outMatch.Groups[1].Value), connection);
                }
                else if (inpRegex.Match(connection.Name) is { Success: true } inpMatch)
                {
                    inpConnectionMappingPin.Add(connection, int.Parse(inpMatch.Groups[1].Value));
                }
            }

            if (script is null || onItemLoaded.Type != DataType.Function) { return; }

            try
            {
                script.Call(onItemLoaded);
            }
            catch (ScriptRuntimeException e)
            {
                HandleException(nameof(onItemLoaded), e, terminate: true);
            }
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();

            if (script is null || onMapLoaded.Type != DataType.Function) { return; }

            try
            {
                script.Call(onMapLoaded);
            }
            catch (ScriptRuntimeException e)
            {
                HandleException(nameof(onMapLoaded), e, terminate: true);
            }
        }

        public override XElement Save(XElement parentElement)
        {
            if (script is not null && onSave.Type == DataType.Function)
            {
                try
                {
                    script.Call(onSave);
                }
                catch (ScriptRuntimeException e)
                {
                    HandleException(nameof(onSave), e, terminate: true);
                }
            }

            return base.Save(parentElement);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (script is null || update.Type != DataType.Function) { return; }

            try
            {
                script.Call(update, DynValue.NewNumber(deltaTime));
            }
            catch (ScriptRuntimeException e)
            {
                HandleException(nameof(update), e, terminate: true);
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (script is null || inp.Type == DataType.Nil) { return; }

            if (inpConnectionMappingPin.TryGetValue(connection, out int pin))
            {
                if (inpSenders.Type == DataType.Table && signal.sender is Character signalSender)
                {
                    inpSenders.Table.Set(pin, DynValue.FromObject(script, signalSender));
                }

                var value = double.TryParse(
                    signal.value,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double num
                ) ? DynValue.NewNumber(num) : DynValue.NewString(signal.value);

                switch (inp.Type)
                {
                    case DataType.Table:
                        inp.Table.Set(pin, value);
                        break;
                    case DataType.Function:
                        try
                        {
                            script.Call(inp, DynValue.NewNumber(pin), value);
                        }
                        catch (ScriptRuntimeException e)
                        {
                            HandleException(nameof(inp), e, terminate: true);
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private readonly struct EventData : ItemComponent.IEventData
        {
            public readonly string Message;

            public EventData(string message)
            {
                Message = message;
            }
        }
    }
}

