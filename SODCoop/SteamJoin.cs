using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Injection;

namespace SodCoop
{
    internal sealed class SteamJoinCallback : Steamworks.Callback
    {
        internal const int Id = 337;

        public SteamJoinCallback(IntPtr p) : base(p) { }
        public SteamJoinCallback() : base(ClassInjector.DerivedConstructorPointer<SteamJoinCallback>()) { ClassInjector.DerivedConstructorBody(this); }

        public override bool IsGameServer { get { return false; } }
        public override Il2CppSystem.Type GetCallbackType() { return null; }
        public override void SetUnregistered() { }

        public override void OnRunCallback(IntPtr pvParam)
        {
            try
            {
                ulong friend = (ulong)Marshal.ReadInt64(pvParam, 0);
                string connect = Marshal.PtrToStringUTF8(pvParam + 8) ?? "";
                SteamLink.OnJoinRequest(friend, connect);
            }
            catch (Exception e) { Plugin.L.LogWarning("[STEAM] запрос на вход: " + e.Message); }
        }
    }
}
