using System;
using System.Runtime.InteropServices;

namespace StoneshardMP.Net.Steam;

// One of Steam's callbacks, heard: Steam's classic CCallbackBase, made by hand (a C++ object - no unsafe code here), and
// registered with SteamAPI_RegisterCallback. The game's Steamworks extension calls SteamAPI_RunCallbacks each frame, on
// the game's thread, which runs ours with the rest - so it comes on the game's thread, inside the extension's call:
// the handler only reads what Steam gives it (the callback's struct, valid for the call), and never throws out.
// The object: its vtable, its flags (a byte at 8), its callback id (at 12). The vtable, as MSVC lays a class out: the two
// overloads of Run in reverse - Run(param, ioFailure, call), then Run(param) - and GetCallbackSizeBytes.
internal sealed class SteamCallback : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void RunResult(IntPtr self, IntPtr param, [MarshalAs(UnmanagedType.U1)] bool ioFailure, ulong call);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void Run(IntPtr self, IntPtr param);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int Size(IntPtr self);

    private readonly int _size;
    private readonly Action<IntPtr> _handler;
    // (Kept: Steam calls them for as long as it's registered.)
    private readonly RunResult _runResult;
    private readonly Run _run;
    private readonly Size _sizeOf;
    private readonly IntPtr _vtable, _object;
    private bool _registered;

    /// <summary>Hears Steam's callback <paramref name="id"/> (its struct <paramref name="size"/> bytes), each time with
    /// a pointer to that struct.</summary>
    public SteamCallback(int id, int size, Action<IntPtr> handler)
    {
        _size = size;
        _handler = handler;
        _runResult = (_, param, _, _) => Handle(param);
        _run = (_, param) => Handle(param);
        _sizeOf = _ => _size;
        _vtable = Marshal.AllocHGlobal(3 * IntPtr.Size);
        Marshal.WriteIntPtr(_vtable, 0, Marshal.GetFunctionPointerForDelegate(_runResult));
        Marshal.WriteIntPtr(_vtable, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_run));
        Marshal.WriteIntPtr(_vtable, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_sizeOf));
        _object = Marshal.AllocHGlobal(16);
        Marshal.WriteIntPtr(_object, 0, _vtable);
        Marshal.WriteInt64(_object, 8, 0);
        Marshal.WriteInt32(_object, 12, id);
        SteamApi.RegisterCallback(_object, id);
        _registered = true;
    }

    private void Handle(IntPtr param)
    {
        try
        {
            if (param != IntPtr.Zero)
                _handler(param);
        }
        catch (Exception)
        {
            // (Nothing may go back into Steam's call.)
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            SteamApi.UnregisterCallback(_object);
            _registered = false;
        }
        Marshal.FreeHGlobal(_object);
        Marshal.FreeHGlobal(_vtable);
    }
}
